using MeetingRecorder.Windows.Detection;
using MeetingRecorder.Windows.Interop;
using MeetingRecorder.Windows.Models;
using MeetingRecorder.Windows.Session;
using MeetingRecorder.Shared.Configuration;
using System.Globalization;
using System.Text.Json;

namespace MeetingRecorder.Windows.Shell;

public sealed class RecorderApplicationContext : ApplicationContext
{
    private readonly RecorderWindowsSettings _settings = RecorderSettingsProvider.Current.Windows;
    private readonly BrowserCaptionBridge _browserCaptionBridge = new();
    private readonly CompanionCliClient _companionCliClient = new();
    private readonly CaptureWorkerClient _captureWorkerClient = new();
    private readonly MeetingDetector _detector = new();
    private readonly MeetingsRepository _meetingsRepository = new();
    private readonly LiveCaptionsLauncher _liveCaptionsLauncher = new();
    private readonly System.Windows.Forms.Timer _liveCaptionsTimer;
    private readonly NativeCaptionMonitor _nativeCaptionMonitor = new();
    private readonly ProcessingLauncher _processingLauncher = new();
    private readonly NotifyIcon _trayIcon;
    private readonly SessionManifestWriter _sessionWriter = new();
    private readonly MeetingDashboardStudioForm _widget;
    private readonly Dictionary<string, DateTimeOffset> _suppressions = [];
    private const string MinimizedWarning = "Target window is minimized. Audio capture can continue, but video continuity may freeze until the window is restored.";

    private ActiveRecordingSession? _activeSession;
    private bool _isPaused;
    private bool _autoPromptEnabled;
    private bool _promptVisible;
    private MeetingCandidate? _currentCandidate;
    private DisplayCaptureTarget? _selectedDisplayTarget;
    private DateTimeOffset? _currentCandidateSince;
    private HistoryForm? _historyForm;
    private ActionItemsForm? _actionItemsForm;
    private LiveCaptionsForm? _liveCaptionsForm;
    private string? _promptedSuppressionKey;
    private string _lastCaptionStatus = "Waiting";
    private string _lastCaptionMeta = "Participant hints and caption source details will appear here.";
    private string _lastCaptionText = "Start a recording to stream live captions here.";

    public RecorderApplicationContext()
    {
        ShellDiagnostics.Log("RecorderApplicationContext constructor starting.");
        _liveCaptionsTimer = new System.Windows.Forms.Timer { Interval = Math.Max(100, _settings.LiveCaptions.UiRefreshIntervalMs) };
        _autoPromptEnabled = _settings.Detection.AutoPromptEnabled;
        _widget = new MeetingDashboardStudioForm(_settings);
        MainForm = _widget;

        if (_settings.EnableBrowserCaptionBridge)
        {
            try
            {
                _browserCaptionBridge.Start();
            }
            catch (Exception error)
            {
                ShellDiagnostics.Log("Unable to start the browser caption bridge.", error);
            }
        }
        else
        {
            ShellDiagnostics.Log("Browser caption bridge disabled for startup.");
        }

        _trayIcon = new NotifyIcon
        {
            Icon = AppIconProvider.Icon,
            Text = "Meeting Recorder",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _trayIcon.DoubleClick += (_, _) => ToggleWidget();
        ShellDiagnostics.Log("Tray icon initialized.");

        _widget.PickRequested += async (_, _) => await HandlePickWindowAsync();
        _widget.PickScreenRequested += async (_, _) => await HandlePickScreenAsync();
        _widget.StartRequested += async (_, _) => await StartRecordingAsync();
        _widget.PauseRequested += async (_, _) => await PauseRecordingAsync("manual-pause");
        _widget.ResumeRequested += async (_, _) => await ResumeRecordingAsync();
        _widget.StopRequested += async (_, _) => await StopRecordingAsync("manual-stop");
        _widget.CaptionsRequested += (_, _) => ToggleLiveCaptions();
        _widget.PopoutCaptionsRequested += (_, _) => ShowLiveCaptionsWindow();
        _widget.HistoryRequested += (_, _) => ToggleHistory();
        _widget.ActionsRequested += (_, _) => ToggleActionCenter();
        _widget.SetSelectedDisplay(GetPrimaryDisplayTarget());
        _widget.UpdateState(null, isRecording: false, isPaused: false);

        _detector.CandidateObserved += OnCandidateObserved;
        _liveCaptionsTimer.Tick += (_, _) => RefreshLiveCaptions();
        _detector.Start();
        _widget.Show();
        try
        {
            _trayIcon.ShowBalloonTip(_settings.Notifications.StartupBalloonMs, "Meeting Recorder", "Recorder started. Double-click the tray icon to reopen the dashboard.", ToolTipIcon.Info);
        }
        catch (Exception error)
        {
            ShellDiagnostics.Log("Unable to show startup tray balloon.", error);
        }
        ShellDiagnostics.Log("RecorderApplicationContext constructor completed.");
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show dashboard", null, (_, _) => ToggleWidget());
        menu.Items.Add("Start current meeting", null, async (_, _) => await StartRecordingAsync());
        menu.Items.Add("Pick window and start", null, async (_, _) => await StartRecordingFromPickerAsync());
        menu.Items.Add("Pick screen and start", null, async (_, _) => await StartRecordingFromScreenPickerAsync());
        menu.Items.Add("Pause recording", null, async (_, _) => await PauseRecordingAsync("tray-pause"));
        menu.Items.Add("Resume recording", null, async (_, _) => await ResumeRecordingAsync());
        menu.Items.Add("Stop recording", null, async (_, _) => await StopRecordingAsync("tray-stop"));
        menu.Items.Add("Live captions", null, (_, _) => ShowWidgetSection(WidgetSection.LiveCaptions));
        menu.Items.Add("Pop out live captions", null, (_, _) => ShowLiveCaptionsWindow());
        menu.Items.Add("Meeting history", null, (_, _) => ShowWidgetSection(WidgetSection.History));
        menu.Items.Add("Action center", null, (_, _) => ShowWidgetSection(WidgetSection.ActionCenter));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    private void OnCandidateObserved(object? sender, MeetingCandidate? candidate)
    {
        _currentCandidate = candidate;
        _widget.UpdateState(candidate, _activeSession is not null && !_isPaused, _isPaused, _activeSession?.Manifest.Title);

        if (_activeSession is not null)
        {
            if (!_isPaused)
            {
                HandleActiveSessionWindowState();
            }

            return;
        }

        if (candidate is null)
        {
            _currentCandidateSince = null;
            _promptedSuppressionKey = null;
            return;
        }

        if (!_autoPromptEnabled)
        {
            return;
        }

        if (_currentCandidateSince is null || _promptedSuppressionKey != candidate.SuppressionKey)
        {
            _currentCandidateSince = DateTimeOffset.UtcNow;
            _promptedSuppressionKey = candidate.SuppressionKey;
            return;
        }

        if (DateTimeOffset.UtcNow - _currentCandidateSince < TimeSpan.FromSeconds(_settings.Detection.PromptDelaySeconds))
        {
            return;
        }

        if (_suppressions.TryGetValue(candidate.SuppressionKey, out var until) && until > DateTimeOffset.UtcNow)
        {
            return;
        }

        if (_promptVisible)
        {
            return;
        }

        _promptVisible = true;
        try
        {
            using var prompt = new PromptForm(candidate);
            prompt.ShowDialog();
            switch (prompt.Decision)
            {
                case PromptForm.PromptDecision.Start:
                    _ = StartRecordingAsync();
                    break;
                case PromptForm.PromptDecision.OpenWidget:
                    _suppressions[candidate.SuppressionKey] = DateTimeOffset.UtcNow.AddMinutes(_settings.Detection.PromptSuppressionMinutes);
                    ShowWidgetSection(WidgetSection.LiveCaptions);
                    break;
                default:
                    _suppressions[candidate.SuppressionKey] = DateTimeOffset.UtcNow.AddMinutes(_settings.Detection.PromptSuppressionMinutes);
                    break;
            }
        }
        finally
        {
            _promptVisible = false;
        }
    }

    private async Task StartRecordingAsync()
    {
        if (_activeSession is not null)
        {
            return;
        }

        var candidate = ResolveStartCandidate();
        if (candidate is null)
        {
            MessageBox.Show(
                string.Equals(_widget.SelectedRecordingMode, "audio-only", StringComparison.OrdinalIgnoreCase)
                    ? "Unable to prepare a recording target yet.\r\n\r\nTry again, or bring the window you want to capture to the front first."
                    : _widget.SelectedCaptureSource == VideoCaptureSource.Window
                        ? "No window target is ready yet.\r\n\r\nUse Pick window to choose the app you want to record, or switch Capture source to Full screen for a manual screen recording."
                        : "No recording target is ready yet.\r\n\r\nTry again, or use Pick window to choose a specific app before starting.",
                "Meeting Recorder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        try
        {
            var captureSelection = ResolveCaptureSelection(candidate);
            var session = _sessionWriter.CreateSession(candidate, captureSelection, _widget.SelectedRecordingMode);
            var workerEvent = await _captureWorkerClient.BeginSessionAsync(session.Manifest);
            if (workerEvent.AudioCaptureMode is not null)
            {
                session.Manifest.AudioCaptureMode = workerEvent.AudioCaptureMode;
            }

            _sessionWriter.UpdateRecordingState(
                session,
                workerEvent.AudioCaptureMode,
                workerEvent.Message is null ? null : [workerEvent.Message]
            );
            _activeSession = session;
            _isPaused = false;
            _autoPromptEnabled = false;
            _suppressions[candidate.SuppressionKey] = DateTimeOffset.UtcNow.AddMinutes(_settings.Detection.PromptSuppressionMinutes);
            _browserCaptionBridge.SetActiveSession(session);
            _nativeCaptionMonitor.Start(session);
            StartLiveCaptions(session.SessionDirectory);
            var captureSummary = string.Equals(session.Manifest.MediaCaptureMode, "audio-only", StringComparison.OrdinalIgnoreCase)
                ? "audio only"
                : captureSelection.Source == VideoCaptureSource.Display
                    ? $"full screen on {captureSelection.Display?.DisplayName ?? "selected screen"}"
                    : "the meeting window";
            _trayIcon.ShowBalloonTip(_settings.Notifications.SessionBalloonMs, "Meeting Recorder", $"Recording started for {candidate.Title} using {captureSummary}.", ToolTipIcon.Info);
            _widget.UpdateState(candidate, isRecording: true, isPaused: false, activeSessionTitle: session.Manifest.Title);
        }
        catch (Exception error)
        {
            ShellDiagnostics.Log("Unable to start recording.", error);
            MessageBox.Show($"Unable to start recording.\r\n\r\n{error.Message}", "Meeting Recorder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private MeetingCandidate? ResolveStartCandidate()
    {
        if (_currentCandidate is not null)
        {
            return _currentCandidate;
        }

        var manualWindowCandidate = _detector.GetCurrentWindowCandidate();
        if (manualWindowCandidate is not null)
        {
            return manualWindowCandidate;
        }

        return BuildManualCaptureCandidate();
    }

    private MeetingCandidate? BuildManualCaptureCandidate()
    {
        var isAudioOnly = string.Equals(_widget.SelectedRecordingMode, "audio-only", StringComparison.OrdinalIgnoreCase);
        if (!isAudioOnly && _widget.SelectedCaptureSource == VideoCaptureSource.Window)
        {
            return null;
        }

        var display = _selectedDisplayTarget ?? GetPrimaryDisplayTarget();
        var title = isAudioOnly
            ? "Manual audio recording"
            : $"Manual screen recording - {display.DisplayName}";
        var windowTitle = isAudioOnly ? title : display.Description;
        var platform = isAudioOnly ? "manual-audio" : "manual-screen";

        return new MeetingCandidate(
            platform,
            "manual-start",
            title,
            windowTitle,
            "MeetingRecorder",
            Environment.ProcessId,
            nint.Zero
        );
    }

    private async Task PauseRecordingAsync(string reason)
    {
        var session = _activeSession;
        if (session is null || _isPaused)
        {
            return;
        }

        try
        {
            var workerEvent = await _captureWorkerClient.PauseSessionAsync(session.Manifest.SessionId, reason);
            _sessionWriter.MarkPaused(
                session,
                reason,
                workerEvent.AudioCaptureMode,
                workerEvent.Message is null ? null : [workerEvent.Message]
            );
            _isPaused = true;
            _browserCaptionBridge.SetActiveSession(null);
            _nativeCaptionMonitor.Stop();
            UpdateCaptionDisplays("Captions paused", string.IsNullOrWhiteSpace(_lastCaptionText)
                ? "Resume recording to continue live captions."
                : _lastCaptionText);
            _trayIcon.ShowBalloonTip(_settings.Notifications.SessionBalloonMs, "Meeting Recorder", $"Recording paused for {session.Manifest.Title}", ToolTipIcon.Info);
            _widget.UpdateState(_currentCandidate, isRecording: false, isPaused: true, activeSessionTitle: session.Manifest.Title);
        }
        catch (Exception error)
        {
            ShellDiagnostics.Log("Unable to pause recording.", error);
            MessageBox.Show($"Unable to pause recording.\r\n\r\n{error.Message}", "Meeting Recorder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ResumeRecordingAsync()
    {
        var session = _activeSession;
        if (session is null || !_isPaused)
        {
            return;
        }

        try
        {
            var workerEvent = await _captureWorkerClient.ResumeSessionAsync(session.Manifest);
            _sessionWriter.MarkResumed(
                session,
                workerEvent.AudioCaptureMode,
                workerEvent.Message is null ? null : [workerEvent.Message]
            );
            _isPaused = false;
            _browserCaptionBridge.SetActiveSession(session);
            _nativeCaptionMonitor.Start(session);
            _trayIcon.ShowBalloonTip(_settings.Notifications.SessionBalloonMs, "Meeting Recorder", $"Recording resumed for {session.Manifest.Title}", ToolTipIcon.Info);
            _widget.UpdateState(_currentCandidate, isRecording: true, isPaused: false, activeSessionTitle: session.Manifest.Title);
        }
        catch (Exception error)
        {
            ShellDiagnostics.Log("Unable to resume recording.", error);
            MessageBox.Show($"Unable to resume recording.\r\n\r\n{error.Message}", "Meeting Recorder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task StartRecordingFromPickerAsync()
    {
        if (_activeSession is not null)
        {
            return;
        }

        var candidates = _detector.GetAvailableWindowCandidates();
        if (candidates.Count == 0)
        {
            MessageBox.Show(
                "No visible windows were found to record.\r\n\r\nBring the meeting window to the front, then try again.",
                "Meeting Recorder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        if (candidates.Count == 1)
        {
            _currentCandidate = candidates[0];
            await StartRecordingAsync();
            return;
        }

        using var picker = new WindowPickerForm(candidates);
        if (picker.ShowDialog() == DialogResult.OK && picker.SelectedCandidate is not null)
        {
            _currentCandidate = picker.SelectedCandidate;
            await StartRecordingAsync();
        }
    }

    private async Task StartRecordingFromScreenPickerAsync()
    {
        if (_activeSession is not null)
        {
            return;
        }

        PickScreen();
        await StartRecordingAsync();
    }

    private async Task HandlePickWindowAsync()
    {
        if (_activeSession is not null)
        {
            await SwitchActiveCaptureToWindowAsync();
            return;
        }

        await StartRecordingFromPickerAsync();
    }

    private async Task HandlePickScreenAsync()
    {
        if (_activeSession is not null)
        {
            await SwitchActiveCaptureToScreenAsync();
            return;
        }

        PickScreen();
    }

    private async Task StopRecordingAsync(string reason)
    {
        var session = _activeSession;
        if (session is null)
        {
            return;
        }

        _activeSession = null;
        _isPaused = false;
        _browserCaptionBridge.SetActiveSession(null);
        _nativeCaptionMonitor.Stop();
        _liveCaptionsTimer.Stop();
        _liveCaptionsLauncher.Stop();
        _widget.UpdateState(_currentCandidate, isRecording: false, isPaused: false);
        UpdateCaptionDisplays("Finalizing in background", string.IsNullOrWhiteSpace(_lastCaptionText)
            ? "The previous session is finishing in the background. You can start a new recording now."
            : $"{_lastCaptionText}{Environment.NewLine}{Environment.NewLine}[Recorder] Finalizing the previous session in the background. You can start a new recording now.");

        try
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var workerEvent = await _captureWorkerClient.StopSessionAsync(session.Manifest.SessionId, reason);
                    _sessionWriter.MarkStopped(
                        session,
                        reason,
                        workerEvent.AudioCaptureMode,
                        workerEvent.Message is null ? null : [workerEvent.Message]
                    );
                    _processingLauncher.Launch(session.SessionDirectory);
                    RunOnUiThread(() =>
                    {
                        RefreshHostedSessions();
                        _trayIcon.ShowBalloonTip(
                            _settings.Notifications.SessionBalloonMs,
                            "Meeting Recorder",
                            $"Stopped {session.Manifest.Title}. Processing is running in the background.",
                            ToolTipIcon.Info);
                    });
                }
                catch (Exception error)
                {
                    ShellDiagnostics.Log("Unable to stop recording cleanly.", error);
                    RunOnUiThread(() =>
                    {
                        _trayIcon.ShowBalloonTip(
                            _settings.Notifications.WarningBalloonMs,
                            "Meeting Recorder",
                            $"Background finalization failed for {session.Manifest.Title}. Check the logs.",
                            ToolTipIcon.Warning);
                    });
                }
            });
        }
        catch (Exception error)
        {
            ShellDiagnostics.Log("Unable to stop recording cleanly.", error);
            MessageBox.Show($"Unable to stop recording cleanly.\r\n\r\n{error.Message}", "Meeting Recorder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ToggleWidget()
    {
        if (_widget.Visible)
        {
            _widget.Hide();
        }
        else
        {
            _widget.UpdateState(_currentCandidate, _activeSession is not null && !_isPaused, _isPaused, _activeSession?.Manifest.Title);
            _widget.Show();
        }
    }

    private void PickScreen()
    {
        var displays = GetAvailableDisplays();
        if (displays.Count == 0)
        {
            return;
        }

        if (displays.Count == 1)
        {
            _selectedDisplayTarget = displays[0];
            _widget.SetSelectedDisplay(_selectedDisplayTarget);
            return;
        }

        using var picker = new ScreenPickerForm(displays, _selectedDisplayTarget);
        if (picker.ShowDialog() == DialogResult.OK && picker.SelectedDisplay is not null)
        {
            _selectedDisplayTarget = picker.SelectedDisplay;
            _widget.SetSelectedDisplay(_selectedDisplayTarget);
        }
    }

    private async Task SwitchActiveCaptureToScreenAsync()
    {
        var session = _activeSession;
        if (session is null || _isPaused)
        {
            return;
        }

        PickScreen();
        var display = _selectedDisplayTarget ?? GetPrimaryDisplayTarget();

        try
        {
            var workerEvent = await _captureWorkerClient.SwitchToDisplayAsync(session.Manifest, display);
            _sessionWriter.UpdateCaptureTarget(
                session,
                new VideoCaptureSelection(VideoCaptureSource.Display, display),
                note: $"Switched active capture to {display.DisplayName}.");
            _widget.UpdateState(_currentCandidate, isRecording: true, isPaused: false, activeSessionTitle: session.Manifest.Title);
            _trayIcon.ShowBalloonTip(
                _settings.Notifications.SessionBalloonMs,
                "Meeting Recorder",
                workerEvent.Message ?? $"Recording switched to {display.DisplayName}.",
                ToolTipIcon.Info);
        }
        catch (Exception error)
        {
            ShellDiagnostics.Log("Unable to switch the active recording to a different screen.", error);
            MessageBox.Show($"Unable to switch the active recording to the selected screen.\r\n\r\n{error.Message}", "Meeting Recorder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SwitchActiveCaptureToWindowAsync()
    {
        var session = _activeSession;
        if (session is null || _isPaused)
        {
            return;
        }

        var candidates = _detector.GetAvailableWindowCandidates();
        if (candidates.Count == 0)
        {
            MessageBox.Show(
                "No visible windows were found to switch to.\r\n\r\nBring the target window to the front, then try again.",
                "Meeting Recorder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        MeetingCandidate? selectedCandidate = null;
        if (candidates.Count == 1)
        {
            selectedCandidate = candidates[0];
        }
        else
        {
            using var picker = new WindowPickerForm(candidates);
            if (picker.ShowDialog() == DialogResult.OK)
            {
                selectedCandidate = picker.SelectedCandidate;
            }
        }

        if (selectedCandidate is null)
        {
            return;
        }

        try
        {
            var workerEvent = await _captureWorkerClient.SwitchToWindowAsync(session.Manifest, selectedCandidate);
            _currentCandidate = selectedCandidate;
            _sessionWriter.UpdateCaptureTarget(
                session,
                new VideoCaptureSelection(VideoCaptureSource.Window, null),
                selectedCandidate,
                note: $"Switched active capture to window {selectedCandidate.Title}.");
            _widget.UpdateState(_currentCandidate, isRecording: true, isPaused: false, activeSessionTitle: session.Manifest.Title);
            _trayIcon.ShowBalloonTip(
                _settings.Notifications.SessionBalloonMs,
                "Meeting Recorder",
                workerEvent.Message ?? $"Recording switched to {selectedCandidate.Title}.",
                ToolTipIcon.Info);
        }
        catch (Exception error)
        {
            ShellDiagnostics.Log("Unable to switch the active recording to a different window.", error);
            MessageBox.Show($"Unable to switch the active recording to the selected window.\r\n\r\n{error.Message}", "Meeting Recorder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ToggleLiveCaptions() => ShowWidgetSection(WidgetSection.LiveCaptions);

    private void ShowLiveCaptionsWindow()
    {
        _liveCaptionsForm ??= new LiveCaptionsForm();
        _liveCaptionsForm.UpdateCaptions(_lastCaptionStatus, _lastCaptionText, _lastCaptionMeta);
        if (!_liveCaptionsForm.Visible)
        {
            _liveCaptionsForm.Show();
        }

        _liveCaptionsForm.BringToFront();
        _liveCaptionsForm.Activate();
    }

    private void ToggleHistory() => ShowWidgetSection(WidgetSection.History);

    private void ToggleActionCenter() => ShowWidgetSection(WidgetSection.ActionCenter);

    private void ReprocessSession(string sessionDirectory)
    {
        _processingLauncher.Launch(sessionDirectory);
        _trayIcon.ShowBalloonTip(_settings.Notifications.SessionBalloonMs, "Meeting Recorder", "Reprocessing started for the selected meeting.", ToolTipIcon.Info);
    }

    private void StartLiveCaptions(string sessionDirectory)
    {
        try
        {
            _liveCaptionsLauncher.Start(sessionDirectory);
            UpdateCaptionDisplays("Listening", string.IsNullOrWhiteSpace(_lastCaptionText)
                ? "Listening for speech..."
                : _lastCaptionText);
            _liveCaptionsTimer.Start();
        }
        catch (Exception error)
        {
            ShellDiagnostics.Log("Unable to start live captions.", error);
            UpdateCaptionDisplays("Unavailable", error.Message);
        }
    }

    private void RefreshLiveCaptions()
    {
        var session = _activeSession;
        var captionsPath = ResolvePreferredCaptionsPath(session);
        if (string.IsNullOrWhiteSpace(captionsPath) || !File.Exists(captionsPath))
        {
            return;
        }

        try
        {
            var json = TryReadSharedText(captionsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            using var document = JsonDocument.Parse(json);
            var status = document.RootElement.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString() ?? "Listening"
                : "Listening";
            var text = document.RootElement.TryGetProperty("text", out var textElement)
                ? textElement.GetString() ?? string.Empty
                : string.Empty;
            var lines = document.RootElement.TryGetProperty("lines", out var linesElement) &&
                        linesElement.ValueKind == JsonValueKind.Array
                ? linesElement.EnumerateArray()
                    .Select(item => item.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .ToArray()
                : [];
            var participants = document.RootElement.TryGetProperty("participants", out var participantsElement) &&
                               participantsElement.ValueKind == JsonValueKind.Array
                ? participantsElement.EnumerateArray()
                    .Select(item => item.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
            var tone = document.RootElement.TryGetProperty("tone", out var toneElement)
                ? toneElement.GetString()
                : null;
            var speaker = document.RootElement.TryGetProperty("speaker", out var speakerElement)
                ? speakerElement.GetString()
                : null;
            var speakerSimilarity = document.RootElement.TryGetProperty("speakerSimilarity", out var speakerSimilarityElement) &&
                                    speakerSimilarityElement.ValueKind == JsonValueKind.Number &&
                                    speakerSimilarityElement.TryGetDouble(out var parsedSpeakerSimilarity)
                ? parsedSpeakerSimilarity
                : (double?)null;
            var toneConfidence = document.RootElement.TryGetProperty("toneConfidence", out var toneConfidenceElement) &&
                                 toneConfidenceElement.ValueKind == JsonValueKind.Number &&
                                 toneConfidenceElement.TryGetDouble(out var parsedConfidence)
                ? parsedConfidence
                : (double?)null;
            var latencyMs = document.RootElement.TryGetProperty("latencyMs", out var latencyElement) &&
                            latencyElement.ValueKind == JsonValueKind.Number &&
                            latencyElement.TryGetInt32(out var parsedLatency)
                ? parsedLatency
                : (int?)null;
            if (lines.Length > 0)
            {
                text = string.Join(Environment.NewLine, lines);
            }
            var metaParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(speaker))
            {
                metaParts.Add(speakerSimilarity.HasValue
                    ? $"Speaker: {speaker} ({speakerSimilarity.Value:0.00})"
                    : $"Speaker: {speaker}");
            }

            if (!string.IsNullOrWhiteSpace(tone))
            {
                metaParts.Add(toneConfidence.HasValue
                    ? $"Tone: {tone} ({toneConfidence.Value:0.00})"
                    : $"Tone: {tone}");
            }

            if (latencyMs.HasValue)
            {
                metaParts.Add($"Latency: {latencyMs.Value} ms");
            }

            if (participants.Length > 0)
            {
                metaParts.Add($"Participants: {string.Join(", ", participants)}");
            }

            UpdateCaptionDisplays(
                FormatCaptionStatus(status),
                text,
                metaParts.Count == 0
                    ? "Participant hints and caption source details will appear here."
                    : string.Join(" | ", metaParts));
        }
        catch (Exception error)
        {
            ShellDiagnostics.Log("Unable to refresh live captions.", error);
        }
    }

    private static string? TryReadSharedText(string path, int attempts = 4, int delayMs = 30)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < attempts - 1)
            {
                Thread.Sleep(delayMs);
            }
        }

        return null;
    }

    private static string FormatCaptionStatus(string status)
        => status switch
        {
            "waiting" => "Listening",
            "listening" => "Live captions",
            "browser-listening" => "Browser captions",
            "paused" => "Captions paused",
            "stopped" => "Captions finalized",
            "failed" => "Captions failed",
            "unavailable" => "Captions unavailable",
            _ => status
        };

    private static string? ResolvePreferredCaptionsPath(ActiveRecordingSession? session)
    {
        if (session is null)
        {
            return null;
        }

        var browserCaptionsPath = SessionPathLayout.Resolve(session.SessionDirectory, "browser-captions.json");
        if (File.Exists(browserCaptionsPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(browserCaptionsPath));
                var status = document.RootElement.TryGetProperty("status", out var statusElement)
                    ? statusElement.GetString()
                    : null;
                var text = document.RootElement.TryGetProperty("text", out var textElement)
                    ? textElement.GetString()
                    : null;
                if (string.Equals(status, "listening", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(text))
                {
                    return browserCaptionsPath;
                }
            }
            catch
            {
            }
        }

        var nativeCaptionsPath = SessionPathLayout.Resolve(session.SessionDirectory, "native-captions.json");
        if (File.Exists(nativeCaptionsPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(nativeCaptionsPath));
                var status = document.RootElement.TryGetProperty("status", out var statusElement)
                    ? statusElement.GetString()
                    : null;
                var text = document.RootElement.TryGetProperty("text", out var textElement)
                    ? textElement.GetString()
                    : null;
                if (string.Equals(status, "listening", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(text))
                {
                    return nativeCaptionsPath;
                }
            }
            catch
            {
            }
        }

        return session.Manifest.Paths.LiveCaptionsJson;
    }

    private void HandleActiveSessionWindowState()
    {
        if (_activeSession is null)
        {
            return;
        }

        if (!string.Equals(_activeSession.Manifest.VideoCaptureMode, "window", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!TryParseWindowHandle(_activeSession.Manifest.WindowHandle, out var hwnd))
        {
            return;
        }

        if (!MeetingDetector.IsWindowMinimized(hwnd))
        {
            return;
        }

        if (_activeSession.Manifest.CaptureWarnings.Contains(MinimizedWarning, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _sessionWriter.UpdateRecordingState(_activeSession, warnings: [MinimizedWarning]);
        _trayIcon.ShowBalloonTip(_settings.Notifications.WarningBalloonMs, "Meeting Recorder", "Meeting window minimized. Audio can continue, but video may pause until the window is restored.", ToolTipIcon.Warning);
        _widget.UpdateState(_currentCandidate, isRecording: true, isPaused: false, activeSessionTitle: _activeSession.Manifest.Title);
    }

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

        if (!long.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        hwnd = new nint(value);
        return hwnd != nint.Zero;
    }

    private VideoCaptureSelection ResolveCaptureSelection(MeetingCandidate candidate)
    {
        if (string.Equals(_widget.SelectedRecordingMode, "audio-only", StringComparison.OrdinalIgnoreCase))
        {
            return new VideoCaptureSelection(VideoCaptureSource.Window, null);
        }

        if (_widget.SelectedCaptureSource == VideoCaptureSource.Window)
        {
            return new VideoCaptureSelection(VideoCaptureSource.Window, null);
        }

        var display = ResolvePreferredDisplay(candidate);
        _selectedDisplayTarget = display;
        _widget.SetSelectedDisplay(display);
        return new VideoCaptureSelection(VideoCaptureSource.Display, display);
    }

    private DisplayCaptureTarget ResolvePreferredDisplay(MeetingCandidate candidate)
    {
        var displays = GetAvailableDisplays();
        if (_selectedDisplayTarget is not null)
        {
            var selected = displays.FirstOrDefault(display =>
                string.Equals(display.DeviceName, _selectedDisplayTarget.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                return selected;
            }
        }

        try
        {
            var screen = Screen.FromHandle(candidate.WindowHandle);
            return DisplayCaptureTarget.FromScreen(screen);
        }
        catch
        {
            return GetPrimaryDisplayTarget();
        }
    }

    private static DisplayCaptureTarget GetPrimaryDisplayTarget()
        => Screen.AllScreens
            .Select(DisplayCaptureTarget.FromScreen)
            .OrderByDescending(display => display.IsPrimary)
            .First();

    private static IReadOnlyList<DisplayCaptureTarget> GetAvailableDisplays()
        => Screen.AllScreens
            .Select(DisplayCaptureTarget.FromScreen)
            .OrderByDescending(display => display.IsPrimary)
            .ThenBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void ShowWidgetSection(WidgetSection section)
    {
        EnsureSectionForm(section);
        RefreshHostedSessions();
        _widget.UpdateState(_currentCandidate, _activeSession is not null && !_isPaused, _isPaused, _activeSession?.Manifest.Title);
        _widget.SelectSection(section);
    }

    private void EnsureSectionForm(WidgetSection section)
    {
        switch (section)
        {
            case WidgetSection.History:
                EnsureHistoryForm();
                break;
            case WidgetSection.ActionCenter:
                EnsureActionItemsForm();
                break;
        }
    }

    private void EnsureHistoryForm()
    {
        if (_historyForm is not null)
        {
            return;
        }

        var embeddedMode = !_settings.Dashboard.ShowEmbeddedWorkspaceHeaders;
        _historyForm = new HistoryForm(_meetingsRepository, _companionCliClient, embedded: embeddedMode);
        _widget.AttachHistoryForm(_historyForm);
    }

    private void EnsureActionItemsForm()
    {
        if (_actionItemsForm is not null)
        {
            return;
        }

        var embeddedMode = !_settings.Dashboard.ShowEmbeddedWorkspaceHeaders;
            _actionItemsForm = new ActionItemsForm(_meetingsRepository, _companionCliClient, ReprocessSession, embedded: embeddedMode);
        _widget.AttachActionItemsForm(_actionItemsForm);
    }

    private void RefreshHostedSessions()
    {
        _historyForm?.RefreshSessions();
        _actionItemsForm?.RefreshSessions();
    }

    private void UpdateCaptionDisplays(string status, string text, string? meta = null)
    {
        _lastCaptionStatus = string.IsNullOrWhiteSpace(status) ? "Live captions" : status;
        _lastCaptionMeta = string.IsNullOrWhiteSpace(meta)
            ? "Participant hints and caption source details will appear here."
            : meta.Trim();
        _lastCaptionText = string.IsNullOrWhiteSpace(text) ? "Listening for speech..." : text;
        _widget.UpdateCaptions(_lastCaptionStatus, _lastCaptionText, _lastCaptionMeta);
        _liveCaptionsForm?.UpdateCaptions(_lastCaptionStatus, _lastCaptionText, _lastCaptionMeta);
    }

    private void RunOnUiThread(Action action)
    {
        if (_widget.IsDisposed || !_widget.IsHandleCreated)
        {
            return;
        }

        try
        {
            _widget.BeginInvoke(action);
        }
        catch
        {
        }
    }

    protected override void ExitThreadCore()
    {
        ShellDiagnostics.Log("RecorderApplicationContext exiting.");
        _detector.Dispose();
        _liveCaptionsTimer.Stop();
        _liveCaptionsTimer.Dispose();
        _browserCaptionBridge.Dispose();
        _nativeCaptionMonitor.Dispose();
        _liveCaptionsLauncher.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _widget.PrepareForExit();
        _widget.Dispose();
        _liveCaptionsForm?.Dispose();
        _historyForm?.Dispose();
        _actionItemsForm?.Dispose();
        base.ExitThreadCore();
    }
}
