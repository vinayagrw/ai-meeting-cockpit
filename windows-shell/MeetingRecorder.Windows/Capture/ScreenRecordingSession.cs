using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MeetingRecorder.Shared.Configuration;

namespace MeetingRecorder.Windows.Capture;

internal sealed class ScreenRecordingSession : IDisposable
{
    private Rectangle? _captureBounds;
    private string _captureTargetDescription;
    private nint _windowHandle;
    private readonly string _sessionDirectory;
    private readonly string _recordingPath;
    private readonly string? _systemAudioPath;
    private readonly string _micPath;
    private readonly List<string> _warnings;
    private readonly string? _ffmpegPath;
    private readonly string _segmentsDirectory;
    private readonly RecorderWindowsCaptureSettings _captureSettings;
    private readonly List<VideoSegment> _segments = [];
    private TimeSpan _initialVideoOffset = TimeSpan.Zero;
    private Process? _currentProcess;
    private int _segmentIndex;

    private ScreenRecordingSession(
        nint windowHandle,
        Rectangle? captureBounds,
        string captureTargetDescription,
        string sessionDirectory,
        string recordingPath,
        string? systemAudioPath,
        string micPath,
        List<string> warnings)
    {
        _windowHandle = windowHandle;
        _captureBounds = captureBounds;
        _captureTargetDescription = captureTargetDescription;
        _sessionDirectory = sessionDirectory;
        _recordingPath = recordingPath;
        _systemAudioPath = systemAudioPath;
        _micPath = micPath;
        _warnings = warnings;
        _ffmpegPath = FfmpegLocator.Resolve();
        _segmentsDirectory = Path.Combine(sessionDirectory, "video-segments");
        _captureSettings = RecorderSettingsProvider.Current.Windows.Capture;
    }

    public static ScreenRecordingSession ForWindow(
        nint windowHandle,
        string sessionDirectory,
        string recordingPath,
        string? systemAudioPath,
        string micPath,
        List<string> warnings,
        TimeSpan? initialVideoOffset = null)
        => new(
            windowHandle,
            null,
            "meeting window",
            sessionDirectory,
            recordingPath,
            systemAudioPath,
            micPath,
            warnings
        )
        {
            _initialVideoOffset = initialVideoOffset ?? TimeSpan.Zero
        };

    public static ScreenRecordingSession ForDisplay(
        Rectangle displayBounds,
        string displayLabel,
        string sessionDirectory,
        string recordingPath,
        string? systemAudioPath,
        string micPath,
        List<string> warnings,
        TimeSpan? initialVideoOffset = null)
        => new(
            nint.Zero,
            displayBounds,
            string.IsNullOrWhiteSpace(displayLabel) ? "selected display" : displayLabel,
            sessionDirectory,
            recordingPath,
            systemAudioPath,
            micPath,
            warnings
        )
        {
            _initialVideoOffset = initialVideoOffset ?? TimeSpan.Zero
        };

    public bool StartSegment()
    {
        if (string.IsNullOrWhiteSpace(_ffmpegPath))
        {
            AddWarning("ffmpeg was not found, so screen video capture is unavailable.");
            return false;
        }

        Rectangle bounds;
        if (_captureBounds is Rectangle requestedBounds)
        {
            if (!TryNormalizeCaptureBounds(requestedBounds, out bounds))
            {
                AddWarning($"Unable to determine valid bounds for {_captureTargetDescription} screen capture.");
                return false;
            }
        }
        else
        {
            if (_windowHandle == nint.Zero)
            {
                AddWarning("The selected meeting window does not have a valid window handle for screen capture.");
                return false;
            }

            if (!TryGetWindowBounds(_windowHandle, out bounds))
            {
                AddWarning("Unable to determine the meeting window bounds for screen capture.");
                return false;
            }
        }

        var width = EnsureEven(bounds.Width);
        var height = EnsureEven(bounds.Height);
        bounds = new Rectangle(bounds.X, bounds.Y, width, height);

        Directory.CreateDirectory(_segmentsDirectory);
        _segmentIndex += 1;
        var segmentPath = Path.Combine(_segmentsDirectory, $"segment-{_segmentIndex:0000}.mp4");

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = BuildCaptureArguments(bounds, segmentPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, data) =>
        {
            if (!string.IsNullOrWhiteSpace(data.Data) &&
                data.Data.Contains("Error", StringComparison.OrdinalIgnoreCase))
            {
                AddWarning($"ffmpeg screen capture reported: {data.Data.Trim()}");
            }
        };

        if (!process.Start())
        {
            AddWarning("ffmpeg could not be started for screen capture.");
            return false;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _currentProcess = process;
        _segments.Add(new VideoSegment(segmentPath, width, height));
        return true;
    }

    public void StopSegment()
    {
        if (_currentProcess is null)
        {
            return;
        }

        try
        {
            if (!_currentProcess.HasExited)
            {
                _currentProcess.StandardInput.WriteLine("q");
                _currentProcess.StandardInput.Flush();
                if (!_currentProcess.WaitForExit(5000))
                {
                    _currentProcess.Kill(entireProcessTree: true);
                    _currentProcess.WaitForExit(2000);
                }
            }
        }
        catch (Exception error)
        {
            AddWarning($"Screen capture stop was not clean: {error.Message}");
        }
        finally
        {
            _currentProcess.Dispose();
            _currentProcess = null;
        }
    }

    public bool SwitchToDisplay(Rectangle displayBounds, string displayLabel)
    {
        StopSegment();
        _windowHandle = nint.Zero;
        _captureBounds = displayBounds;
        _captureTargetDescription = string.IsNullOrWhiteSpace(displayLabel) ? "selected display" : displayLabel;
        return StartSegment();
    }

    public bool SwitchToWindow(nint windowHandle, string windowTitle)
    {
        if (windowHandle == nint.Zero)
        {
            AddWarning("The selected window does not have a valid handle for capture switching.");
            return false;
        }

        StopSegment();
        _windowHandle = windowHandle;
        _captureBounds = null;
        _captureTargetDescription = string.IsNullOrWhiteSpace(windowTitle) ? "meeting window" : windowTitle;
        return StartSegment();
    }

    public void SetInitialVideoOffset(TimeSpan offset)
    {
        if (_segments.Count > 0)
        {
            return;
        }

        _initialVideoOffset = offset < TimeSpan.Zero ? TimeSpan.Zero : offset;
    }

    public bool FinalizeRecording()
    {
        StopSegment();

        var existingSegments = _segments.Where(segment => File.Exists(segment.Path)).ToList();
        if (existingSegments.Count == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_ffmpegPath))
        {
            return false;
        }

        var canvasWidth = EnsureEven(existingSegments.Max(segment => segment.Width));
        var canvasHeight = EnsureEven(existingSegments.Max(segment => segment.Height));
        var normalizedSegments = NormalizeSegments(existingSegments, canvasWidth, canvasHeight);
        if (normalizedSegments.Count == 0)
        {
            return false;
        }

        var concatSegments = new List<string>();
        var introSegment = CreateIntroSegment(canvasWidth, canvasHeight);
        if (!string.IsNullOrWhiteSpace(introSegment))
        {
            concatSegments.Add(introSegment);
        }

        concatSegments.AddRange(normalizedSegments);
        var mergedVideoPath = concatSegments.Count == 1
            ? concatSegments[0]
            : MergeSegments(concatSegments);

        if (string.IsNullOrWhiteSpace(mergedVideoPath) || !File.Exists(mergedVideoPath))
        {
            return false;
        }

        var mixedAudioPath = BuildMixedAudio();
        if (!string.IsNullOrWhiteSpace(mixedAudioPath) && File.Exists(mixedAudioPath))
        {
            if (RunFfmpeg(
                    $"-y -i \"{mergedVideoPath}\" -i \"{mixedAudioPath}\" -c:v copy -c:a aac -b:a {_captureSettings.AudioBitrateKbps}k -ar {_captureSettings.AudioSampleRate} -ac {_captureSettings.AudioChannels} -movflags +faststart -shortest \"{_recordingPath}\"",
                    "mux the screen video with meeting audio"))
            {
                return true;
            }
        }

        try
        {
            File.Copy(mergedVideoPath, _recordingPath, overwrite: true);
            return true;
        }
        catch (Exception error)
        {
            AddWarning($"Unable to finalize recording.mp4: {error.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        StopSegment();
    }

    public static bool FinalizeAudioOnlyRecording(
        string sessionDirectory,
        string recordingPath,
        string? systemAudioPath,
        string micPath,
        List<string> warnings)
    {
        var ffmpegPath = FfmpegLocator.Resolve();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            AddWarning(warnings, "ffmpeg was not found, so the final audio recording could not be generated.");
            return false;
        }

        var mixedAudioPath = BuildMixedAudio(sessionDirectory, systemAudioPath, micPath, warnings, ffmpegPath);
        if (string.IsNullOrWhiteSpace(mixedAudioPath) || !File.Exists(mixedAudioPath))
        {
            AddWarning(warnings, "No usable audio tracks were available to finalize the recording.");
            return false;
        }

        return RunFfmpeg(
            ffmpegPath,
            $"-y -i \"{mixedAudioPath}\" -c:a aac -b:a {RecorderSettingsProvider.Current.Windows.Capture.AudioBitrateKbps}k -ar {RecorderSettingsProvider.Current.Windows.Capture.AudioSampleRate} -ac {RecorderSettingsProvider.Current.Windows.Capture.AudioChannels} -movflags +faststart \"{recordingPath}\"",
            "finalize the audio-only recording",
            warnings
        );
    }

    private string? MergeSegments(IReadOnlyList<string> existingSegments)
    {
        var listPath = Path.Combine(_segmentsDirectory, "segments.txt");
        var builder = new StringBuilder();
        foreach (var segment in existingSegments)
        {
            builder.Append("file '");
            builder.Append(segment.Replace("'", "''", StringComparison.Ordinal));
            builder.AppendLine("'");
        }

        File.WriteAllText(listPath, builder.ToString(), Encoding.UTF8);
        var mergedPath = Path.Combine(_segmentsDirectory, "merged-video.mp4");
        return RunFfmpeg(
            $"-y -f concat -safe 0 -i \"{listPath}\" -c:v {_captureSettings.ScreenVideoCodec} -preset {_captureSettings.ScreenVideoPreset} -pix_fmt {_captureSettings.ScreenPixelFormat} -an \"{mergedPath}\"",
            "merge paused screen-video segments")
            ? mergedPath
            : null;
    }

    private List<string> NormalizeSegments(IReadOnlyList<VideoSegment> existingSegments, int canvasWidth, int canvasHeight)
    {
        var normalizedDirectory = Path.Combine(_segmentsDirectory, "normalized");
        Directory.CreateDirectory(normalizedDirectory);
        var normalizedSegments = new List<string>();

        for (var index = 0; index < existingSegments.Count; index++)
        {
            var segment = existingSegments[index];
            var normalizedPath = Path.Combine(normalizedDirectory, $"normalized-{index + 1:0000}.mp4");
            var normalized = RunFfmpeg(
                $"-y -i \"{segment.Path}\" -vf \"scale={canvasWidth}:{canvasHeight}:force_original_aspect_ratio=decrease,pad={canvasWidth}:{canvasHeight}:(ow-iw)/2:(oh-ih)/2:black,fps={_captureSettings.ScreenFrameRate}\" -an -c:v {_captureSettings.ScreenVideoCodec} -preset {_captureSettings.ScreenVideoPreset} -pix_fmt {_captureSettings.ScreenPixelFormat} \"{normalizedPath}\"",
                $"normalize video segment {index + 1}");
            if (normalized && File.Exists(normalizedPath))
            {
                normalizedSegments.Add(normalizedPath);
            }
        }

        return normalizedSegments;
    }

    private string? CreateIntroSegment(int canvasWidth, int canvasHeight)
    {
        if (_initialVideoOffset <= TimeSpan.FromMilliseconds(250))
        {
            return null;
        }

        var introPath = Path.Combine(_segmentsDirectory, "intro-filler.mp4");
        var durationSeconds = _initialVideoOffset.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return RunFfmpeg(
            $"-y -f lavfi -i color=c=black:s={canvasWidth}x{canvasHeight}:r={_captureSettings.ScreenFrameRate}:d={durationSeconds} -an -c:v {_captureSettings.ScreenVideoCodec} -preset {_captureSettings.ScreenVideoPreset} -pix_fmt {_captureSettings.ScreenPixelFormat} \"{introPath}\"",
            "create the pre-roll filler segment")
            ? introPath
            : null;
    }

    private string? BuildMixedAudio()
        => BuildMixedAudio(_sessionDirectory, _systemAudioPath, _micPath, _warnings, _ffmpegPath);

    private static string? BuildMixedAudio(string sessionDirectory, string? systemAudioPath, string micPath, List<string> warnings, string? ffmpegPath)
    {
        var hasSystemAudio = !string.IsNullOrWhiteSpace(systemAudioPath) && File.Exists(systemAudioPath) && new FileInfo(systemAudioPath).Length > 44;
        var hasMic = File.Exists(micPath) && new FileInfo(micPath).Length > 44;

        if (!hasSystemAudio && !hasMic)
        {
            return null;
        }

        if (hasSystemAudio && !hasMic)
        {
            return systemAudioPath;
        }

        if (!hasSystemAudio && hasMic)
        {
            return micPath;
        }

        var mixedPath = Path.Combine(sessionDirectory, "_session", "mixed-audio.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(mixedPath) ?? sessionDirectory);
        return RunFfmpeg(
            ffmpegPath,
            $"-y -i \"{systemAudioPath}\" -i \"{micPath}\" -filter_complex \"[0:a]volume=1.10[a0];[1:a]volume=1.35,highpass=f=120,lowpass=f=7800,acompressor=threshold=-20dB:ratio=3:attack=15:release=200[a1];[a0][a1]amix=inputs=2:weights='1 1.25':normalize=0,aresample={RecorderSettingsProvider.Current.Windows.Capture.AudioSampleRate},aformat=sample_fmts=fltp:channel_layouts=stereo,dynaudnorm=f=180:g=12:p=0.9,alimiter=limit=0.95[a]\" -map \"[a]\" -ar {RecorderSettingsProvider.Current.Windows.Capture.AudioSampleRate} -ac {RecorderSettingsProvider.Current.Windows.Capture.AudioChannels} \"{mixedPath}\"",
            "mix system audio and microphone",
            warnings)
            ? mixedPath
            : systemAudioPath;
    }

    private bool RunFfmpeg(string arguments, string action)
        => RunFfmpeg(_ffmpegPath, arguments, action, _warnings);

    private static bool RunFfmpeg(string? ffmpegPath, string arguments, string action, List<string> warnings)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });

            if (process is null)
            {
                AddWarning(warnings, $"ffmpeg could not be started to {action}.");
                return false;
            }

            var stderr = process.StandardError.ReadToEnd();
            _ = process.StandardOutput.ReadToEnd();
            process.WaitForExit(RecorderSettingsProvider.Current.Windows.Capture.FfmpegTimeoutSeconds * 1000);
            if (process.ExitCode == 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                AddWarning(warnings, $"ffmpeg could not {action}: {stderr.Trim()}");
            }
        }
        catch (Exception error)
        {
            AddWarning(warnings, $"ffmpeg could not {action}: {error.Message}");
        }

        return false;
    }

    private string BuildCaptureArguments(Rectangle bounds, string outputPath)
    {
        var width = Math.Max(2, bounds.Width - (bounds.Width % 2));
        var height = Math.Max(2, bounds.Height - (bounds.Height % 2));
        return string.Join(
            " ",
            [
                "-y",
                "-f", "gdigrab",
                "-framerate", _captureSettings.ScreenFrameRate.ToString(),
                "-offset_x", bounds.X.ToString(),
                "-offset_y", bounds.Y.ToString(),
                "-video_size", $"{width}x{height}",
                "-draw_mouse", _captureSettings.DrawMouse ? "1" : "0",
                "-i", "desktop",
                "-an",
                "-c:v", _captureSettings.ScreenVideoCodec,
                "-preset", _captureSettings.ScreenVideoPreset,
                "-pix_fmt", _captureSettings.ScreenPixelFormat,
                $"\"{outputPath}\""
            ]);
    }

    private void AddWarning(string warning)
        => AddWarning(_warnings, warning);

    private static int EnsureEven(int value)
        => Math.Max(2, value - (value % 2));

    private static void AddWarning(List<string> warnings, string warning)
    {
        if (warnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        warnings.Add(warning);
    }

    private static bool TryGetWindowBounds(nint hwnd, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var windowBounds = new Rectangle(rect.Left, rect.Top, width, height);
        return TryNormalizeCaptureBounds(windowBounds, out bounds);
    }

    private static bool TryNormalizeCaptureBounds(Rectangle requestedBounds, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        var desktopBounds = SystemInformation.VirtualScreen;
        var clampedBounds = Rectangle.Intersect(requestedBounds, desktopBounds);
        if (clampedBounds.Width <= 2 || clampedBounds.Height <= 2)
        {
            return false;
        }

        bounds = clampedBounds;
        return true;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed record VideoSegment(string Path, int Width, int Height);
}
