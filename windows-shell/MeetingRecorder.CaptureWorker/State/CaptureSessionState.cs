using MeetingRecorder.CaptureWorker.Audio;

namespace MeetingRecorder.CaptureWorker.State;

public sealed class CaptureSessionState
{
    public required string SessionId { get; init; }
    public required string OutputDirectory { get; init; }
    public required string RecordingPath { get; init; }
    public required string MicPath { get; init; }
    public required string Title { get; init; }
    public required string Platform { get; init; }
    public required string SourceType { get; init; }
    public string? ExeName { get; init; }
    public int? ProcessId { get; init; }
    public string? WindowHandle { get; init; }
    public string? WindowTitle { get; init; }
    public string AudioCaptureMode { get; set; } = "process_loopback";
    public string CaptureBackend { get; init; } = "offline-scaffold";
    public string StartedAt { get; init; } = DateTimeOffset.UtcNow.ToString("O");
    public bool IsRecording { get; set; }
    public bool IsPaused { get; set; }
    public List<string> Warnings { get; } = [];
    internal WasapiAudioCapture? LoopbackCapture { get; set; }
    internal WasapiAudioCapture? MicrophoneCapture { get; set; }
}
