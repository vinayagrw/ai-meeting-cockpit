using System.Text.Json;
using MeetingRecorder.Shared.Pipes;
using MeetingRecorder.Shared.Sessions;
using MeetingRecorder.Shared.Configuration;
using MeetingRecorder.Windows.Models;

namespace MeetingRecorder.Windows.Session;

public sealed class SessionManifestWriter
{
    public ActiveRecordingSession CreateSession(MeetingCandidate candidate, VideoCaptureSelection? captureSelection = null, string mediaCaptureMode = "screen-and-audio")
    {
        var meetingsRoot = ResolveMeetingsRoot();
        var startedAt = DateTimeOffset.UtcNow;
        var dateFolder = Path.Combine(meetingsRoot, startedAt.ToLocalTime().ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dateFolder);

        var folderName = $"{candidate.Platform}-{Slugify(candidate.Title)}-{startedAt.ToLocalTime():HHmmss}";
        var sessionDirectory = Path.Combine(dateFolder, folderName);
        var suffix = 1;
        while (Directory.Exists(sessionDirectory))
        {
            sessionDirectory = Path.Combine(dateFolder, $"{folderName}-{suffix++}");
        }

        Directory.CreateDirectory(sessionDirectory);
        Directory.CreateDirectory(SessionPathLayout.InternalDirectory(sessionDirectory));

        var manifest = new SessionManifest
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Platform = candidate.Platform,
            MeetingId = candidate.MeetingId,
            Title = candidate.Title,
            StartedAt = startedAt.ToString("O"),
            SourceType = candidate.SourceType,
            ExeName = candidate.ProcessName,
            ProcessId = candidate.ProcessId,
            WindowHandle = $"0x{candidate.WindowHandle.ToInt64():X}",
            WindowTitle = candidate.WindowTitle,
            AudioCaptureMode = "system_loopback",
            MediaCaptureMode = mediaCaptureMode,
            VideoCaptureMode = string.Equals(mediaCaptureMode, "audio-only", StringComparison.OrdinalIgnoreCase)
                ? "none"
                : captureSelection?.Source == VideoCaptureSource.Window ? "window" : "display",
            DisplayDeviceName = captureSelection?.Display?.DeviceName,
            DisplayLabel = captureSelection?.Display?.DisplayName,
            CaptureX = captureSelection?.Display?.Bounds.X,
            CaptureY = captureSelection?.Display?.Bounds.Y,
            CaptureWidth = captureSelection?.Display?.Bounds.Width,
            CaptureHeight = captureSelection?.Display?.Bounds.Height,
            Paths = new SessionArtifactPaths
            {
                Recording = Path.Combine(sessionDirectory, "recording.mp4"),
                SystemAudio = SessionPathLayout.InternalArtifact(sessionDirectory, "system-audio.wav"),
                Mic = SessionPathLayout.InternalArtifact(sessionDirectory, "mic.wav"),
                Capture = SessionPathLayout.InternalArtifact(sessionDirectory, "capture.json"),
                TranscriptJson = SessionPathLayout.InternalArtifact(sessionDirectory, "transcript.json"),
                TranscriptText = Path.Combine(sessionDirectory, "transcript.txt"),
                Summary = Path.Combine(sessionDirectory, "summary.md"),
                LiveCaptionsJson = SessionPathLayout.InternalArtifact(sessionDirectory, "live-captions.json"),
                LiveCaptionsText = SessionPathLayout.InternalArtifact(sessionDirectory, "live-captions.txt"),
                Log = SessionPathLayout.InternalArtifact(sessionDirectory, "app.log")
            }
        };

        WriteMetadata(manifest, sessionDirectory, status: "recording");
        File.WriteAllText(manifest.Paths.Capture, JsonSerializer.Serialize(new
        {
            status = "preparing",
            sessionId = manifest.SessionId,
            audioCaptureMode = manifest.AudioCaptureMode,
            mediaCaptureMode = manifest.MediaCaptureMode,
            videoCaptureMode = manifest.VideoCaptureMode,
            displayDeviceName = manifest.DisplayDeviceName,
            displayLabel = manifest.DisplayLabel,
            captureX = manifest.CaptureX,
            captureY = manifest.CaptureY,
            captureWidth = manifest.CaptureWidth,
            captureHeight = manifest.CaptureHeight,
            recordingPath = manifest.Paths.Recording,
            systemAudioPath = manifest.Paths.SystemAudio,
            micPath = manifest.Paths.Mic,
            liveCaptionsJsonPath = manifest.Paths.LiveCaptionsJson,
            liveCaptionsTextPath = manifest.Paths.LiveCaptionsText,
            warnings = manifest.CaptureWarnings
        }, PipeJson.Options));
        AppendLog(manifest.Paths.Log, $"Session created for {manifest.Title}");
        return new ActiveRecordingSession(sessionDirectory, manifest);
    }

    public void UpdateRecordingState(ActiveRecordingSession session, string? audioCaptureMode = null, IEnumerable<string>? warnings = null)
    {
        if (!string.IsNullOrWhiteSpace(audioCaptureMode))
        {
            session.Manifest.AudioCaptureMode = audioCaptureMode;
        }

        var newWarnings = MergeWarnings(session.Manifest, warnings);
        WriteMetadata(session.Manifest, session.SessionDirectory, "recording");
        WriteCaptureState(session.Manifest, "recording");
        foreach (var warning in newWarnings)
        {
            AppendLog(session.Manifest.Paths.Log, $"Warning: {warning}");
        }
    }

    public void MarkPaused(ActiveRecordingSession session, string reason, string? audioCaptureMode = null, IEnumerable<string>? warnings = null)
    {
        if (!string.IsNullOrWhiteSpace(audioCaptureMode))
        {
            session.Manifest.AudioCaptureMode = audioCaptureMode;
        }

        var newWarnings = MergeWarnings(session.Manifest, warnings);
        WriteMetadata(session.Manifest, session.SessionDirectory, "paused", reason);
        WriteCaptureState(session.Manifest, "paused", reason);
        foreach (var warning in newWarnings)
        {
            AppendLog(session.Manifest.Paths.Log, $"Warning: {warning}");
        }

        AppendLog(session.Manifest.Paths.Log, $"Session paused ({reason})");
    }

    public void MarkResumed(ActiveRecordingSession session, string? audioCaptureMode = null, IEnumerable<string>? warnings = null)
    {
        if (!string.IsNullOrWhiteSpace(audioCaptureMode))
        {
            session.Manifest.AudioCaptureMode = audioCaptureMode;
        }

        var newWarnings = MergeWarnings(session.Manifest, warnings);
        WriteMetadata(session.Manifest, session.SessionDirectory, "recording");
        WriteCaptureState(session.Manifest, "recording");
        foreach (var warning in newWarnings)
        {
            AppendLog(session.Manifest.Paths.Log, $"Warning: {warning}");
        }

        AppendLog(session.Manifest.Paths.Log, "Session resumed");
    }

    public void MarkStopped(ActiveRecordingSession session, string reason, string? audioCaptureMode = null, IEnumerable<string>? warnings = null)
    {
        if (!string.IsNullOrWhiteSpace(audioCaptureMode))
        {
            session.Manifest.AudioCaptureMode = audioCaptureMode;
        }

        var newWarnings = MergeWarnings(session.Manifest, warnings);

        WriteMetadata(session.Manifest, session.SessionDirectory, "processing", reason, DateTimeOffset.UtcNow.ToString("O"));
        WriteCaptureState(session.Manifest, "stopped", reason, DateTimeOffset.UtcNow.ToString("O"));
        foreach (var warning in newWarnings)
        {
            AppendLog(session.Manifest.Paths.Log, $"Warning: {warning}");
        }
        AppendLog(session.Manifest.Paths.Log, $"Session stopped ({reason})");
    }

    public void UpdateCaptureTarget(ActiveRecordingSession session, VideoCaptureSelection captureSelection, MeetingCandidate? candidate = null, string? note = null)
    {
        session.Manifest.MediaCaptureMode = "screen-and-audio";
        session.Manifest.VideoCaptureMode = captureSelection.Source == VideoCaptureSource.Window ? "window" : "display";

        session.Manifest.DisplayDeviceName = captureSelection.Display?.DeviceName;
        session.Manifest.DisplayLabel = captureSelection.Display?.DisplayName;
        session.Manifest.CaptureX = captureSelection.Display?.Bounds.X;
        session.Manifest.CaptureY = captureSelection.Display?.Bounds.Y;
        session.Manifest.CaptureWidth = captureSelection.Display?.Bounds.Width;
        session.Manifest.CaptureHeight = captureSelection.Display?.Bounds.Height;

        if (candidate is not null)
        {
            session.Manifest.SourceType = candidate.SourceType;
            session.Manifest.ExeName = candidate.ProcessName;
            session.Manifest.ProcessId = candidate.ProcessId;
            session.Manifest.WindowHandle = $"0x{candidate.WindowHandle.ToInt64():X}";
            session.Manifest.WindowTitle = candidate.WindowTitle;
        }

        WriteMetadata(session.Manifest, session.SessionDirectory, "recording");
        WriteCaptureState(session.Manifest, "recording");
        if (!string.IsNullOrWhiteSpace(note))
        {
            AppendLog(session.Manifest.Paths.Log, note);
        }
    }

    private static void WriteMetadata(SessionManifest manifest, string sessionDirectory, string status, string? stopReason = null, string? stoppedAt = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["sessionId"] = manifest.SessionId,
            ["platform"] = manifest.Platform,
            ["meetingId"] = manifest.MeetingId,
            ["title"] = manifest.Title,
            ["startedAt"] = manifest.StartedAt,
            ["sourceType"] = manifest.SourceType,
            ["exeName"] = manifest.ExeName,
            ["processId"] = manifest.ProcessId,
            ["windowHandle"] = manifest.WindowHandle,
            ["windowTitle"] = manifest.WindowTitle,
            ["audioCaptureMode"] = manifest.AudioCaptureMode,
            ["mediaCaptureMode"] = manifest.MediaCaptureMode,
            ["videoCaptureMode"] = manifest.VideoCaptureMode,
            ["displayDeviceName"] = manifest.DisplayDeviceName,
            ["displayLabel"] = manifest.DisplayLabel,
            ["captureX"] = manifest.CaptureX,
            ["captureY"] = manifest.CaptureY,
            ["captureWidth"] = manifest.CaptureWidth,
            ["captureHeight"] = manifest.CaptureHeight,
            ["captureWarnings"] = manifest.CaptureWarnings,
            ["status"] = status,
            ["paths"] = new
            {
                recording = manifest.Paths.Recording,
                systemAudio = manifest.Paths.SystemAudio,
                mic = manifest.Paths.Mic,
                capture = manifest.Paths.Capture,
                transcriptJson = manifest.Paths.TranscriptJson,
                transcriptText = manifest.Paths.TranscriptText,
                summary = manifest.Paths.Summary,
                liveCaptionsJson = manifest.Paths.LiveCaptionsJson,
                liveCaptionsText = manifest.Paths.LiveCaptionsText,
                log = manifest.Paths.Log
            }
        };

        if (!string.IsNullOrWhiteSpace(stopReason))
        {
            payload["stopReason"] = stopReason;
        }

        if (!string.IsNullOrWhiteSpace(stoppedAt))
        {
            payload["stoppedAt"] = stoppedAt;
        }

        var metadataPath = Path.Combine(sessionDirectory, "metadata.json");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(payload, PipeJson.Options));
    }

    private static void AppendLog(string logPath, string message)
    {
        File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
    }

    private static void WriteCaptureState(SessionManifest manifest, string status, string? reason = null, string? stoppedAt = null)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(manifest.Paths.Capture))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest.Paths.Capture));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                payload[property.Name] = JsonSerializer.Deserialize<object?>(property.Value.GetRawText(), PipeJson.Options);
            }
        }

        payload["status"] = status;
        payload["sessionId"] = manifest.SessionId;
        payload["title"] = manifest.Title;
        payload["meetingId"] = manifest.MeetingId;
        payload["platform"] = manifest.Platform;
        payload["sourceType"] = manifest.SourceType;
        payload["startedAt"] = manifest.StartedAt;
        payload["exeName"] = manifest.ExeName;
        payload["processId"] = manifest.ProcessId;
        payload["windowHandle"] = manifest.WindowHandle;
        payload["windowTitle"] = manifest.WindowTitle;
        payload["audioCaptureMode"] = manifest.AudioCaptureMode;
        payload["mediaCaptureMode"] = manifest.MediaCaptureMode;
        payload["videoCaptureMode"] = manifest.VideoCaptureMode;
        payload["displayDeviceName"] = manifest.DisplayDeviceName;
        payload["displayLabel"] = manifest.DisplayLabel;
        payload["captureX"] = manifest.CaptureX;
        payload["captureY"] = manifest.CaptureY;
        payload["captureWidth"] = manifest.CaptureWidth;
        payload["captureHeight"] = manifest.CaptureHeight;
        payload["recordingPath"] = manifest.Paths.Recording;
        payload["systemAudioPath"] = manifest.Paths.SystemAudio;
        payload["micPath"] = manifest.Paths.Mic;
        payload["liveCaptionsJsonPath"] = manifest.Paths.LiveCaptionsJson;
        payload["liveCaptionsTextPath"] = manifest.Paths.LiveCaptionsText;
        payload["warnings"] = manifest.CaptureWarnings;
        payload["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");

        if (!string.IsNullOrWhiteSpace(reason))
        {
            payload["reason"] = reason;
        }
        else
        {
            payload.Remove("reason");
        }

        if (!string.IsNullOrWhiteSpace(stoppedAt))
        {
            payload["stoppedAt"] = stoppedAt;
        }
        else
        {
            payload.Remove("stoppedAt");
        }

        File.WriteAllText(manifest.Paths.Capture, JsonSerializer.Serialize(payload, PipeJson.Options));
    }

    private static List<string> MergeWarnings(SessionManifest manifest, IEnumerable<string>? warnings)
    {
        if (warnings is null)
        {
            return [];
        }

        var added = new List<string>();
        foreach (var warning in warnings)
        {
            if (string.IsNullOrWhiteSpace(warning))
            {
                continue;
            }

            if (manifest.CaptureWarnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            manifest.CaptureWarnings.Add(warning);
            added.Add(warning);
        }

        return added;
    }

    private static string ResolveMeetingsRoot()
        => RecorderSettingsProvider.ResolveMeetingsRoot();

    private static string Slugify(string value)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length == 0 || builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
