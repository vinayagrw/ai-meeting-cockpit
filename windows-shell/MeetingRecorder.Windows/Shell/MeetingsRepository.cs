using System.Text.Json;
using MeetingRecorder.Shared.Configuration;

namespace MeetingRecorder.Windows.Shell;

public sealed class MeetingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string MeetingsRoot { get; }

    public MeetingsRepository()
    {
        MeetingsRoot = ResolveMeetingsRoot();
    }

    public IReadOnlyList<SavedMeetingSession> GetRecentSessions(int limit = 50)
    {
        if (!Directory.Exists(MeetingsRoot))
        {
            return [];
        }

        return Directory.EnumerateDirectories(MeetingsRoot, "*", SearchOption.AllDirectories)
            .Select(TryLoadSession)
            .Where(session => session is not null)
            .Cast<SavedMeetingSession>()
            .OrderByDescending(session => session.StartedAt ?? session.LastUpdatedAt)
            .Take(limit)
            .ToList();
    }

    public IReadOnlyList<SavedMeetingSession> SearchSessions(string query, int limit = 50)
    {
        var needle = query.Trim();
        var candidates = GetRecentSessions(limit: 500);
        if (string.IsNullOrWhiteSpace(needle))
        {
            return candidates.Take(limit).ToList();
        }

        return candidates
            .Where(session => session.SearchBlob.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(session => session.StartedAt ?? session.LastUpdatedAt)
            .Take(limit)
            .ToList();
    }

    public SessionActionItems LoadActionItems(SavedMeetingSession session)
    {
        var path = Path.Combine(session.SessionDirectory, "action-items.json");
        if (!File.Exists(path))
        {
            return new SessionActionItems(path, []);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var items = new List<ActionItemRecord>();
            if (document.RootElement.TryGetProperty("items", out var itemsElement) &&
                itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsElement.EnumerateArray())
                {
                    items.Add(new ActionItemRecord(
                        GetString(item, "task") ?? string.Empty,
                        GetString(item, "owner"),
                        GetString(item, "dueDate"),
                        GetString(item, "source"),
                        GetString(item, "status") ?? "pending"
                    ));
                }
            }

            return new SessionActionItems(path, items);
        }
        catch
        {
            return new SessionActionItems(path, []);
        }
    }

    public void SaveActionItems(SessionActionItems actionItems)
    {
        var payload = new
        {
            items = actionItems.Items.Select(item => new
            {
                task = item.Task,
                owner = item.Owner,
                dueDate = item.DueDate,
                source = item.Source,
                status = item.Status
            })
        };
        File.WriteAllText(actionItems.Path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    public string LoadPreview(SavedMeetingSession session)
    {
        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(session.SummaryText))
        {
            sections.Add("Summary");
            sections.Add(session.SummaryText.Trim());
        }

        if (!string.IsNullOrWhiteSpace(session.TranscriptText))
        {
            sections.Add("Transcript");
            sections.Add(session.TranscriptText.Trim());
        }

        if (sections.Count == 0)
        {
            return "No summary or transcript has been generated yet for this meeting.";
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    public string LoadFollowUp(SavedMeetingSession session)
    {
        var path = Path.Combine(session.SessionDirectory, "follow-up.md");
        return File.Exists(path)
            ? File.ReadAllText(path)
            : "No follow-up draft has been generated yet.";
    }

    private SavedMeetingSession? TryLoadSession(string sessionDirectory)
    {
        var metadataPath = Path.Combine(sessionDirectory, "metadata.json");
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var root = document.RootElement;
            var summaryPath = Path.Combine(sessionDirectory, "summary.md");
            var transcriptPath = Path.Combine(sessionDirectory, "transcript.txt");
            var followUpPath = Path.Combine(sessionDirectory, "follow-up.md");
            var actionItemsPath = Path.Combine(sessionDirectory, "action-items.json");
            var summaryText = File.Exists(summaryPath) ? File.ReadAllText(summaryPath) : string.Empty;
            var transcriptText = File.Exists(transcriptPath) ? File.ReadAllText(transcriptPath) : string.Empty;
            var followUpText = File.Exists(followUpPath) ? File.ReadAllText(followUpPath) : string.Empty;
            var actionItemsText = File.Exists(actionItemsPath) ? File.ReadAllText(actionItemsPath) : string.Empty;
            var searchBlob = string.Join(
                "\n",
                GetString(root, "title") ?? string.Empty,
                GetString(root, "platform") ?? string.Empty,
                summaryText,
                transcriptText,
                followUpText,
                actionItemsText
            );

            return new SavedMeetingSession(
                sessionDirectory,
                GetString(root, "sessionId") ?? Path.GetFileName(sessionDirectory),
                GetString(root, "title") ?? Path.GetFileName(sessionDirectory),
                GetString(root, "platform") ?? "unknown",
                GetString(root, "status") ?? "unknown",
                ParseDate(GetString(root, "startedAt")),
                File.GetLastWriteTime(Path.Combine(sessionDirectory, "metadata.json")),
                summaryText,
                transcriptText,
                searchBlob,
                File.Exists(Path.Combine(sessionDirectory, "action-items.json")),
                File.Exists(Path.Combine(sessionDirectory, "follow-up.md"))
            );
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            ? value.GetString()
            : null;
    }

    private static string ResolveMeetingsRoot()
        => RecorderSettingsProvider.ResolveMeetingsRoot();
}

public sealed record SavedMeetingSession(
    string SessionDirectory,
    string SessionId,
    string Title,
    string Platform,
    string Status,
    DateTimeOffset? StartedAt,
    DateTime LastUpdatedAt,
    string SummaryText,
    string TranscriptText,
    string SearchBlob,
    bool HasActionItems,
    bool HasFollowUp
);

public sealed record ActionItemRecord(
    string Task,
    string? Owner,
    string? DueDate,
    string? Source,
    string Status
);

public sealed record SessionActionItems(string Path, IReadOnlyList<ActionItemRecord> Items);
