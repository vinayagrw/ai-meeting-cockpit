using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MeetingRecorder.Windows.Shell;

public static class AppIconProvider
{
    private static readonly Lazy<Icon> CachedIcon = new(CreateIcon);

    public static Icon Icon => CachedIcon.Value;

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var backgroundPath = CreateRoundedRectangle(new Rectangle(4, 4, 56, 56), 16);
        using var backgroundBrush = new SolidBrush(Color.FromArgb(29, 53, 87));
        graphics.FillPath(backgroundBrush, backgroundPath);

        using var accentBrush = new SolidBrush(Color.FromArgb(230, 57, 70));
        graphics.FillEllipse(accentBrush, 14, 18, 18, 18);

        using var barBrush = new SolidBrush(Color.FromArgb(241, 250, 238));
        graphics.FillRectangle(barBrush, 36, 16, 6, 28);
        graphics.FillRectangle(barBrush, 46, 22, 6, 22);

        using var linePen = new Pen(Color.FromArgb(168, 218, 220), 4)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(linePen, 16, 46, 50, 46);

        var handle = bitmap.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(handle);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
