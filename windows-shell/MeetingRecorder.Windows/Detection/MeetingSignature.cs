namespace MeetingRecorder.Windows.Detection;

public sealed record MeetingSignature(
    string Platform,
    string SourceType,
    string[] ProcessNames,
    string[] TitleFragments
);
