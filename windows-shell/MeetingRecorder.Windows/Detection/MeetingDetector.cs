using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MeetingRecorder.Shared.Configuration;
using MeetingRecorder.Windows.Models;

namespace MeetingRecorder.Windows.Detection;

public sealed class MeetingDetector : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly int _currentProcessId = Environment.ProcessId;
    private readonly IReadOnlyList<MeetingSignature> _signatures;
    private MeetingCandidate? _lastWindowCandidate;

    public event EventHandler<MeetingCandidate?>? CandidateObserved;

    public MeetingDetector()
    {
        var settings = RecorderSettingsProvider.Current.Windows;
        _signatures = settings.MeetingSignatures
            .Select(signature => new MeetingSignature(
                signature.Platform,
                signature.SourceType,
                signature.ProcessNames.ToArray(),
                signature.TitleFragments.ToArray()))
            .ToList();

        _timer = new System.Windows.Forms.Timer
        {
            Interval = settings.Detection.PollIntervalMs
        };
        _timer.Tick += (_, _) =>
        {
            var autoCandidate = GetCandidate(includeFallback: false, excludeCurrentProcess: true);
            var windowCandidate = GetCandidate(includeFallback: true, excludeCurrentProcess: true);
            if (windowCandidate is not null)
            {
                _lastWindowCandidate = windowCandidate;
            }

            CandidateObserved?.Invoke(this, autoCandidate);
        };
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public MeetingCandidate? GetCurrentCandidate()
        => GetCandidate(includeFallback: false, excludeCurrentProcess: true);

    public MeetingCandidate? GetCurrentWindowCandidate()
    {
        var candidate = GetCandidate(includeFallback: true, excludeCurrentProcess: true);
        if (candidate is not null)
        {
            _lastWindowCandidate = candidate;
            return candidate;
        }

        return _lastWindowCandidate;
    }

    public IReadOnlyList<MeetingCandidate> GetAvailableWindowCandidates()
    {
        var candidates = new Dictionary<string, MeetingCandidate>(StringComparer.OrdinalIgnoreCase);

        EnumWindows((hwnd, _) =>
        {
            var candidate = BuildCandidateForWindow(hwnd, includeFallback: true, excludeCurrentProcess: true);
            if (candidate is not null)
            {
                candidates[candidate.SuppressionKey] = candidate;
            }

            return true;
        }, nint.Zero);

        return candidates.Values
            .OrderByDescending(IsLikelyMeetingPlatform)
            .ThenBy(candidate => candidate.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private MeetingCandidate? GetCandidate(bool includeFallback, bool excludeCurrentProcess)
    {
        var hwnd = GetForegroundWindow();
        return BuildCandidateForWindow(hwnd, includeFallback, excludeCurrentProcess);
    }

    private MeetingCandidate? BuildCandidateForWindow(nint hwnd, bool includeFallback, bool excludeCurrentProcess)
    {
        if (hwnd == nint.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            return null;
        }

        var title = GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return null;
        }

        if (excludeCurrentProcess && processId == _currentProcessId)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName.ToLowerInvariant();
            var normalizedTitle = title.ToLowerInvariant();
            foreach (var signature in _signatures)
            {
                var matchesProcess = signature.ProcessNames.Any(name => name.Equals(processName, StringComparison.OrdinalIgnoreCase));
                var matchesTitle = signature.TitleFragments.Any(fragment => normalizedTitle.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (matchesProcess && matchesTitle)
                {
                    return new MeetingCandidate(
                        signature.Platform,
                        signature.SourceType,
                        title,
                        title,
                        process.ProcessName,
                        process.Id,
                        hwnd
                    );
                }
            }

            if (includeFallback)
            {
                return BuildFallbackCandidate(title, process.ProcessName, process.Id, hwnd, normalizedTitle);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static MeetingCandidate BuildFallbackCandidate(string title, string processName, int processId, nint hwnd, string normalizedTitle)
    {
        var normalizedProcess = processName.ToLowerInvariant();
        var sourceType = IsBrowserProcess(normalizedProcess) ? "browser-window" : "native-window";
        var platform = GuessPlatform(normalizedProcess, normalizedTitle, sourceType);
        return new MeetingCandidate(
            platform,
            sourceType,
            title,
            title,
            processName,
            processId,
            hwnd
        );
    }

    private static bool IsBrowserProcess(string processName)
        => processName is "chrome" or "msedge";

    private static int IsLikelyMeetingPlatform(MeetingCandidate candidate)
        => candidate.Platform switch
        {
            "google-meet" or "teams" or "teams-web" or "zoom" or "zoom-web" or "webex" or "webex-web" => 2,
            "browser-window" or "manual-window" => 1,
            _ => 0
        };

    private static string GuessPlatform(string processName, string normalizedTitle, string sourceType)
    {
        if (normalizedTitle.Contains("google meet", StringComparison.OrdinalIgnoreCase) ||
            normalizedTitle.Contains("meet.google.com", StringComparison.OrdinalIgnoreCase) ||
            normalizedTitle.Contains("meet -", StringComparison.OrdinalIgnoreCase))
        {
            return "google-meet";
        }

        if (normalizedTitle.Contains("teams.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
            normalizedTitle.Contains("microsoft teams", StringComparison.OrdinalIgnoreCase))
        {
            return sourceType == "browser-window" ? "teams-web" : "teams";
        }

        if (normalizedTitle.Contains("webex", StringComparison.OrdinalIgnoreCase))
        {
            return sourceType == "browser-window" ? "webex-web" : "webex";
        }

        if (normalizedTitle.Contains("zoom", StringComparison.OrdinalIgnoreCase))
        {
            return sourceType == "browser-window" ? "zoom-web" : "zoom";
        }

        return processName switch
        {
            "zoom" => "zoom",
            "ms-teams" or "teams" => "teams",
            "webex" or "ciscocollabhost" => "webex",
            "chrome" or "msedge" => "browser-window",
            _ => "manual-window"
        };
    }

    public static bool IsWindowMinimized(nint hwnd) => hwnd != nint.Zero && IsIconic(hwnd);

    private static string GetWindowTitle(nint hwnd)
    {
        var builder = new StringBuilder(512);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, nint lParam);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);
}
