using System.Text.Json;

namespace MeetingRecorder.Shared.Sessions;

public sealed class SessionManifest
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Platform { get; set; } = "unknown";
    public string MeetingId { get; set; } = "manual";
    public string Title { get; set; } = "Meeting";
    public string StartedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public string SourceType { get; set; } = "native-window";
    public string? ExeName { get; set; }
    public int? ProcessId { get; set; }
    public string? WindowHandle { get; set; }
    public string? WindowTitle { get; set; }
    public string? AudioCaptureMode { get; set; }
    public string? MediaCaptureMode { get; set; }
    public string? VideoCaptureMode { get; set; }
    public string? DisplayDeviceName { get; set; }
    public string? DisplayLabel { get; set; }
    public int? CaptureX { get; set; }
    public int? CaptureY { get; set; }
    public int? CaptureWidth { get; set; }
    public int? CaptureHeight { get; set; }
    public List<string> CaptureWarnings { get; set; } = [];
    public SessionArtifactPaths Paths { get; set; } = new();

    public static SessionManifest LoadFrom(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<SessionManifest>(json, Pipes.PipeJson.Options)
            ?? throw new InvalidOperationException($"Unable to deserialize session manifest: {manifestPath}");
    }

    public void SaveTo(string manifestPath)
    {
        var json = JsonSerializer.Serialize(this, Pipes.PipeJson.Options);
        File.WriteAllText(manifestPath, json);
    }
}

public sealed class SessionArtifactPaths
{
    public string Recording { get; set; } = string.Empty;
    public string? SystemAudio { get; set; }
    public string Mic { get; set; } = string.Empty;
    public string Capture { get; set; } = string.Empty;
    public string TranscriptJson { get; set; } = string.Empty;
    public string TranscriptText { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? LiveCaptionsJson { get; set; }
    public string? LiveCaptionsText { get; set; }
    public string Log { get; set; } = string.Empty;
}
