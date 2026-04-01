using System.Text.Json;
using MeetingRecorder.Shared.Configuration;

namespace MeetingRecorder.Shared.Pipes;

public static class PipeChannel
{
    public static string Name => RecorderSettingsProvider.Current.Windows.CaptureWorker.PipeName;
}

public static class PipeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

public sealed record BeginSessionCommand(
    string SessionId,
    string OutputDirectory,
    string Title,
    string Platform,
    string SourceType,
    int? ProcessId,
    string? ExeName,
    string? WindowHandle,
    string? WindowTitle,
    bool PreferProcessLoopback
);

public sealed record PauseSessionCommand(string SessionId, string Reason);

public sealed record ResumeSessionCommand(string SessionId);

public sealed record StopSessionCommand(string SessionId, string Reason);

public sealed record GetStatusCommand(string SessionId);

public sealed record WorkerCommandEnvelope(string CommandType, JsonElement Payload);

public sealed record WorkerEventEnvelope(
    string EventType,
    string SessionId,
    string? Message = null,
    int? ProgressPercent = null,
    string? AudioCaptureMode = null
);
