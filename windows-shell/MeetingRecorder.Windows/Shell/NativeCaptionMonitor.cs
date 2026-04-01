using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MeetingRecorder.Shared.Configuration;
using MeetingRecorder.Windows.Models;
using MeetingRecorder.Windows.Session;

namespace MeetingRecorder.Windows.Shell;

public sealed class NativeCaptionMonitor : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new()
    {
        Interval = RecorderSettingsProvider.Current.Windows.LiveCaptions.NativeCaptionPollIntervalMs
    };
    private readonly HashSet<string> _seenIds = new(StringComparer.Ordinal);
    private ActiveRecordingSession? _session;

    public NativeCaptionMonitor()
    {
        _timer.Tick += (_, _) => Poll();
    }

    public void Start(ActiveRecordingSession session)
    {
        if (!SupportsPlatform(session.Manifest.Platform))
        {
            Stop();
            return;
        }

        if (!string.Equals(_session?.SessionDirectory, session.SessionDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _seenIds.Clear();
        }

        _session = session;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _session = null;
        _seenIds.Clear();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }

    private void Poll()
    {
        var session = _session;
        if (session is null || !TryParseWindowHandle(session.Manifest.WindowHandle, out var hwnd))
        {
            return;
        }

        try
        {
            var texts = CollectWindowTexts(hwnd);
            var captions = ExtractCaptions(texts);
            WriteArtifacts(session.SessionDirectory, session.Manifest.Platform, captions);
        }
        catch (Exception error)
        {
            ShellDiagnostics.Log("Native caption monitor poll failed.", error);
        }
    }

    private IReadOnlyList<NativeCaptionEntry> ExtractCaptions(IReadOnlyList<string> texts)
    {
        var results = new List<NativeCaptionEntry>();
        var seenThisPass = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawText in texts)
        {
            foreach (var candidate in ParseCaptionCandidates(rawText))
            {
                if (seenThisPass.Add(candidate.Id))
                {
                    results.Add(candidate);
                }
            }
        }

        return results.TakeLast(8).ToList();
    }

    private IEnumerable<NativeCaptionEntry> ParseCaptionCandidates(string rawText)
    {
        var normalized = Normalize(rawText);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 280)
        {
            yield break;
        }

        if (TryParseSpeakerLine(normalized, out var direct))
        {
            yield return direct;
            yield break;
        }

        if (TryParseBracketedSpeakerLine(normalized, out var bracketed))
        {
            yield return bracketed;
            yield break;
        }

        var lines = normalized
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(4)
            .ToArray();
        if (lines.Length >= 2)
        {
            var speaker = lines[0];
            var text = string.Join(" ", lines.Skip(1)).Trim();
            if (LooksLikeSpeaker(speaker) && LooksLikeCaptionText(text))
            {
                yield return CreateEntry(speaker, text);
                yield break;
            }
        }

        foreach (var line in lines)
        {
            if (TryParseSpeakerLine(line, out var delimited) || TryParseBracketedSpeakerLine(line, out delimited))
            {
                yield return delimited;
            }
        }
    }

    private void WriteArtifacts(string sessionDirectory, string platform, IReadOnlyList<NativeCaptionEntry> captions)
    {
        Directory.CreateDirectory(SessionPathLayout.InternalDirectory(sessionDirectory));
        var jsonPath = SessionPathLayout.InternalArtifact(sessionDirectory, "native-captions.json");
        var txtPath = SessionPathLayout.InternalArtifact(sessionDirectory, "native-captions.txt");
        var jsonlPath = SessionPathLayout.InternalArtifact(sessionDirectory, "native-captions.jsonl");
        var participantsPath = SessionPathLayout.InternalArtifact(sessionDirectory, "participants.json");

        var participants = captions
            .Select(caption => caption.Speaker)
            .Where(speaker => !string.IsNullOrWhiteSpace(speaker))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var snapshot = new
        {
            status = captions.Count == 0 ? "waiting" : "listening",
            text = captions.Count == 0
                ? "Waiting for native captions..."
                : string.Join(Environment.NewLine, captions.TakeLast(4).Select(FormatLine)),
            lines = captions.TakeLast(8).Select(FormatLine).ToArray(),
            captions = captions.Select(caption => new
            {
                captionId = caption.Id,
                speaker = caption.Speaker,
                text = caption.Text,
                observedAt = caption.ObservedAt.ToString("O")
            }).ToArray(),
            participants,
            platform,
            updatedAt = DateTimeOffset.UtcNow.ToString("O"),
            source = "native-caption-monitor"
        };

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        File.WriteAllText(participantsPath, JsonSerializer.Serialize(new
        {
            participants,
            updatedAt = DateTimeOffset.UtcNow.ToString("O")
        }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        foreach (var caption in captions)
        {
            if (!_seenIds.Add(caption.Id))
            {
                continue;
            }

            File.AppendAllText(txtPath, $"[{caption.ObservedAt:O}] {FormatLine(caption)}{Environment.NewLine}", Encoding.UTF8);
            File.AppendAllText(jsonlPath, JsonSerializer.Serialize(new
            {
                captionId = caption.Id,
                speaker = caption.Speaker,
                text = caption.Text,
                observedAt = caption.ObservedAt.ToString("O"),
                platform,
                source = "native-caption-monitor"
            }) + Environment.NewLine, Encoding.UTF8);
        }
    }

    private static IReadOnlyList<string> CollectWindowTexts(nint parentHwnd)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AddWindowText(parentHwnd, results, seen);
        EnumChildWindows(parentHwnd, (childHwnd, _) =>
        {
            AddWindowText(childHwnd, results, seen);
            return true;
        }, nint.Zero);

        return results;
    }

    private static void AddWindowText(nint hwnd, List<string> results, HashSet<string> seen)
    {
        var text = GetWindowTextValue(hwnd);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var normalized = Normalize(text);
        if (normalized.Length <= 1 || normalized.Length > 280)
        {
            return;
        }

        if (seen.Add(normalized))
        {
            results.Add(normalized);
        }
    }

    private static string GetWindowTextValue(nint hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        var builder = new StringBuilder(Math.Max(length + 1, 256));
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static NativeCaptionEntry CreateEntry(string speaker, string text)
    {
        var normalizedSpeaker = Normalize(speaker);
        var normalizedText = Normalize(text);
        return new NativeCaptionEntry(
            $"{normalizedSpeaker}|{normalizedText}",
            normalizedSpeaker,
            normalizedText,
            DateTimeOffset.UtcNow
        );
    }

    private static string FormatLine(NativeCaptionEntry entry)
        => string.IsNullOrWhiteSpace(entry.Speaker)
            ? entry.Text
            : $"{entry.Speaker}: {entry.Text}";

    private static bool TryParseSpeakerLine(string rawText, out NativeCaptionEntry entry)
    {
        entry = default!;
        foreach (var delimiter in new[] { ": ", " - ", " — ", " – " })
        {
            var delimiterIndex = rawText.IndexOf(delimiter, StringComparison.Ordinal);
            if (delimiterIndex <= 0)
            {
                continue;
            }

            var speaker = rawText[..delimiterIndex];
            var text = rawText[(delimiterIndex + delimiter.Length)..];
            if (!LooksLikeSpeaker(speaker) || !LooksLikeCaptionText(text))
            {
                continue;
            }

            entry = CreateEntry(speaker, text);
            return true;
        }

        return false;
    }

    private static bool TryParseBracketedSpeakerLine(string rawText, out NativeCaptionEntry entry)
    {
        entry = default!;
        if (!rawText.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        var closingIndex = rawText.IndexOf(']');
        if (closingIndex <= 1 || closingIndex >= rawText.Length - 2)
        {
            return false;
        }

        var speaker = rawText[1..closingIndex];
        var text = rawText[(closingIndex + 1)..].TrimStart(' ', ':', '-', '—', '–');
        if (!LooksLikeSpeaker(speaker) || !LooksLikeCaptionText(text))
        {
            return false;
        }

        entry = CreateEntry(speaker, text);
        return true;
    }

    private static bool LooksLikeSpeaker(string value)
    {
        var candidate = Normalize(value);
        if (candidate.Length is < 2 or > 60)
        {
            return false;
        }

        if (candidate.Any(char.IsDigit))
        {
            return false;
        }

        return candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5;
    }

    private static bool LooksLikeCaptionText(string value)
    {
        var candidate = Normalize(value);
        if (candidate.Length is < 2 or > 220)
        {
            return false;
        }

        return !candidate.Contains("Turn on captions", StringComparison.OrdinalIgnoreCase) &&
               !candidate.Contains("subtitles unavailable", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
        => string.Join(" ", value.Replace('\u00A0', ' ').ReplaceLineEndings(" ").Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static bool SupportsPlatform(string platform)
        => platform is "zoom" or "teams" or "webex";

    private static bool TryParseWindowHandle(string? rawValue, out nint hwnd)
    {
        hwnd = nint.Zero;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalized = rawValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? rawValue[2..]
            : rawValue;

        if (!long.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        hwnd = new nint(value);
        return hwnd != nint.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint hWndParent, EnumWindowsProc lpEnumFunc, nint lParam);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    private sealed record NativeCaptionEntry(string Id, string Speaker, string Text, DateTimeOffset ObservedAt);
}
