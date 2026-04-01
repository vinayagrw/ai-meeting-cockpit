using MeetingRecorder.Shared.Sessions;

namespace MeetingRecorder.Windows.Models;

public sealed record ActiveRecordingSession(string SessionDirectory, SessionManifest Manifest);
