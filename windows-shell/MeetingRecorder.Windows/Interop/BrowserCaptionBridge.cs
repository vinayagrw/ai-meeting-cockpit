using System.Net;
using System.Text;
using System.Text.Json;
using MeetingRecorder.Shared.Configuration;
using MeetingRecorder.Windows.Models;
using MeetingRecorder.Windows.Session;
using MeetingRecorder.Windows.Shell;

namespace MeetingRecorder.Windows.Interop;

public sealed class BrowserCaptionBridge : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _cancellation;
    private Task? _serverTask;
    private string? _activeSessionDirectory;
    private string? _activePlatform;
    private string? _activeWindowTitle;
    private string? _activeSourceType;
    private HashSet<string> _seenCaptionIds = new(StringComparer.Ordinal);

    private static string Prefix
    {
        get
        {
            var settings = RecorderSettingsProvider.Current.Windows.BrowserCaptionBridge;
            return $"http://{settings.Host}:{settings.Port}/";
        }
    }

    public string BaseUrl => Prefix.TrimEnd('/');

    public void Start()
    {
        if (_listener.IsListening)
        {
            return;
        }

        _listener.Prefixes.Clear();
        _listener.Prefixes.Add(Prefix);
        _listener.Start();
        _cancellation = new CancellationTokenSource();
        _serverTask = Task.Run(() => RunAsync(_cancellation.Token));
    }

    public void SetActiveSession(ActiveRecordingSession? session)
    {
        lock (_syncRoot)
        {
            if (session is null || !string.Equals(session.Manifest.SourceType, "browser-window", StringComparison.OrdinalIgnoreCase))
            {
                _activeSessionDirectory = null;
                _activePlatform = null;
                _activeWindowTitle = null;
                _activeSourceType = null;
                _seenCaptionIds = new HashSet<string>(StringComparer.Ordinal);
                return;
            }

            if (!string.Equals(_activeSessionDirectory, session.SessionDirectory, StringComparison.OrdinalIgnoreCase))
            {
                _seenCaptionIds = new HashSet<string>(StringComparer.Ordinal);
            }

            _activeSessionDirectory = session.SessionDirectory;
            _activePlatform = session.Manifest.Platform;
            _activeWindowTitle = session.Manifest.WindowTitle;
            _activeSourceType = session.Manifest.SourceType;
        }
    }

    public void Stop()
    {
        var cancellation = _cancellation;
        _cancellation = null;

        try
        {
            cancellation?.Cancel();
        }
        catch
        {
        }

        if (_listener.IsListening)
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
            }
        }

        if (_serverTask is not null)
        {
            try
            {
                _serverTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }
            finally
            {
                _serverTask = null;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                await HandleAsync(context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                if (!_listener.IsListening)
                {
                    break;
                }
            }
            catch (Exception error)
            {
                ShellDiagnostics.Log("Browser caption bridge request failed.", error);
                if (context is not null)
                {
                    await WriteJsonAsync(context.Response, 500, new { ok = false, error = error.Message });
                }
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            AddCorsHeaders(context.Response);
            context.Response.StatusCode = 204;
            context.Response.Close();
            return;
        }

        if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(path, "/health", StringComparison.Ordinal))
        {
            await WriteJsonAsync(context.Response, 200, new { ok = true, listening = _listener.IsListening });
            return;
        }

        if (string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(path, "/browser-captions", StringComparison.Ordinal))
        {
            using var document = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: cancellationToken);
            var applied = TryApplyUpdate(document.RootElement);
            await WriteJsonAsync(context.Response, applied ? 202 : 409, new { ok = applied });
            return;
        }

        await WriteJsonAsync(context.Response, 404, new { ok = false, error = "Not found" });
    }

    private bool TryApplyUpdate(JsonElement root)
    {
        string? sessionDirectory;
        string? platform;
        string? activeWindowTitle;
        string? sourceType;

        lock (_syncRoot)
        {
            sessionDirectory = _activeSessionDirectory;
            platform = _activePlatform;
            activeWindowTitle = _activeWindowTitle;
            sourceType = _activeSourceType;
        }

        if (string.IsNullOrWhiteSpace(sessionDirectory) ||
            !string.Equals(sourceType, "browser-window", StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(sessionDirectory))
        {
            return false;
        }

        var payloadPlatform = root.TryGetProperty("platform", out var platformElement)
            ? platformElement.GetString()
            : null;
        if (!string.IsNullOrWhiteSpace(platform) &&
            !string.IsNullOrWhiteSpace(payloadPlatform) &&
            !string.Equals(platform, payloadPlatform, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payloadWindowTitle = root.TryGetProperty("windowTitle", out var titleElement)
            ? titleElement.GetString()
            : null;
        if (!string.IsNullOrWhiteSpace(activeWindowTitle) &&
            !string.IsNullOrWhiteSpace(payloadWindowTitle) &&
            payloadWindowTitle.Length > 10 &&
            !string.Equals(activeWindowTitle, payloadWindowTitle, StringComparison.OrdinalIgnoreCase) &&
            !activeWindowTitle.Contains(payloadWindowTitle, StringComparison.OrdinalIgnoreCase) &&
            !payloadWindowTitle.Contains(activeWindowTitle, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var captions = ParseCaptions(root);
        var participants = ParseParticipants(root, captions);
        var snapshot = BuildSnapshotPayload(root, captions, participants);

        Directory.CreateDirectory(SessionPathLayout.InternalDirectory(sessionDirectory));
        var browserCaptionsJsonPath = SessionPathLayout.InternalArtifact(sessionDirectory, "browser-captions.json");
        var browserCaptionsTextPath = SessionPathLayout.InternalArtifact(sessionDirectory, "browser-captions.txt");
        var browserCaptionsJsonlPath = SessionPathLayout.InternalArtifact(sessionDirectory, "browser-captions.jsonl");
        var participantsPath = SessionPathLayout.InternalArtifact(sessionDirectory, "participants.json");

        File.WriteAllText(browserCaptionsJsonPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        File.WriteAllText(participantsPath, JsonSerializer.Serialize(new
        {
            participants,
            updatedAt = DateTimeOffset.UtcNow.ToString("O")
        }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        foreach (var caption in captions)
        {
            var signature = caption.Id;
            lock (_syncRoot)
            {
                if (!_seenCaptionIds.Add(signature))
                {
                    continue;
                }
            }

            var line = string.IsNullOrWhiteSpace(caption.Speaker)
                ? caption.Text
                : $"{caption.Speaker}: {caption.Text}";
            File.AppendAllText(browserCaptionsTextPath, $"[{caption.ObservedAt:O}] {line}{Environment.NewLine}", Encoding.UTF8);
            File.AppendAllText(browserCaptionsJsonlPath, JsonSerializer.Serialize(new
            {
                captionId = caption.Id,
                speaker = caption.Speaker,
                text = caption.Text,
                observedAt = caption.ObservedAt.ToString("O"),
                platform = payloadPlatform,
                source = "browser-extension"
            }) + Environment.NewLine, Encoding.UTF8);
        }

        return true;
    }

    private static List<BrowserCaption> ParseCaptions(JsonElement root)
    {
        var captions = new List<BrowserCaption>();
        if (!root.TryGetProperty("captions", out var captionsElement) || captionsElement.ValueKind != JsonValueKind.Array)
        {
            return captions;
        }

        foreach (var item in captionsElement.EnumerateArray())
        {
            var text = item.TryGetProperty("text", out var textElement)
                ? (textElement.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var speaker = item.TryGetProperty("speaker", out var speakerElement)
                ? (speakerElement.GetString() ?? string.Empty).Trim()
                : string.Empty;
            var observedAtRaw = item.TryGetProperty("observedAt", out var observedElement)
                ? observedElement.GetString()
                : null;
            var observedAt = DateTimeOffset.TryParse(observedAtRaw, out var parsedObservedAt)
                ? parsedObservedAt
                : DateTimeOffset.UtcNow;
            var id = item.TryGetProperty("captionId", out var idElement)
                ? (idElement.GetString() ?? string.Empty).Trim()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = $"{speaker}|{text}";
            }

            captions.Add(new BrowserCaption(id, speaker, text, observedAt));
        }

        return captions;
    }

    private static List<string> ParseParticipants(JsonElement root, IReadOnlyList<BrowserCaption> captions)
    {
        var participants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("participants", out var participantsElement) && participantsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in participantsElement.EnumerateArray())
            {
                var value = item.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    participants.Add(value);
                }
            }
        }

        foreach (var caption in captions)
        {
            if (!string.IsNullOrWhiteSpace(caption.Speaker))
            {
                participants.Add(caption.Speaker);
            }
        }

        return participants.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static object BuildSnapshotPayload(JsonElement root, IReadOnlyList<BrowserCaption> captions, IReadOnlyList<string> participants)
    {
        var lines = captions
            .Select(caption => string.IsNullOrWhiteSpace(caption.Speaker)
                ? caption.Text
                : $"{caption.Speaker}: {caption.Text}")
            .Distinct(StringComparer.Ordinal)
            .TakeLast(8)
            .ToArray();

        return new
        {
            status = captions.Count > 0 ? "listening" : "waiting",
            text = lines.Length == 0 ? "Waiting for browser captions..." : string.Join(Environment.NewLine, lines.TakeLast(4)),
            lines,
            captions = captions.TakeLast(8).Select(caption => new
            {
                captionId = caption.Id,
                speaker = caption.Speaker,
                text = caption.Text,
                observedAt = caption.ObservedAt.ToString("O")
            }),
            participants,
            platform = root.TryGetProperty("platform", out var platformElement) ? platformElement.GetString() : null,
            meetingId = root.TryGetProperty("meetingId", out var meetingIdElement) ? meetingIdElement.GetString() : null,
            sourceUrl = root.TryGetProperty("sourceUrl", out var sourceUrlElement) ? sourceUrlElement.GetString() : null,
            updatedAt = DateTimeOffset.UtcNow.ToString("O"),
            source = "browser-extension"
        };
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        AddCorsHeaders(response);
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static void AddCorsHeaders(HttpListenerResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
    }

    private sealed record BrowserCaption(string Id, string Speaker, string Text, DateTimeOffset ObservedAt);
}
