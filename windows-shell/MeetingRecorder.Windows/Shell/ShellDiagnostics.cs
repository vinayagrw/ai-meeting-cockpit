using MeetingRecorder.Shared.Configuration;

namespace MeetingRecorder.Windows.Shell;

public static class ShellDiagnostics
{
    private static readonly object Sync = new();
    private static readonly string LocalFallbackDirectory = Path.Combine(AppContext.BaseDirectory, "meetings-data", "logs");

    public static void Log(string message, Exception? error = null)
    {
        lock (Sync)
        {
            var logPath = GetDotNetLogPath();
            try
            {
                WriteEntry(logPath, message, error);
            }
            catch
            {
                try
                {
                    var fallback = GetFallbackLogPath();
                    WriteEntry(fallback, message, error);
                }
                catch
                {
                }
            }
        }
    }

    public static string GetDotNetLogPath() => Path.Combine(GetLogsDirectory(preferDocuments: true), "dotnet-recorder.log");

    public static string GetStartupTracePath() => Path.Combine(GetLogsDirectory(preferDocuments: true), "dotnet-startup-trace.log");

    public static string GetStartupErrorPath() => Path.Combine(GetLogsDirectory(preferDocuments: true), "dotnet-startup-error.log");

    private static void WriteEntry(string logPath, string message, Exception? error)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        using var writer = new StreamWriter(logPath, append: true);
        writer.WriteLine($"[{DateTimeOffset.Now:O}] {message}");
        if (error is not null)
        {
            writer.WriteLine(error.ToString());
        }
    }

    private static string GetFallbackLogPath() => Path.Combine(GetLogsDirectory(preferDocuments: false), "dotnet-recorder.log");

    private static string GetLogsDirectory(bool preferDocuments)
    {
        if (!preferDocuments)
        {
            Directory.CreateDirectory(LocalFallbackDirectory);
            return LocalFallbackDirectory;
        }

        try
        {
            var configuredLogsRoot = RecorderSettingsProvider.ResolveLogsRoot();
            if (!string.IsNullOrWhiteSpace(configuredLogsRoot))
            {
                Directory.CreateDirectory(configuredLogsRoot);
                return configuredLogsRoot;
            }
        }
        catch
        {
        }

        Directory.CreateDirectory(LocalFallbackDirectory);
        return LocalFallbackDirectory;
    }
}
