using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using MeetingRecorder.CaptureWorker.Audio;
using MeetingRecorder.CaptureWorker.State;
using MeetingRecorder.Shared.Pipes;

namespace MeetingRecorder.CaptureWorker.Services;

public sealed class CapturePipeServer
{
    private readonly ConcurrentDictionary<string, CaptureSessionState> _sessions = new();

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(
                PipeChannel.Name,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous
            );

            await pipe.WaitForConnectionAsync(cancellationToken);

            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true)
            {
                AutoFlush = true
            };

            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var envelope = JsonSerializer.Deserialize<WorkerCommandEnvelope>(line, PipeJson.Options)
                ?? throw new InvalidOperationException("Unable to deserialize command envelope.");

            var response = envelope.CommandType switch
            {
                "BeginSession" => BeginSession(envelope.Payload.Deserialize<BeginSessionCommand>(PipeJson.Options)
                    ?? throw new InvalidOperationException("Invalid BeginSession payload.")),
                "PauseSession" => PauseSession(envelope.Payload.Deserialize<PauseSessionCommand>(PipeJson.Options)
                    ?? throw new InvalidOperationException("Invalid PauseSession payload.")),
                "ResumeSession" => ResumeSession(envelope.Payload.Deserialize<ResumeSessionCommand>(PipeJson.Options)
                    ?? throw new InvalidOperationException("Invalid ResumeSession payload.")),
                "StopSession" => StopSession(envelope.Payload.Deserialize<StopSessionCommand>(PipeJson.Options)
                    ?? throw new InvalidOperationException("Invalid StopSession payload.")),
                "GetStatus" => GetStatus(envelope.Payload.Deserialize<GetStatusCommand>(PipeJson.Options)
                    ?? throw new InvalidOperationException("Invalid GetStatus payload.")),
                _ => new WorkerEventEnvelope("Error", string.Empty, $"Unknown command: {envelope.CommandType}")
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(response, PipeJson.Options));
        }
    }

    private WorkerEventEnvelope BeginSession(BeginSessionCommand command)
    {
        Directory.CreateDirectory(command.OutputDirectory);
        var recordingPath = Path.Combine(command.OutputDirectory, "recording.wav");
        var micPath = Path.Combine(command.OutputDirectory, "mic.wav");

        var state = new CaptureSessionState
        {
            SessionId = command.SessionId,
            OutputDirectory = command.OutputDirectory,
            RecordingPath = recordingPath,
            MicPath = micPath,
            Title = command.Title,
            Platform = command.Platform,
            SourceType = command.SourceType,
            ExeName = command.ExeName,
            ProcessId = command.ProcessId,
            WindowHandle = command.WindowHandle,
            WindowTitle = command.WindowTitle,
            AudioCaptureMode = "system_loopback",
            CaptureBackend = "wasapi-audio",
            IsRecording = true
        };

        if (command.PreferProcessLoopback)
        {
            AddWarning(state.Warnings, "Process-scoped audio capture is not implemented yet. Falling back to system loopback.");
        }

        if (!TryStartCapture(state, appendToExisting: false))
        {
            state.IsRecording = false;
            WriteCaptureState(command.OutputDirectory, state, "error");
            return new WorkerEventEnvelope("Error", command.SessionId, "Unable to start WASAPI audio capture.");
        }

        _sessions[command.SessionId] = state;
        WriteCaptureState(command.OutputDirectory, state, "recording");

        return new WorkerEventEnvelope(
            "Started",
            command.SessionId,
            state.Warnings.FirstOrDefault() ?? "Native WASAPI audio capture started.",
            ProgressPercent: 0,
            AudioCaptureMode: state.AudioCaptureMode
        );
    }

    private WorkerEventEnvelope PauseSession(PauseSessionCommand command)
    {
        if (!_sessions.TryGetValue(command.SessionId, out var state))
        {
            return new WorkerEventEnvelope("Error", command.SessionId, "Session not found.");
        }

        if (state.IsPaused)
        {
            return new WorkerEventEnvelope("Paused", command.SessionId, "Session is already paused.", ProgressPercent: 50, AudioCaptureMode: state.AudioCaptureMode);
        }

        StopCapture(state.LoopbackCapture, state.Warnings, "meeting audio");
        StopCapture(state.MicrophoneCapture, state.Warnings, "microphone");
        state.LoopbackCapture = null;
        state.MicrophoneCapture = null;
        state.IsRecording = false;
        state.IsPaused = true;
        WriteCaptureState(state.OutputDirectory, state, "paused", command.Reason);
        return new WorkerEventEnvelope(
            "Paused",
            command.SessionId,
            state.Warnings.FirstOrDefault() ?? "Native WASAPI audio capture paused.",
            ProgressPercent: 50,
            AudioCaptureMode: state.AudioCaptureMode
        );
    }

    private WorkerEventEnvelope ResumeSession(ResumeSessionCommand command)
    {
        if (!_sessions.TryGetValue(command.SessionId, out var state))
        {
            return new WorkerEventEnvelope("Error", command.SessionId, "Session not found.");
        }

        if (!state.IsPaused)
        {
            return new WorkerEventEnvelope("Progress", command.SessionId, "Session is already recording.", ProgressPercent: 10, AudioCaptureMode: state.AudioCaptureMode);
        }

        if (!TryStartCapture(state, appendToExisting: true))
        {
            state.IsPaused = true;
            state.IsRecording = false;
            WriteCaptureState(state.OutputDirectory, state, "paused", "resume-failed");
            return new WorkerEventEnvelope("Error", command.SessionId, "Unable to resume WASAPI audio capture.");
        }

        WriteCaptureState(state.OutputDirectory, state, "recording");
        return new WorkerEventEnvelope(
            "Started",
            command.SessionId,
            state.Warnings.FirstOrDefault() ?? "Native WASAPI audio capture resumed.",
            ProgressPercent: 10,
            AudioCaptureMode: state.AudioCaptureMode
        );
    }

    private WorkerEventEnvelope StopSession(StopSessionCommand command)
    {
        if (!_sessions.TryRemove(command.SessionId, out var state))
        {
            return new WorkerEventEnvelope("Error", command.SessionId, "Session not found.");
        }

        StopCapture(state.LoopbackCapture, state.Warnings, "meeting audio");
        StopCapture(state.MicrophoneCapture, state.Warnings, "microphone");
        state.LoopbackCapture = null;
        state.MicrophoneCapture = null;
        state.IsRecording = false;
        state.IsPaused = false;
        WriteCaptureState(state.OutputDirectory, state, "stopped", command.Reason);
        return new WorkerEventEnvelope(
            "Stopped",
            command.SessionId,
            state.Warnings.FirstOrDefault(),
            ProgressPercent: 100,
            AudioCaptureMode: state.AudioCaptureMode
        );
    }

    private WorkerEventEnvelope GetStatus(GetStatusCommand command)
    {
        if (!_sessions.TryGetValue(command.SessionId, out var state))
        {
            return new WorkerEventEnvelope("Error", command.SessionId, "Session not found.");
        }

        return new WorkerEventEnvelope(
            state.IsPaused ? "Paused" : state.IsRecording ? "Progress" : "Stopped",
            command.SessionId,
            state.Warnings.FirstOrDefault(),
            ProgressPercent: state.IsPaused ? 50 : state.IsRecording ? 50 : 100,
            AudioCaptureMode: state.AudioCaptureMode
        );
    }

    private static bool TryStartCapture(CaptureSessionState state, bool appendToExisting)
    {
        WasapiAudioCapture? loopbackCapture = null;
        WasapiAudioCapture? microphoneCapture = null;
        var startedAnyCapture = false;

        try
        {
            loopbackCapture = new WasapiAudioCapture(
                AudioDataFlow.Render,
                state.RecordingPath,
                useLoopback: true,
                label: "meeting audio",
                appendToExisting: appendToExisting
            );
            loopbackCapture.Start();
            startedAnyCapture = true;
        }
        catch (Exception error)
        {
            AddWarning(state.Warnings, $"Unable to capture meeting audio via system loopback: {error.Message}");
            loopbackCapture?.Dispose();
            loopbackCapture = null;
        }

        try
        {
            microphoneCapture = new WasapiAudioCapture(
                AudioDataFlow.Capture,
                state.MicPath,
                useLoopback: false,
                label: "microphone",
                appendToExisting: appendToExisting
            );
            microphoneCapture.Start();
            startedAnyCapture = true;
        }
        catch (Exception error)
        {
            AddWarning(state.Warnings, $"Unable to capture microphone audio: {error.Message}");
            microphoneCapture?.Dispose();
            microphoneCapture = null;
        }

        if (!startedAnyCapture)
        {
            return false;
        }

        state.LoopbackCapture = loopbackCapture;
        state.MicrophoneCapture = microphoneCapture;
        state.IsRecording = true;
        state.IsPaused = false;
        return true;
    }

    private static void WriteCaptureState(string outputDirectory, CaptureSessionState state, string status, string? reason = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["sessionId"] = state.SessionId,
            ["title"] = state.Title,
            ["platform"] = state.Platform,
            ["sourceType"] = state.SourceType,
            ["exeName"] = state.ExeName,
            ["processId"] = state.ProcessId,
            ["windowHandle"] = state.WindowHandle,
            ["windowTitle"] = state.WindowTitle,
            ["audioCaptureMode"] = state.AudioCaptureMode,
            ["captureBackend"] = state.CaptureBackend,
            ["startedAt"] = state.StartedAt,
            ["recordingPath"] = state.RecordingPath,
            ["micPath"] = state.MicPath,
            ["warnings"] = state.Warnings,
            ["isPaused"] = state.IsPaused,
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O")
        };

        if (!string.IsNullOrWhiteSpace(reason))
        {
            payload["reason"] = reason;
        }

        var captureJsonPath = Path.Combine(outputDirectory, "capture.json");
        File.WriteAllText(captureJsonPath, JsonSerializer.Serialize(payload, PipeJson.Options));
    }

    private static void StopCapture(WasapiAudioCapture? capture, List<string> warnings, string label)
    {
        if (capture is null)
        {
            return;
        }

        try
        {
            capture.Stop();
        }
        catch (Exception error)
        {
            warnings.Add($"Error while stopping {label}: {error.Message}");
        }
        finally
        {
            capture.Dispose();
        }
    }

    private static void AddWarning(List<string> warnings, string warning)
    {
        if (warnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        warnings.Add(warning);
    }
}
