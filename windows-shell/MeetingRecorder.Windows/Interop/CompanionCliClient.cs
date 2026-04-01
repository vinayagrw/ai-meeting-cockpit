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

    private sealed record ProcessResult(string StandardOutput, string StandardError);
    private sealed record SearchResponse(IReadOnlyList<SearchResultItem>? Results);
}

public sealed record SearchResultItem(
    string? SessionDir,
    string? Title,
    string? Platform,
    string? StartedAt,
    string? Snippet
);
