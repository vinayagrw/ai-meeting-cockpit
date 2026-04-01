using MeetingRecorder.Shared.Configuration;

namespace MeetingRecorder.Windows.Capture;

internal static class FfmpegLocator
{
    public static string? Resolve() => RecorderSettingsProvider.ResolveFfmpegPath();
}
