namespace MeetingRecorder.Windows.Models;

public sealed record MeetingCandidate(
    string Platform,
    string SourceType,
    string Title,
    string WindowTitle,
    string ProcessName,
    int ProcessId,
    nint WindowHandle
)
{
    public string SuppressionKey => $"{Platform}:{ProcessId}:0x{WindowHandle.ToInt64():X}";
    public string MeetingId => $"{ProcessName}-{ProcessId}";
    public string DisplayLabel => $"{Title} [{ProcessName} | {Platform}]";
}
