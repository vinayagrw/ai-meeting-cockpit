using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using MeetingRecorder.Shared.Configuration;
using MeetingRecorder.Shared.Pipes;
using MeetingRecorder.Shared.Sessions;
using MeetingRecorder.Windows.Capture;
using MeetingRecorder.Windows.Models;
using MeetingRecorder.Windows.Shell;

namespace MeetingRecorder.Windows.Interop;

public sealed class CaptureWorkerClient
{
    private static readonly Dictionary<string, LocalCaptureSession> LocalFallbackSessions = [];
    private static readonly Lock LocalFallbackLock = new();

    public async Task<WorkerEventEnvelope> BeginSessionAsync(SessionManifest manifest, CancellationToken cancellationToken = default)
    {
        if (!ShouldUseCaptureWorker())
        {
            return BeginLocalFallback(manifest);
        }

        try
        {
            await EnsureWorkerProcessStartedAsync(cancellationToken);
            var command = new BeginSessionCommand(
                manifest.SessionId,
                Path.GetDirectoryName(manifest.Paths.Log) ?? string.Empty,
                manifest.Title,
                manifest.Platform,
                manifest.SourceType,
                manifest.ProcessId,
                manifest.ExeName,
                manifest.WindowHandle,
                manifest.WindowTitle,
                manifest.ProcessId is not null
            );

            var response = await SendCommandWithRetryAsync("BeginSession", command, cancellationToken);
            if (response.EventType == "Error")
            {
                throw new InvalidOperationException(response.Message ?? "Capture worker returned an error while starting.");
            }

            return response;
        }
        catch (Exception error)
        {
            ShellDiagnostics.Log($"Capture worker unavailable for session {manifest.SessionId}. Falling back to in-process WASAPI capture.", error);
            return BeginLocalFallback(manifest);
        }
    }

    public async Task<WorkerEventEnvelope> PauseSessionAsync(string sessionId, string reason, CancellationToken cancellationToken = default)
    {
        lock (LocalFallbackLock)
        {
            if (LocalFallbackSessions.TryGetValue(sessionId, out var session))
            {
                ShellDiagnostics.Log($"Pausing local fallback session {sessionId}.");
                StopLocalCapture(session.LoopbackCapture, session.Warnings, "meeting audio");
                StopLocalCapture(session.MicrophoneCapture, session.Warnings, "microphone");
                session.ScreenCapture?.StopSegment();
                session.LoopbackCapture = null;
                session.MicrophoneCapture = null;
                session.IsPaused = true;
                WriteLocalCaptureState(session, "paused", reason);
                return new WorkerEventEnvelope(
                    "Paused",
                    sessionId,
                    session.Warnings.FirstOrDefault() ?? "Local WASAPI fallback session paused.",
                    ProgressPercent: 50,
                    AudioCaptureMode: session.AudioCaptureMode
                );
            }
        }

        if (!ShouldUseCaptureWorker())
        {
            throw new InvalidOperationException("The recording session is not active in the current app instance.");
        }

        var response = await SendCommandWithRetryAsync("PauseSession", new PauseSessionCommand(sessionId, reason), cancellationToken);
        if (response.EventType == "Error")
        {
            throw new InvalidOperationException(response.Message ?? "Capture worker returned an error while pausing.");
        }

        return response;
    }

    public async Task<WorkerEventEnvelope> ResumeSessionAsync(SessionManifest manifest, CancellationToken cancellationToken = default)
    {
        lock (LocalFallbackLock)
        {
            if (LocalFallbackSessions.TryGetValue(manifest.SessionId, out var session))
            {
                ShellDiagnostics.Log($"Resuming local fallback session {manifest.SessionId}.");
                StartLocalFallbackCapture(session, appendToExisting: true);
                WriteLocalCaptureState(session, "recording");
                return new WorkerEventEnvelope(
                    "Started",
                    manifest.SessionId,
                    session.Warnings.FirstOrDefault() ?? "Local WASAPI fallback session resumed.",
                    ProgressPercent: 10,
                    AudioCaptureMode: session.AudioCaptureMode
                );
            }
        }

        if (!ShouldUseCaptureWorker())
        {
            throw new InvalidOperationException("The recording session is not active in the current app instance.");
        }

        var response = await SendCommandWithRetryAsync("ResumeSession", new ResumeSessionCommand(manifest.SessionId), cancellationToken);
        if (response.EventType == "Error")
        {
            throw new InvalidOperationException(response.Message ?? "Capture worker returned an error while resuming.");
        }

        return response;
    }

    public async Task<WorkerEventEnvelope> StopSessionAsync(string sessionId, string reason, CancellationToken cancellationToken = default)
    {
        lock (LocalFallbackLock)
        {
            if (LocalFallbackSessions.TryGetValue(sessionId, out var session))
            {
                LocalFallbackSessions.Remove(sessionId);
                ShellDiagnostics.Log($"Stopping local fallback session {sessionId}.");
                StopLocalCapture(session.LoopbackCapture, session.Warnings, "meeting audio");
                StopLocalCapture(session.MicrophoneCapture, session.Warnings, "microphone");
                session.LoopbackCapture = null;
                session.MicrophoneCapture = null;
                var finalized = session.ScreenCapture is not null
                    ? session.ScreenCapture.FinalizeRecording()
                    : ScreenRecordingSession.FinalizeAudioOnlyRecording(
                        ResolveSessionRoot(session.Manifest),
                        session.Manifest.Paths.Recording,
                        session.Manifest.Paths.SystemAudio,
                        session.Manifest.Paths.Mic,
                        session.Warnings);
                if (!finalized)
                {
                    AddWarning(
                        session.Warnings,
                        session.ScreenCapture is null
                            ? "Audio recording was not finalized. Check ffmpeg availability and audio capture warnings."
                            : "Screen recording was not finalized. Check ffmpeg availability and screen capture warnings.");
                }
                session.IsPaused = false;
                WriteLocalCaptureState(session, "stopped", reason);
                return new WorkerEventEnvelope(
                    "Stopped",
                    sessionId,
                    session.Warnings.FirstOrDefault() ?? "Local WASAPI fallback session stopped.",
                    ProgressPercent: 100,
                    AudioCaptureMode: session.AudioCaptureMode
                );
            }
        }

        if (!ShouldUseCaptureWorker())
        {
            throw new InvalidOperationException("The recording session is not active in the current app instance.");
        }

        var response = await SendCommandWithRetryAsync("StopSession", new StopSessionCommand(sessionId, reason), cancellationToken);
        if (response.EventType == "Error")
        {
            throw new InvalidOperationException(response.Message ?? "Capture worker returned an error while stopping.");
        }

        return response;
    }

    public Task<WorkerEventEnvelope> SwitchToDisplayAsync(SessionManifest manifest, DisplayCaptureTarget display, CancellationToken cancellationToken = default)
    {
        lock (LocalFallbackLock)
        {
            if (LocalFallbackSessions.TryGetValue(manifest.SessionId, out var session))
            {
                if (!EnsureDisplayCaptureActive(session, display))
                {
                    throw new InvalidOperationException("Unable to switch the active recording to the selected screen.");
                }

                WriteLocalCaptureState(session, "recording");
                return Task.FromResult(new WorkerEventEnvelope(
                    "Progress",
                    manifest.SessionId,
                    $"Switched recording to {display.DisplayName}.",
                    ProgressPercent: 25,
                    AudioCaptureMode: session.AudioCaptureMode
                ));
            }
        }

        throw new InvalidOperationException("Live capture switching is currently available only in the in-process recording path.");
    }

    public Task<WorkerEventEnvelope> SwitchToWindowAsync(SessionManifest manifest, MeetingCandidate candidate, CancellationToken cancellationToken = default)
    {
        lock (LocalFallbackLock)
        {
            if (LocalFallbackSessions.TryGetValue(manifest.SessionId, out var session))
            {
                if (!EnsureWindowCaptureActive(session, candidate))
                {
                    throw new InvalidOperationException("Unable to switch the active recording to the selected window.");
                }

                WriteLocalCaptureState(session, "recording");
                return Task.FromResult(new WorkerEventEnvelope(
                    "Progress",
                    manifest.SessionId,
                    $"Switched recording to {candidate.Title}.",
                    ProgressPercent: 25,
                    AudioCaptureMode: session.AudioCaptureMode
                ));
            }
        }

        throw new InvalidOperationException("Live capture switching is currently available only in the in-process recording path.");
    }

    public Task<WorkerEventEnvelope> GetStatusAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        lock (LocalFallbackLock)
        {
            if (LocalFallbackSessions.TryGetValue(sessionId, out var session))
            {
                return Task.FromResult(new WorkerEventEnvelope(
                    session.IsPaused ? "Paused" : "Progress",
                    sessionId,
                    session.Warnings.FirstOrDefault() ?? "Using in-process WASAPI fallback capture.",
                    ProgressPercent: session.IsPaused ? 50 : 10,
                    AudioCaptureMode: session.AudioCaptureMode
                ));
            }
        }

        if (!ShouldUseCaptureWorker())
        {
            return Task.FromResult(new WorkerEventEnvelope("Error", sessionId, "The recording session is not active in the current app instance."));
        }

        return SendCommandWithRetryAsync("GetStatus", new GetStatusCommand(sessionId), cancellationToken);
    }

    private static bool ShouldUseCaptureWorker()
        => RecorderSettingsProvider.Current.Windows.UseCaptureWorker;

    private static async Task<WorkerEventEnvelope> SendCommandWithRetryAsync<TPayload>(string commandType, TPayload payload, CancellationToken cancellationToken)
    {
        var workerSettings = RecorderSettingsProvider.Current.Windows.CaptureWorker;
        Exception? lastError = null;
        for (var attempt = 0; attempt < workerSettings.CommandRetryAttempts; attempt++)
        {
            try
            {
                return await SendCommandAsync(commandType, payload, cancellationToken);
            }
            catch (Exception error) when (error is TimeoutException or IOException)
            {
                lastError = error;
                if (attempt == 0)
                {
                    await EnsureWorkerProcessStartedAsync(cancellationToken);
                }

                await Task.Delay(workerSettings.CommandRetryDelayMs, cancellationToken);
            }
        }

        throw new InvalidOperationException("Capture worker did not respond in time.", lastError);
    }

    private static async Task<WorkerEventEnvelope> SendCommandAsync<TPayload>(string commandType, TPayload payload, CancellationToken cancellationToken)
    {
        var workerSettings = RecorderSettingsProvider.Current.Windows.CaptureWorker;
        using var pipe = new NamedPipeClientStream(".", PipeChannel.Name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(workerSettings.ConnectTimeoutMs, cancellationToken);

        await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true)
        {
            AutoFlush = true
        };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

        var payloadJson = JsonSerializer.SerializeToElement(payload, PipeJson.Options);
        var command = new WorkerCommandEnvelope(commandType, payloadJson);
        await writer.WriteLineAsync(JsonSerializer.Serialize(command, PipeJson.Options));
        var responseLine = await reader.ReadLineAsync() ?? throw new InvalidOperationException("Capture worker returned no response.");
        return JsonSerializer.Deserialize<WorkerEventEnvelope>(responseLine, PipeJson.Options)
            ?? throw new InvalidOperationException("Unable to deserialize capture worker response.");
    }

    private static async Task EnsureWorkerProcessStartedAsync(CancellationToken cancellationToken)
    {
        var workerSettings = RecorderSettingsProvider.Current.Windows.CaptureWorker;
        if (IsWorkerProcessRunning())
        {
            return;
        }

        var workerPath = ResolveWorkerExecutable();
        if (!string.IsNullOrWhiteSpace(workerPath) && File.Exists(workerPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = workerPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        for (var attempt = 0; attempt < workerSettings.StartupPollAttempts; attempt++)
        {
            if (IsWorkerProcessRunning())
            {
                return;
            }

            await Task.Delay(workerSettings.StartupPollIntervalMs, cancellationToken);
        }

        throw new InvalidOperationException("Capture worker could not be started.");
    }

    private static bool IsWorkerProcessRunning()
    {
        try
        {
            return Process.GetProcessesByName("MeetingRecorder.CaptureWorker").Any();
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveWorkerExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "MeetingRecorder.CaptureWorker.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MeetingRecorder.CaptureWorker", "bin", "Debug", "net10.0-windows", "MeetingRecorder.CaptureWorker.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MeetingRecorder.CaptureWorker", "bin", "Release", "net10.0-windows", "MeetingRecorder.CaptureWorker.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MeetingRecorder.CaptureWorker", "bin", "Debug", "net10.0", "MeetingRecorder.CaptureWorker.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MeetingRecorder.CaptureWorker", "bin", "Release", "net10.0", "MeetingRecorder.CaptureWorker.exe"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static WorkerEventEnvelope BeginLocalFallback(SessionManifest manifest, string? warning = null)
    {
        var sessionDirectory = Path.GetDirectoryName(manifest.Paths.Log) ?? throw new InvalidOperationException("Session output directory is missing.");
        Directory.CreateDirectory(sessionDirectory);

        var session = new LocalCaptureSession
        {
            Manifest = manifest,
            AudioCaptureMode = "system_loopback"
        };
        if (!string.IsNullOrWhiteSpace(warning))
        {
            AddWarning(session.Warnings, warning);
        }

        StartLocalFallbackCapture(session, appendToExisting: false);
        WriteLocalCaptureState(session, "recording");

        lock (LocalFallbackLock)
        {
            LocalFallbackSessions[manifest.SessionId] = session;
        }

        return new WorkerEventEnvelope(
            "Started",
            manifest.SessionId,
            session.Warnings.FirstOrDefault(),
            ProgressPercent: 0,
            AudioCaptureMode: session.AudioCaptureMode
        );
    }

    private static void StartLocalFallbackCapture(LocalCaptureSession session, bool appendToExisting)
    {
        WasapiAudioCapture? loopbackCapture = null;
        WasapiAudioCapture? microphoneCapture = null;
        var startedAnyCapture = false;

        try
        {
            loopbackCapture = new WasapiAudioCapture(
                AudioDataFlow.Render,
                session.Manifest.Paths.SystemAudio ?? session.Manifest.Paths.Recording,
                useLoopback: true,
                label: "meeting audio",
                appendToExisting: appendToExisting
            );
            loopbackCapture.Start();
            startedAnyCapture = true;
        }
        catch (Exception error)
        {
            AddWarning(session.Warnings, $"Unable to capture meeting audio via system loopback: {error.Message}");
            loopbackCapture?.Dispose();
            loopbackCapture = null;
        }

        try
        {
            microphoneCapture = new WasapiAudioCapture(
                AudioDataFlow.Capture,
                session.Manifest.Paths.Mic,
                useLoopback: false,
                label: "microphone",
                appendToExisting: appendToExisting
            );
            microphoneCapture.Start();
            startedAnyCapture = true;
        }
        catch (Exception error)
        {
            AddWarning(session.Warnings, $"Unable to capture microphone audio: {error.Message}");
            microphoneCapture?.Dispose();
            microphoneCapture = null;
        }

        if (session.ScreenCapture is null)
        {
            session.ScreenCapture = CreateScreenCaptureSession(session);
        }

        if (session.ScreenCapture?.StartSegment() == true)
        {
            startedAnyCapture = true;
        }

        if (!startedAnyCapture)
        {
            throw new InvalidOperationException("No audio or screen capture streams could be started.");
        }

        session.LoopbackCapture = loopbackCapture;
        session.MicrophoneCapture = microphoneCapture;
        session.IsPaused = false;
    }

    private static bool EnsureDisplayCaptureActive(LocalCaptureSession session, DisplayCaptureTarget display)
    {
        if (session.ScreenCapture is null)
        {
            var initialOffset = GetCurrentRecordedDuration(session);
            session.ScreenCapture = ScreenRecordingSession.ForDisplay(
                display.Bounds,
                display.DisplayName,
                ResolveSessionRoot(session.Manifest),
                session.Manifest.Paths.Recording,
                session.Manifest.Paths.SystemAudio,
                session.Manifest.Paths.Mic,
                session.Warnings,
                initialOffset);
            session.Manifest.MediaCaptureMode = "screen-and-audio";
            session.Manifest.VideoCaptureMode = "display";
            session.Manifest.DisplayDeviceName = display.DeviceName;
            session.Manifest.DisplayLabel = display.DisplayName;
            session.Manifest.CaptureX = display.Bounds.X;
            session.Manifest.CaptureY = display.Bounds.Y;
            session.Manifest.CaptureWidth = display.Bounds.Width;
            session.Manifest.CaptureHeight = display.Bounds.Height;
            return session.ScreenCapture.StartSegment();
        }

        session.Manifest.MediaCaptureMode = "screen-and-audio";
        session.Manifest.VideoCaptureMode = "display";
        session.Manifest.DisplayDeviceName = display.DeviceName;
        session.Manifest.DisplayLabel = display.DisplayName;
        session.Manifest.CaptureX = display.Bounds.X;
        session.Manifest.CaptureY = display.Bounds.Y;
        session.Manifest.CaptureWidth = display.Bounds.Width;
        session.Manifest.CaptureHeight = display.Bounds.Height;
        return session.ScreenCapture.SwitchToDisplay(display.Bounds, display.DisplayName);
    }

    private static bool EnsureWindowCaptureActive(LocalCaptureSession session, MeetingCandidate candidate)
    {
        if (candidate.WindowHandle == nint.Zero)
        {
            AddWarning(session.Warnings, "The selected window does not have a valid handle for capture switching.");
            return false;
        }

        if (session.ScreenCapture is null)
        {
            var initialOffset = GetCurrentRecordedDuration(session);
            session.ScreenCapture = ScreenRecordingSession.ForWindow(
                candidate.WindowHandle,
                ResolveSessionRoot(session.Manifest),
                session.Manifest.Paths.Recording,
                session.Manifest.Paths.SystemAudio,
                session.Manifest.Paths.Mic,
                session.Warnings,
                initialOffset);
            session.Manifest.MediaCaptureMode = "screen-and-audio";
            session.Manifest.VideoCaptureMode = "window";
            session.Manifest.DisplayDeviceName = null;
            session.Manifest.DisplayLabel = null;
            session.Manifest.CaptureX = null;
            session.Manifest.CaptureY = null;
            session.Manifest.CaptureWidth = null;
            session.Manifest.CaptureHeight = null;
            session.Manifest.SourceType = candidate.SourceType;
            session.Manifest.ExeName = candidate.ProcessName;
            session.Manifest.ProcessId = candidate.ProcessId;
            session.Manifest.WindowHandle = $"0x{candidate.WindowHandle.ToInt64():X}";
            session.Manifest.WindowTitle = candidate.WindowTitle;
            return session.ScreenCapture.StartSegment();
        }

        session.Manifest.MediaCaptureMode = "screen-and-audio";
        session.Manifest.VideoCaptureMode = "window";
        session.Manifest.DisplayDeviceName = null;
        session.Manifest.DisplayLabel = null;
        session.Manifest.CaptureX = null;
        session.Manifest.CaptureY = null;
        session.Manifest.CaptureWidth = null;
        session.Manifest.CaptureHeight = null;
        session.Manifest.SourceType = candidate.SourceType;
        session.Manifest.ExeName = candidate.ProcessName;
        session.Manifest.ProcessId = candidate.ProcessId;
        session.Manifest.WindowHandle = $"0x{candidate.WindowHandle.ToInt64():X}";
        session.Manifest.WindowTitle = candidate.WindowTitle;
        return session.ScreenCapture.SwitchToWindow(candidate.WindowHandle, candidate.WindowTitle);
    }

    private static void WriteLocalCaptureState(LocalCaptureSession session, string status, string? reason = null)
    {
        var capturePayload = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["sessionId"] = session.Manifest.SessionId,
            ["title"] = session.Manifest.Title,
            ["platform"] = session.Manifest.Platform,
            ["meetingId"] = session.Manifest.MeetingId,
            ["sourceType"] = session.Manifest.SourceType,
            ["exeName"] = session.Manifest.ExeName,
            ["processId"] = session.Manifest.ProcessId,
            ["windowHandle"] = session.Manifest.WindowHandle,
            ["windowTitle"] = session.Manifest.WindowTitle,
            ["audioCaptureMode"] = session.AudioCaptureMode,
            ["mediaCaptureMode"] = session.Manifest.MediaCaptureMode,
            ["videoCaptureMode"] = session.Manifest.VideoCaptureMode,
            ["displayDeviceName"] = session.Manifest.DisplayDeviceName,
            ["displayLabel"] = session.Manifest.DisplayLabel,
            ["captureX"] = session.Manifest.CaptureX,
            ["captureY"] = session.Manifest.CaptureY,
            ["captureWidth"] = session.Manifest.CaptureWidth,
            ["captureHeight"] = session.Manifest.CaptureHeight,
            ["captureBackend"] = "in-process-wasapi-ffmpeg",
            ["startedAt"] = session.Manifest.StartedAt,
            ["recordingPath"] = session.Manifest.Paths.Recording,
            ["systemAudioPath"] = session.Manifest.Paths.SystemAudio,
            ["micPath"] = session.Manifest.Paths.Mic,
            ["liveCaptionsJsonPath"] = session.Manifest.Paths.LiveCaptionsJson,
            ["liveCaptionsTextPath"] = session.Manifest.Paths.LiveCaptionsText,
            ["warnings"] = session.Warnings,
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["isPaused"] = session.IsPaused
        };

        if (!string.IsNullOrWhiteSpace(reason))
        {
            capturePayload["reason"] = reason;
        }

        File.WriteAllText(session.Manifest.Paths.Capture, JsonSerializer.Serialize(capturePayload, PipeJson.Options));
    }

    private static void StopLocalCapture(WasapiAudioCapture? capture, List<string> warnings, string label)
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

    private static ScreenRecordingSession? CreateScreenCaptureSession(LocalCaptureSession session)
    {
        if (string.Equals(session.Manifest.MediaCaptureMode, "audio-only", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sessionDirectory = Path.GetDirectoryName(session.Manifest.Paths.Log) ?? AppContext.BaseDirectory;
        if (string.Equals(session.Manifest.VideoCaptureMode, "display", StringComparison.OrdinalIgnoreCase) &&
            TryGetDisplayBounds(session.Manifest, out var displayBounds))
        {
            return ScreenRecordingSession.ForDisplay(
                displayBounds,
                session.Manifest.DisplayLabel ?? session.Manifest.DisplayDeviceName ?? "selected display",
                ResolveSessionRoot(session.Manifest),
                session.Manifest.Paths.Recording,
                session.Manifest.Paths.SystemAudio,
                session.Manifest.Paths.Mic,
                session.Warnings
            );
        }

        if (string.Equals(session.Manifest.VideoCaptureMode, "display", StringComparison.OrdinalIgnoreCase))
        {
            AddWarning(session.Warnings, "The selected display details were unavailable, so capture fell back to the meeting window.");
        }

        if (TryParseWindowHandle(session.Manifest.WindowHandle, out var windowHandle))
        {
            return ScreenRecordingSession.ForWindow(
                windowHandle,
                ResolveSessionRoot(session.Manifest),
                session.Manifest.Paths.Recording,
                session.Manifest.Paths.SystemAudio,
                session.Manifest.Paths.Mic,
                session.Warnings
            );
        }

        return null;
    }

    private static TimeSpan GetCurrentRecordedDuration(LocalCaptureSession session)
    {
        var durations = new[]
        {
            TryReadWaveDuration(session.Manifest.Paths.SystemAudio),
            TryReadWaveDuration(session.Manifest.Paths.Mic)
        };

        return durations.Max();
    }

    private static TimeSpan TryReadWaveDuration(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return TimeSpan.Zero;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= 44)
            {
                return TimeSpan.Zero;
            }

            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            stream.Seek(28, SeekOrigin.Begin);
            var avgBytesPerSecond = reader.ReadInt32();
            if (avgBytesPerSecond <= 0)
            {
                return TimeSpan.Zero;
            }

            var dataLength = Math.Max(0, stream.Length - 44);
            return TimeSpan.FromSeconds(dataLength / (double)avgBytesPerSecond);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private static string ResolveSessionRoot(SessionManifest manifest)
        => Path.GetDirectoryName(Path.GetDirectoryName(manifest.Paths.Log) ?? string.Empty)
            ?? Path.GetDirectoryName(manifest.Paths.Log)
            ?? AppContext.BaseDirectory;

    private static bool TryGetDisplayBounds(SessionManifest manifest, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!manifest.CaptureX.HasValue ||
            !manifest.CaptureY.HasValue ||
            !manifest.CaptureWidth.HasValue ||
            !manifest.CaptureHeight.HasValue)
        {
            return false;
        }

        if (manifest.CaptureWidth.Value <= 2 || manifest.CaptureHeight.Value <= 2)
        {
            return false;
        }

        bounds = new Rectangle(
            manifest.CaptureX.Value,
            manifest.CaptureY.Value,
            manifest.CaptureWidth.Value,
            manifest.CaptureHeight.Value
        );
        return true;
    }

    private sealed class LocalCaptureSession
    {
        public required SessionManifest Manifest { get; init; }
        public required string AudioCaptureMode { get; init; }
        public WasapiAudioCapture? LoopbackCapture { get; set; }
        public WasapiAudioCapture? MicrophoneCapture { get; set; }
        public ScreenRecordingSession? ScreenCapture { get; set; }
        public bool IsPaused { get; set; }
        public List<string> Warnings { get; } = [];
    }

    private static bool TryParseWindowHandle(string? rawValue, out nint hwnd)
    {
        hwnd = nint.Zero;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalized = rawValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? rawValue[2..]
            : rawValue;

        if (!long.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        hwnd = new nint(value);
        return hwnd != nint.Zero;
    }
}
