using System.Diagnostics;
using System.Text.Json;
using MeetingRecorder.Shared.Configuration;

namespace MeetingRecorder.Windows.Interop;

public sealed class CompanionCliClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<string> AskMeetingAsync(string sessionDirectory, string question, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            [
                "-m",
                "companion.meeting_companion",
                "ask-meeting",
                "--session-dir",
                sessionDirectory,
                "--question",
                question
            ],
            cancellationToken
        );

        return string.IsNullOrWhiteSpace(result.StandardOutput)
            ? (string.IsNullOrWhiteSpace(result.StandardError) ? "No answer was returned." : result.StandardError.Trim())
            : result.StandardOutput.Trim();
    }

    public async Task<IReadOnlyList<SearchResultItem>> SearchHistoryAsync(string query, int limit = 25, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            [
                "-m",
                "companion.meeting_companion",
                "search-history",
                "--query",
                query,
                "--limit",
                limit.ToString()
            ],
            cancellationToken
        );

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return [];
        }

        try
        {
            var payload = JsonSerializer.Deserialize<SearchResponse>(result.StandardOutput, JsonOptions);
            return payload?.Results ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var workspaceRoot = RecorderSettingsProvider.WorkspaceRoot;
        var pythonExecutable = RecorderSettingsProvider.ResolvePythonExecutable();

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            WorkingDirectory = workspaceRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(await stdoutTask, await stderrTask);
    }

    public async Task<string> GetOpenCommitmentsAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            ["-m", "companion.meeting_companion", "open-commitments", "--limit", limit.ToString()],
            cancellationToken
        );
        return result.StandardOutput.Trim();
    }

    public async Task<string> GetMeetingPrepAsync(string title, int limit = 3, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            ["-m", "companion.meeting_companion", "meeting-prep", "--title", title, "--limit", limit.ToString()],
            cancellationToken
        );
        return string.IsNullOrWhiteSpace(result.StandardOutput)
            ? "No prep brief could be generated."
            : result.StandardOutput.Trim();
    }

    public async Task<string> GetTopicTimelineAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            ["-m", "companion.meeting_companion", "topic-timeline", "--query", query, "--limit", limit.ToString()],
            cancellationToken
        );
        return result.StandardOutput.Trim();
    }

    public async Task<string> SemanticSearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            ["-m", "companion.meeting_companion", "semantic-search", "--query", query, "--limit", limit.ToString()],
            cancellationToken
        );

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return string.Empty;
        }

        return result.StandardOutput.Trim();
    }

    public async Task<IReadOnlyList<SemanticSearchResultItem>> SemanticSearchResultsAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        var raw = await SemanticSearchAsync(query, limit, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            var payload = JsonSerializer.Deserialize<SemanticSearchResponse>(raw, JsonOptions);
            return payload?.Results ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<int> IndexSessionAsync(string sessionDirectory, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            ["-m", "companion.meeting_companion", "index-session", "--session-dir", sessionDirectory],
            cancellationToken
        );
        return 0;
    }

    private sealed record ProcessResult(string StandardOutput, string StandardError);
    private sealed record SearchResponse(IReadOnlyList<SearchResultItem>? Results);
    private sealed record SemanticSearchResponse(IReadOnlyList<SemanticSearchResultItem>? Results);
}

public sealed record SearchResultItem(
    string? SessionDir,
    string? Title,
    string? Platform,
    string? StartedAt,
    string? Snippet
);

public sealed record SemanticSearchExcerpt(
    string? Text,
    string? Speaker,
    double? StartSec,
    double? EndSec,
    double? Similarity
);

public sealed record SemanticSearchResultItem(
    string? SessionDir,
    string? Title,
    string? Platform,
    string? StartedAt,
    double? Relevance,
    IReadOnlyList<SemanticSearchExcerpt>? Excerpts
);
