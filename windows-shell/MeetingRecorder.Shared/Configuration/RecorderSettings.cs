using System.Text.Json;

namespace MeetingRecorder.Shared.Configuration;

public sealed class RecorderSettings
{
    public RecorderAppSettings App { get; set; } = new();
    public RecorderWindowsSettings Windows { get; set; } = new();
    public RecorderCompanionSettings Companion { get; set; } = new();
}

public sealed class RecorderAppSettings
{
    public string? MeetingsRoot { get; set; }
    public string? LogsRoot { get; set; }
    public string? PythonExecutable { get; set; }
    public string? FfmpegPath { get; set; }
}

public sealed class RecorderWindowsSettings
{
    public bool EnableBrowserCaptionBridge { get; set; }
    public bool UseCaptureWorker { get; set; }
    public string DefaultRecordingMode { get; set; } = "screen-and-audio";
    public string DefaultCaptureSource { get; set; } = "display";
    public RecorderWindowsDetectionSettings Detection { get; set; } = new();
    public RecorderWindowsDashboardSettings Dashboard { get; set; } = new();
    public RecorderWindowsLiveCaptionsSettings LiveCaptions { get; set; } = new();
    public RecorderWindowsNotificationSettings Notifications { get; set; } = new();
    public RecorderWindowsBridgeSettings BrowserCaptionBridge { get; set; } = new();
    public RecorderWindowsCaptureWorkerSettings CaptureWorker { get; set; } = new();
    public RecorderWindowsCaptureSettings Capture { get; set; } = new();
    public List<RecorderMeetingSignatureSettings> MeetingSignatures { get; set; } = [];
}

public sealed class RecorderWindowsDetectionSettings
{
    public int PollIntervalMs { get; set; } = 2000;
    public int PromptDelaySeconds { get; set; } = 5;
    public int PromptSuppressionMinutes { get; set; } = 30;
    public bool AutoPromptEnabled { get; set; } = true;
    public bool RequireDetectedMeetingForStart { get; set; } = true;
}

public sealed class RecorderWindowsDashboardSettings
{
    public int WindowWidth { get; set; } = 1440;
    public int WindowHeight { get; set; } = 900;
    public int MinWindowWidth { get; set; } = 1040;
    public int MinWindowHeight { get; set; } = 680;
    public int WideBreakpointWidth { get; set; } = 1400;
    public int StackedBreakpointWidth { get; set; } = 1100;
    public int DefaultStatusPanelWidth { get; set; } = 380;
    public int CompactStatusPanelWidth { get; set; } = 320;
    public int StackedStatusPanelHeight { get; set; } = 240;
    public int DefaultTopSectionHeight { get; set; } = 290;
    public int AppBarHeight { get; set; } = 58;
    public int SummaryStripHeight { get; set; } = 118;
    public int NavRailWidth { get; set; } = 156;
    public int NavRailButtonHeight { get; set; } = 42;
    public int WorkspaceSidebarWidth { get; set; } = 320;
    public int WorkspaceSidebarMinWidth { get; set; } = 260;
    public int WorkspaceSidebarStackedHeight { get; set; } = 280;
    public int OverflowButtonWidth { get; set; } = 86;
    public int DiagnosticsToggleButtonWidth { get; set; } = 132;
    public int TopBarStatusMaxWidth { get; set; } = 480;
    public int MinimumControlPanelWidth { get; set; } = 420;
    public int StatusDetailsHeight { get; set; } = 110;
    public int ControlButtonHeight { get; set; } = 36;
    public int PrimaryActionButtonWidth { get; set; } = 92;
    public int PickerButtonWidth { get; set; } = 106;
    public int MeetingsFolderButtonWidth { get; set; } = 118;
    public int ResetLayoutButtonWidth { get; set; } = 100;
    public int HideButtonWidth { get; set; } = 92;
    public int CommandButtonGap { get; set; } = 8;
    public int WorkspaceTabWidth { get; set; } = 160;
    public int WorkspaceListMinWidth { get; set; } = 260;
    public int WorkspaceDetailMinWidth { get; set; } = 340;
    public int WorkspaceBottomPaneMinHeight { get; set; } = 180;
    public double HistoryMainSplitRatio { get; set; } = 0.38;
    public double HistoryDetailSplitRatio { get; set; } = 0.56;
    public double ActionMainSplitRatio { get; set; } = 0.32;
    public double ActionDetailSplitRatio { get; set; } = 0.55;
    public bool DiagnosticsCollapsedByDefault { get; set; } = true;
    public bool EnableHorizontalTextScroll { get; set; } = true;
    public bool StatusWordWrap { get; set; } = true;
    public bool CaptionsWordWrap { get; set; }
    public bool ShowTopBarSubtitle { get; set; } = true;
    public bool ShowWorkspaceSummary { get; set; } = true;
    public bool ShowFooterHint { get; set; } = true;
    public bool ShowEmbeddedWorkspaceHeaders { get; set; }
}

public sealed class RecorderWindowsLiveCaptionsSettings
{
    public int UiRefreshIntervalMs { get; set; } = 250;
    public int NativeCaptionPollIntervalMs { get; set; } = 800;
}

public sealed class RecorderWindowsNotificationSettings
{
    public int StartupBalloonMs { get; set; } = 2500;
    public int SessionBalloonMs { get; set; } = 2000;
    public int WarningBalloonMs { get; set; } = 2500;
}

public sealed class RecorderWindowsBridgeSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 17824;
}

public sealed class RecorderWindowsCaptureWorkerSettings
{
    public string PipeName { get; set; } = "meeting-recorder-capture";
    public int ConnectTimeoutMs { get; set; } = 1500;
    public int StartupPollIntervalMs { get; set; } = 250;
    public int StartupPollAttempts { get; set; } = 12;
    public int CommandRetryDelayMs { get; set; } = 300;
    public int CommandRetryAttempts { get; set; } = 12;
}

public sealed class RecorderWindowsCaptureSettings
{
    public int ScreenFrameRate { get; set; } = 6;
    public string ScreenVideoCodec { get; set; } = "libx264";
    public string ScreenVideoPreset { get; set; } = "ultrafast";
    public string ScreenPixelFormat { get; set; } = "yuv420p";
    public bool DrawMouse { get; set; } = true;
    public int FfmpegTimeoutSeconds { get; set; } = 30;
    public int AudioBitrateKbps { get; set; } = 192;
    public int AudioSampleRate { get; set; } = 48000;
    public int AudioChannels { get; set; } = 2;
}

public sealed class RecorderMeetingSignatureSettings
{
    public string Platform { get; set; } = "manual-window";
    public string SourceType { get; set; } = "native-window";
    public List<string> ProcessNames { get; set; } = [];
    public List<string> TitleFragments { get; set; } = [];
}

public sealed class RecorderCompanionSettings
{
    public RecorderCompanionServerSettings Server { get; set; } = new();
    public RecorderCompanionSummarySettings Summary { get; set; } = new();
    public RecorderCompanionTranscriptionSettings Transcription { get; set; } = new();
    public RecorderCompanionLiveCaptionsSettings LiveCaptions { get; set; } = new();
    public RecorderCompanionHistorySettings History { get; set; } = new();
    public RecorderCompanionAutomationSettings Automation { get; set; } = new();
    public RecorderCompanionProcessingSettings Processing { get; set; } = new();
}

public sealed class RecorderCompanionServerSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 17823;
    public string StartupName { get; set; } = "MeetingRecorderCompanion";
}

public sealed class RecorderCompanionSummarySettings
{
    public string Provider { get; set; } = "openai";
    public string OllamaUrl { get; set; } = "http://127.0.0.1:11434";
    public string OllamaModel { get; set; } = "qwen2.5:7b-instruct";
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string OpenAiModel { get; set; } = "gpt-5-mini";
    public int OpenAiMaxOutputTokens { get; set; } = 900;
    public int OllamaTimeoutSeconds { get; set; } = 60;
    public int OpenAiTimeoutSeconds { get; set; } = 90;
}

public sealed class RecorderCompanionTranscriptionSettings
{
    public string GpuModel { get; set; } = "small";
    public string CpuModel { get; set; } = "base";
    public string LiveGpuModel { get; set; } = "small";
    public string LiveCpuModel { get; set; } = "base";
    public int NormalizedSampleRate { get; set; } = 16000;
    public int NormalizedChannels { get; set; } = 1;
    public string LiveLanguageHint { get; set; } = "en";
    public string FullTask { get; set; } = "translate";
    public string LiveTask { get; set; } = "translate";
    public int VadBeamSize { get; set; } = 5;
    public int FallbackBeamSize { get; set; } = 1;
    public int LiveBeamSize { get; set; } = 1;
    public int LiveBestOf { get; set; } = 1;
}

public sealed class RecorderCompanionLiveCaptionsSettings
{
    public double DefaultPollSeconds { get; set; } = 0.8;
    public int DefaultWindowSeconds { get; set; } = 4;
    public int DisplayLineCount { get; set; } = 4;
    public int HistoryLineCount { get; set; } = 8;
    public int RecentSegmentCount { get; set; } = 3;
    public int EmptyPassesBeforeFallback { get; set; } = 3;
    public int ClipSampleRate { get; set; } = 16000;
    public int ClipChannels { get; set; } = 1;
}

public sealed class RecorderCompanionHistorySettings
{
    public int DefaultSearchLimit { get; set; } = 10;
    public int MaxTranscriptChars { get; set; } = 12000;
    public int MaxSummaryChars { get; set; } = 5000;
    public int MaxRelevantExcerpts { get; set; } = 6;
    public int OpenAiTimeoutSeconds { get; set; } = 90;
    public int OpenAiMaxOutputTokens { get; set; } = 700;
}

public sealed class RecorderCompanionAutomationSettings
{
    public int OpenAiTimeoutSeconds { get; set; } = 90;
    public int OpenAiMaxOutputTokens { get; set; } = 700;
    public int MaxActionItems { get; set; } = 10;
}

public sealed class RecorderCompanionProcessingSettings
{
    public int ShutdownJoinTimeoutSeconds { get; set; } = 5;
}

public static class RecorderSettingsProvider
{
    private static readonly Lazy<(string WorkspaceRoot, string? ConfigPath)> Locations = new(ResolveLocations);
    private static readonly Lazy<RecorderSettings> Settings = new(LoadSettings);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static RecorderSettings Current => Settings.Value;
    public static string WorkspaceRoot => Locations.Value.WorkspaceRoot;
    public static string? ConfigPath => Locations.Value.ConfigPath;

    public static string ResolveMeetingsRoot()
    {
        var configured = ResolvePath(Current.App.MeetingsRoot);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            TryCreate(configured);
            if (Directory.Exists(configured))
            {
                return configured;
            }
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            var documentsPath = Path.Combine(documents, "Meetings");
            TryCreate(documentsPath);
            if (Directory.Exists(documentsPath))
            {
                return documentsPath;
            }
        }

        var fallback = Path.Combine(AppContext.BaseDirectory, "meetings-data");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    public static string ResolveLogsRoot()
    {
        var configured = ResolvePath(Current.App.LogsRoot);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            TryCreate(configured);
            if (Directory.Exists(configured))
            {
                return configured;
            }
        }

        var logsRoot = Path.Combine(ResolveMeetingsRoot(), "logs");
        TryCreate(logsRoot);
        return logsRoot;
    }

    public static string ResolvePythonExecutable()
    {
        var configured = ResolvePath(Current.App.PythonExecutable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var candidates = new[]
        {
            Path.Combine(WorkspaceRoot, ".venv", "Scripts", "python.exe"),
            Path.Combine(WorkspaceRoot, "venv", "Scripts", "python.exe")
        };

        return candidates.FirstOrDefault(File.Exists) ?? "python";
    }

    public static string? ResolveFfmpegPath()
    {
        var configured = ResolvePath(Current.App.FfmpegPath);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var bundled = Path.Combine(WorkspaceRoot, "companion", "bin", "ffmpeg", "ffmpeg.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(segment, "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(WorkspaceRoot, path));
    }

    private static RecorderSettings LoadSettings()
    {
        RecorderSettings settings;
        var configPath = ConfigPath;
        if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            settings = JsonSerializer.Deserialize<RecorderSettings>(json, JsonOptions) ?? new RecorderSettings();
        }
        else
        {
            settings = new RecorderSettings();
        }

        ApplyEnvironmentOverrides(settings);
        if (settings.Windows.MeetingSignatures.Count == 0)
        {
            settings.Windows.MeetingSignatures = BuildDefaultMeetingSignatures();
        }

        return settings;
    }

    private static void ApplyEnvironmentOverrides(RecorderSettings settings)
    {
        ApplyStringOverride("MEETING_COMPANION_MEETINGS_ROOT", value => settings.App.MeetingsRoot = value);
        ApplyStringOverride("MEETING_COMPANION_LOGS_ROOT", value => settings.App.LogsRoot = value);
        ApplyStringOverride("MEETING_RECORDER_PYTHON", value => settings.App.PythonExecutable = value);
        ApplyStringOverride("MEETING_COMPANION_FFMPEG", value => settings.App.FfmpegPath = value);

        ApplyBoolOverride("MEETING_RECORDER_ENABLE_BROWSER_BRIDGE", value => settings.Windows.EnableBrowserCaptionBridge = value);
        ApplyBoolOverride("MEETING_RECORDER_USE_WORKER", value => settings.Windows.UseCaptureWorker = value);
    }

    private static void ApplyStringOverride(string environmentVariable, Action<string> apply)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            apply(value);
        }
    }

    private static void ApplyBoolOverride(string environmentVariable, Action<bool> apply)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            apply(true);
            return;
        }

        if (value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            apply(false);
        }
    }

    private static (string WorkspaceRoot, string? ConfigPath) ResolveLocations()
    {
        var configuredPath = Environment.GetEnvironmentVariable("MEETING_RECORDER_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = Path.GetFullPath(configuredPath);
            return (Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory(), fullPath);
        }

        foreach (var start in CandidateRoots())
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                var configPath = Path.Combine(current.FullName, "meeting-recorder.config.json");
                if (File.Exists(configPath))
                {
                    return (current.FullName, configPath);
                }

                current = current.Parent;
            }
        }

        foreach (var start in CandidateRoots())
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "windows-shell")) &&
                    Directory.Exists(Path.Combine(current.FullName, "companion")))
                {
                    return (current.FullName, null);
                }

                current = current.Parent;
            }
        }

        return (Directory.GetCurrentDirectory(), null);
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
    }

    private static void TryCreate(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
        }
    }

    private static List<RecorderMeetingSignatureSettings> BuildDefaultMeetingSignatures()
        =>
        [
            new()
            {
                Platform = "zoom",
                SourceType = "native-window",
                ProcessNames = ["zoom"],
                TitleFragments = ["zoom meeting", "zoom workplace", " zoom "]
            },
            new()
            {
                Platform = "teams",
                SourceType = "native-window",
                ProcessNames = ["ms-teams", "teams"],
                TitleFragments = ["microsoft teams", "teams meeting", "meeting | microsoft teams"]
            },
            new()
            {
                Platform = "webex",
                SourceType = "native-window",
                ProcessNames = ["webex", "ciscocollabhost"],
                TitleFragments = ["webex", "meeting"]
            },
            new()
            {
                Platform = "google-meet",
                SourceType = "browser-window",
                ProcessNames = ["chrome", "msedge"],
                TitleFragments = ["google meet", "meet.google.com", "meet -", "- meet"]
            },
            new()
            {
                Platform = "teams-web",
                SourceType = "browser-window",
                ProcessNames = ["chrome", "msedge"],
                TitleFragments = ["microsoft teams", "teams.microsoft.com", "teams on the web"]
            },
            new()
            {
                Platform = "zoom-web",
                SourceType = "browser-window",
                ProcessNames = ["chrome", "msedge"],
                TitleFragments = ["zoom", "app.zoom.us", "zoom workplace"]
            },
            new()
            {
                Platform = "webex-web",
                SourceType = "browser-window",
                ProcessNames = ["chrome", "msedge"],
                TitleFragments = ["webex", "webex.com", "cisco webex"]
            }
        ];
}
