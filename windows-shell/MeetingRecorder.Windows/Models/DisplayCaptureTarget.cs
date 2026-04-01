namespace MeetingRecorder.Windows.Models;

public sealed record DisplayCaptureTarget(
    string DeviceName,
    string DisplayName,
    Rectangle Bounds,
    bool IsPrimary
)
{
    public string Description => $"{DisplayName}{(IsPrimary ? " (Primary)" : string.Empty)} - {Bounds.Width}x{Bounds.Height} @ {Bounds.X},{Bounds.Y}";

    public static DisplayCaptureTarget FromScreen(Screen screen)
        => new(
            screen.DeviceName,
            string.IsNullOrWhiteSpace(screen.DeviceName) ? "Display" : screen.DeviceName,
            screen.Bounds,
            screen.Primary
        );

    public override string ToString() => Description;
}

public enum VideoCaptureSource
{
    Display,
    Window
}

public sealed record VideoCaptureSelection(
    VideoCaptureSource Source,
    DisplayCaptureTarget? Display
);
