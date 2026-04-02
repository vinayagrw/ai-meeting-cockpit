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

    public SavedMeetingSession? GetSession(string sessionDirectory)
        => TryLoadSession(sessionDirectory);

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

    public SessionCommitments LoadCommitments(SavedMeetingSession session)
    {
        var insights = LoadInsightsDocument(session);
        if (insights is not null &&
            insights.Value.TryGetProperty("commitments", out var commitmentsElement) &&
            commitmentsElement.ValueKind == JsonValueKind.Object &&
            commitmentsElement.TryGetProperty("items", out var insightItems) &&
            insightItems.ValueKind == JsonValueKind.Array)
        {
            var items = new List<CommitmentRecord>();
            foreach (var item in insightItems.EnumerateArray())
            {
                items.Add(new CommitmentRecord(
                    GetString(item, "commitment") ?? string.Empty,
                    GetString(item, "owner"),
                    GetString(item, "dueDate"),
                    GetString(item, "meetingDate"),
                    GetString(item, "status") ?? "open"
                ));
            }

            return new SessionCommitments(Path.Combine(session.SessionDirectory, "insights.json"), items);
        }

        var path = Path.Combine(session.SessionDirectory, "commitments.json");
        if (!File.Exists(path))
        {
            return new SessionCommitments(path, []);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var items = new List<CommitmentRecord>();
            if (document.RootElement.TryGetProperty("items", out var itemsElement) &&
                itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsElement.EnumerateArray())
                {
                    items.Add(new CommitmentRecord(
                        GetString(item, "commitment") ?? string.Empty,
                        GetString(item, "owner"),
                        GetString(item, "dueDate"),
                        GetString(item, "meetingDate"),
                        GetString(item, "status") ?? "open"
                    ));
                }
            }

            return new SessionCommitments(path, items);
        }
        catch
        {
            return new SessionCommitments(path, []);
        }
    }

    public IReadOnlyList<RecurringSpeakerRecord> GetRecurringSpeakers(int minimumSessions = 2, int limit = 20)
    {
        var storeResults = LoadRecurringSpeakersFromStore(minimumSessions, limit);
        if (storeResults.Count > 0)
        {
            return storeResults;
        }

        var speakerIndex = new Dictionary<string, RecurringSpeakerAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in GetRecentSessions(limit: 500))
        {
            foreach (var speaker in LoadSpeakers(session))
            {
                if (string.IsNullOrWhiteSpace(speaker))
                {
                    continue;
                }

                if (!speakerIndex.TryGetValue(speaker, out var accumulator))
                {
                    accumulator = new RecurringSpeakerAccumulator(speaker);
                    speakerIndex[speaker] = accumulator;
                }

                accumulator.SessionDirectories.Add(session.SessionDirectory);
                accumulator.Platforms.Add(session.Platform);
                if (!string.IsNullOrWhiteSpace(session.Title) && accumulator.ExampleMeetings.Count < 3)
                {
                    accumulator.ExampleMeetings.Add(session.Title);
                }
            }
        }

        return speakerIndex.Values
            .Where(item => item.SessionDirectories.Count >= minimumSessions)
            .OrderByDescending(item => item.SessionDirectories.Count)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(item => new RecurringSpeakerRecord(
                item.Name,
                item.SessionDirectories.Count,
                item.Platforms.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                item.ExampleMeetings.ToArray()))
            .ToList();
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
        var insights = LoadInsightsSummary(session);
        if (!string.IsNullOrWhiteSpace(insights))
        {
            sections.Add("Insights");
            sections.Add(insights.Trim());
        }

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

    public string LoadInsightsSummary(SavedMeetingSession session)
    {
        var lines = new List<string>();
        JsonElement? insights = LoadInsightsDocument(session);
        var metadataPath = Path.Combine(session.SessionDirectory, "metadata.json");
        if (File.Exists(metadataPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
                var root = document.RootElement;
                var smartTitle = GetString(root, "smartTitle");
                if (string.IsNullOrWhiteSpace(smartTitle) && insights is not null)
                {
                    smartTitle = GetString(insights.Value, "smartTitle");
                }
                if (!string.IsNullOrWhiteSpace(smartTitle))
                {
                    lines.Add($"Smart title: {smartTitle}");
                }

                if (root.TryGetProperty("meetingType", out var meetingTypeElement) &&
                    meetingTypeElement.ValueKind == JsonValueKind.Object)
                {
                    var detectedType = GetString(meetingTypeElement, "type");
                    if (!string.IsNullOrWhiteSpace(detectedType))
                    {
                        lines.Add($"Meeting type: {detectedType}");
                    }
                }
                else if (insights is not null &&
                         insights.Value.TryGetProperty("meetingType", out var insightsMeetingType) &&
                         insightsMeetingType.ValueKind == JsonValueKind.Object)
                {
                    var detectedType = GetString(insightsMeetingType, "type");
                    if (!string.IsNullOrWhiteSpace(detectedType))
                    {
                        lines.Add($"Meeting type: {detectedType}");
                    }
                }
            }
            catch
            {
                // ignore malformed metadata previews
            }
        }

        if (insights is not null &&
            insights.Value.TryGetProperty("sentiment", out var sentimentRoot) &&
            sentimentRoot.ValueKind == JsonValueKind.Object &&
            sentimentRoot.TryGetProperty("segments", out var sentimentSegments) &&
            sentimentSegments.ValueKind == JsonValueKind.Array)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var segment in sentimentSegments.EnumerateArray())
            {
                var tone = GetString(segment, "tone") ?? GetString(segment, "sentiment");
                if (string.IsNullOrWhiteSpace(tone))
                {
                    continue;
                }

                counts[tone] = counts.TryGetValue(tone, out var current) ? current + 1 : 1;
            }

            if (counts.Count > 0)
            {
                var summary = string.Join(", ", counts.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key}: {pair.Value}"));
                lines.Add($"Sentiment: {summary}");
            }
        }

        if (insights is not null &&
            insights.Value.TryGetProperty("agenda", out var agendaRoot) &&
            agendaRoot.ValueKind == JsonValueKind.Object &&
            agendaRoot.TryGetProperty("items", out var agendaItems) &&
            agendaItems.ValueKind == JsonValueKind.Array)
        {
            var agendaLines = agendaItems.EnumerateArray()
                .Take(5)
                .Select(item =>
                {
                    var topic = GetString(item, "topic");
                    if (string.IsNullOrWhiteSpace(topic))
                    {
                        return null;
                    }

                    var owner = GetString(item, "owner");
                    return string.IsNullOrWhiteSpace(owner)
                        ? $"- {topic}"
                        : $"- {topic} ({owner})";
                })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToList();

            if (agendaLines.Count > 0)
            {
                lines.Add("Agenda:");
                lines.AddRange(agendaLines);
            }
        }

        if (insights is not null &&
            insights.Value.TryGetProperty("highlights", out var highlightsRoot) &&
            highlightsRoot.ValueKind == JsonValueKind.Object &&
            highlightsRoot.TryGetProperty("highlights", out var highlights) &&
            highlights.ValueKind == JsonValueKind.Array)
        {
            var momentLines = highlights.EnumerateArray()
                .Take(3)
                .Select(item =>
                {
                    var title = GetString(item, "title") ?? "Untitled moment";
                    var start = item.TryGetProperty("startSec", out var startElement) && startElement.TryGetDouble(out var seconds)
                        ? TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss")
                        : "00:00:00";
                    return $"- [{start}] {title}";
                })
                .ToList();

            if (momentLines.Count > 0)
            {
                lines.Add("Highlights:");
                lines.AddRange(momentLines);
            }
        }

        var highlightClips = LoadHighlightClips(session);
        if (highlightClips.Count > 0)
        {
            lines.Add("Highlight clips:");
            lines.AddRange(highlightClips.Take(4).Select(path => $"- {path}"));
        }

        var commitments = LoadCommitments(session);
        if (commitments.Items.Count > 0)
        {
            lines.Add("Commitments:");
            foreach (var item in commitments.Items.Take(4))
            {
                var owner = string.IsNullOrWhiteSpace(item.Owner) ? "Unassigned" : item.Owner;
                var due = string.IsNullOrWhiteSpace(item.DueDate) ? string.Empty : $" (due {item.DueDate})";
                lines.Add($"- {owner}: {item.Commitment}{due}");
            }
        }

        var speakers = LoadSpeakers(session);
        if (speakers.Count > 0)
        {
            lines.Add($"Speakers: {string.Join(", ", speakers.Take(8))}");
        }

        if (insights is not null &&
            insights.Value.TryGetProperty("speakerIdentity", out var speakerIdentityRoot) &&
            speakerIdentityRoot.ValueKind == JsonValueKind.Object &&
            speakerIdentityRoot.TryGetProperty("assignments", out var assignmentsElement) &&
            assignmentsElement.ValueKind == JsonValueKind.Array)
        {
            var identityLines = assignmentsElement.EnumerateArray()
                .Take(4)
                .Select(item =>
                {
                    var label = GetString(item, "speaker_label") ?? GetString(item, "speakerLabel");
                    var similarity = item.TryGetProperty("similarity", out var similarityElement) && similarityElement.TryGetDouble(out var similarityValue)
                        ? similarityValue
                        : (double?)null;
                    var segments = item.TryGetProperty("session_segment_count", out var segmentCountElement) && segmentCountElement.TryGetInt32(out var segmentCount)
                        ? segmentCount
                        : item.TryGetProperty("sessionSegmentCount", out var altSegmentCountElement) && altSegmentCountElement.TryGetInt32(out var altSegmentCount)
                            ? altSegmentCount
                            : 0;
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        return null;
                    }

                    return similarity.HasValue
                        ? $"- {label} ({segments} segments, similarity {similarity.Value:0.00})"
                        : $"- {label} ({segments} segments)";
                })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToList();

            if (identityLines.Count > 0)
            {
                lines.Add("Recurring speaker matches:");
                lines.AddRange(identityLines);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    public IReadOnlyList<string> LoadSpeakers(SavedMeetingSession session)
    {
        var speakerPath = ResolveExistingSessionFile(session.SessionDirectory, "speaker-diarization.json");
        if (!File.Exists(speakerPath))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(speakerPath));
            var participants = new List<string>();
            if (document.RootElement.TryGetProperty("participants", out var participantsElement) &&
                participantsElement.ValueKind == JsonValueKind.Array)
            {
                participants.AddRange(
                    participantsElement.EnumerateArray()
                        .Select(item => item.GetString())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Cast<string>());
            }

            if (participants.Count == 0 &&
                document.RootElement.TryGetProperty("segments", out var segmentsElement) &&
                segmentsElement.ValueKind == JsonValueKind.Array)
            {
                participants.AddRange(
                    segmentsElement.EnumerateArray()
                        .Select(item => GetString(item, "speaker"))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Cast<string>());
            }

            return participants
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<string> LoadHighlightClips(SavedMeetingSession session)
    {
        var clipsDirectory = Path.Combine(session.SessionDirectory, "_session", "highlight-clips");
        if (!Directory.Exists(clipsDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(clipsDirectory, "*.mp4", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private JsonElement? LoadInsightsDocument(SavedMeetingSession session)
    {
        var path = Path.Combine(session.SessionDirectory, "insights.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }
        catch
        {
            return null;
        }
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
            var insightsPath = Path.Combine(sessionDirectory, "insights.json");
            var commitmentsPath = Path.Combine(sessionDirectory, "commitments.json");
            var summaryText = File.Exists(summaryPath) ? File.ReadAllText(summaryPath) : string.Empty;
            var transcriptText = File.Exists(transcriptPath) ? File.ReadAllText(transcriptPath) : string.Empty;
            var followUpText = File.Exists(followUpPath) ? File.ReadAllText(followUpPath) : string.Empty;
            var actionItemsText = File.Exists(actionItemsPath) ? File.ReadAllText(actionItemsPath) : string.Empty;
            var insightsText = File.Exists(insightsPath) ? File.ReadAllText(insightsPath) : string.Empty;
            var commitmentsText = File.Exists(commitmentsPath) ? File.ReadAllText(commitmentsPath) : string.Empty;
            var displayTitle = GetString(root, "smartTitle") ?? GetString(root, "title") ?? Path.GetFileName(sessionDirectory);
            var searchBlob = string.Join(
                "\n",
                GetString(root, "smartTitle") ?? string.Empty,
                GetString(root, "title") ?? string.Empty,
                GetString(root, "platform") ?? string.Empty,
                summaryText,
                transcriptText,
                followUpText,
                actionItemsText,
                insightsText,
                commitmentsText
            );

            return new SavedMeetingSession(
                sessionDirectory,
                GetString(root, "sessionId") ?? Path.GetFileName(sessionDirectory),
                displayTitle,
                GetString(root, "platform") ?? "unknown",
                GetString(root, "status") ?? "unknown",
                ParseDate(GetString(root, "startedAt")),
                File.GetLastWriteTime(Path.Combine(sessionDirectory, "metadata.json")),
                summaryText,
                transcriptText,
                searchBlob,
                File.Exists(Path.Combine(sessionDirectory, "action-items.json")),
                File.Exists(Path.Combine(sessionDirectory, "follow-up.md")),
                File.Exists(commitmentsPath) || File.Exists(insightsPath)
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

    private static string ResolveExistingSessionFile(string sessionDirectory, string fileName)
    {
        var artifactPath = Path.Combine(sessionDirectory, "_session", fileName);
        if (File.Exists(artifactPath))
        {
            return artifactPath;
        }

        return Path.Combine(sessionDirectory, fileName);
    }

    private IReadOnlyList<RecurringSpeakerRecord> LoadRecurringSpeakersFromStore(int minimumSessions, int limit)
    {
        var storePath = Path.Combine(MeetingsRoot, "speaker-profiles.json");
        if (!File.Exists(storePath))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(storePath));
            if (!document.RootElement.TryGetProperty("profiles", out var profilesElement) ||
                profilesElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return profilesElement.EnumerateArray()
                .Select(profile =>
                {
                    var displayName = GetString(profile, "display_name") ?? GetString(profile, "displayName");
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        return null;
                    }

                    var sessionCount = profile.TryGetProperty("session_keys", out var sessionKeysElement) &&
                                       sessionKeysElement.ValueKind == JsonValueKind.Array
                        ? sessionKeysElement.EnumerateArray().Count()
                        : profile.TryGetProperty("sessionKeys", out var altSessionKeysElement) &&
                          altSessionKeysElement.ValueKind == JsonValueKind.Array
                            ? altSessionKeysElement.EnumerateArray().Count()
                            : 0;

                    if (sessionCount < minimumSessions)
                    {
                        return null;
                    }

                    var platforms = profile.TryGetProperty("platforms", out var platformsElement) &&
                                    platformsElement.ValueKind == JsonValueKind.Array
                        ? platformsElement.EnumerateArray()
                            .Select(item => item.GetString())
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Cast<string>()
                            .ToArray()
                        : [];

                    var examples = profile.TryGetProperty("example_sessions", out var examplesElement) &&
                                   examplesElement.ValueKind == JsonValueKind.Array
                        ? examplesElement.EnumerateArray()
                            .Select(item => item.GetString())
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Cast<string>()
                            .ToArray()
                        : profile.TryGetProperty("exampleSessions", out var altExamplesElement) &&
                          altExamplesElement.ValueKind == JsonValueKind.Array
                            ? altExamplesElement.EnumerateArray()
                                .Select(item => item.GetString())
                                .Where(value => !string.IsNullOrWhiteSpace(value))
                                .Cast<string>()
                                .ToArray()
                            : [];

                    return new RecurringSpeakerRecord(displayName, sessionCount, platforms, examples);
                })
                .Where(record => record is not null)
                .Cast<RecurringSpeakerRecord>()
                .OrderByDescending(record => record.SessionCount)
                .ThenBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string ResolveMeetingsRoot()
        => RecorderSettingsProvider.ResolveMeetingsRoot();
}

file sealed class RecurringSpeakerAccumulator
{
    public RecurringSpeakerAccumulator(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public HashSet<string> SessionDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Platforms { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ExampleMeetings { get; } = [];
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
    bool HasFollowUp,
    bool HasCommitments
);

public sealed record CommitmentRecord(
    string Commitment,
    string? Owner,
    string? DueDate,
    string? MeetingDate,
    string Status
);

public sealed record ActionItemRecord(
    string Task,
    string? Owner,
    string? DueDate,
    string? Source,
    string Status
);

public sealed record SessionActionItems(string Path, IReadOnlyList<ActionItemRecord> Items);
public sealed record SessionCommitments(string Path, IReadOnlyList<CommitmentRecord> Items);
public sealed record RecurringSpeakerRecord(
    string Name,
    int SessionCount,
    IReadOnlyList<string> Platforms,
    IReadOnlyList<string> ExampleMeetings
);
