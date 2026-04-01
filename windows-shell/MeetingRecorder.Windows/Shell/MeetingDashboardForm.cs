using System.Diagnostics;
using MeetingRecorder.Shared.Configuration;
using MeetingRecorder.Windows.Models;

namespace MeetingRecorder.Windows.Shell;

public sealed class MeetingDashboardForm : Form
{
    private enum DashboardLayoutMode
    {
        Wide,
        Medium,
        Stacked
    }

    private readonly RecorderWindowsSettings _settings;
    private readonly ComboBox _captureModeCombo;
    private readonly ComboBox _recordingModeCombo;
    private readonly UiButton _pickScreenButton;
    private readonly UiButton _pickButton;
    private readonly UiButton _startButton;
    private readonly UiButton _pauseButton;
    private readonly UiButton _resumeButton;
    private readonly UiButton _stopButton;
    private readonly UiButton _meetingsFolderButton;
    private readonly UiButton _resetLayoutButton;
    private readonly UiButton _hideButton;
    private readonly Label _footerLabel;
    private readonly Label _captionsStatusLabel;
    private readonly Label _captionsMetaLabel;
    private readonly Label _stateTitleLabel;
    private readonly Label _stateHintLabel;
    private readonly Label _meetingValueLabel;
    private readonly Label _modeValueLabel;
    private readonly Label _captureValueLabel;
    private readonly Label _nextStepValueLabel;
    private readonly Label _workspaceSummaryLabel;
    private readonly Label _startAvailabilityLabel;
    private readonly Label _topBarSubtitleLabel = CreateMutedLabel(string.Empty);
    private readonly TextBox _captionsBox;
    private readonly TextBox _statusBox;
    private readonly UiBadgeLabel _statusBadge;
    private readonly UiTabControl _workspaceTabs;
    private readonly Panel _historyHostPanel;
    private readonly Panel _actionsHostPanel;
    private readonly Panel _statusHostPanel;
    private readonly Panel _controlsHostPanel;
    private readonly Panel _topSectionHostPanel;
    private readonly Splitter _topSectionSplitter;
    private readonly Splitter _topColumnsSplitter;
    private readonly TableLayoutPanel _sourceGrid;
    private readonly FlowLayoutPanel _pickerRow;
    private readonly FlowLayoutPanel _transportRow;
    private readonly FlowLayoutPanel _topBarActions;

    private int _savedStatusPanelWidth;
    private int _savedTopSectionHeight;
    private bool _allowClose;
    private bool _suppressTabEvents;
    private string _selectedDisplayLabel = "Display will follow the meeting window";
    private MeetingCandidate? _lastCandidate;
    private bool _lastIsPaused;
    private bool _lastIsRecording;
    private string? _lastActiveSessionTitle;
    private WidgetSection _selectedSection = WidgetSection.LiveCaptions;
    private DashboardLayoutMode _layoutMode;

    public MeetingDashboardForm(RecorderWindowsSettings settings)
    {
        _settings = settings;
        _savedStatusPanelWidth = Math.Max(280, _settings.Dashboard.DefaultStatusPanelWidth);
        _savedTopSectionHeight = Math.Max(240, _settings.Dashboard.DefaultTopSectionHeight);
        var buttonGap = Math.Max(4, _settings.Dashboard.CommandButtonGap);

        UiTheme.ApplyForm(this);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(
            Math.Max(1024, _settings.Dashboard.MinWindowWidth),
            Math.Max(720, _settings.Dashboard.MinWindowHeight));
        StartPosition = FormStartPosition.CenterScreen;
        Width = Math.Max(MinimumSize.Width, _settings.Dashboard.WindowWidth);
        Height = Math.Max(MinimumSize.Height, _settings.Dashboard.WindowHeight);
        Text = "Meeting Recorder";
        Icon = AppIconProvider.Icon;

        _pickScreenButton = CreateButton("Pick screen", UiButtonTone.Secondary, _settings.Dashboard.PickerButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _pickButton = CreateButton("Pick window", UiButtonTone.Neutral, _settings.Dashboard.PickerButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _startButton = CreateButton("Start", UiButtonTone.Primary, _settings.Dashboard.PrimaryActionButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _pauseButton = CreateButton("Pause", UiButtonTone.Neutral, _settings.Dashboard.PrimaryActionButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _resumeButton = CreateButton("Resume", UiButtonTone.Secondary, _settings.Dashboard.PrimaryActionButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _stopButton = CreateButton("Stop", UiButtonTone.Danger, _settings.Dashboard.PrimaryActionButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _meetingsFolderButton = CreateButton("Meetings folder", UiButtonTone.Ghost, _settings.Dashboard.MeetingsFolderButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _resetLayoutButton = CreateButton("Reset layout", UiButtonTone.Ghost, _settings.Dashboard.ResetLayoutButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _hideButton = CreateButton("Hide app", UiButtonTone.Ghost, _settings.Dashboard.HideButtonWidth, _settings.Dashboard.ControlButtonHeight);

        _meetingsFolderButton.Click += (_, _) => OpenMeetingsFolder();
        _resetLayoutButton.Click += (_, _) => ResetLayout();
        _hideButton.Click += (_, _) => Hide();
        foreach (var button in new[]
                 {
                     _pickScreenButton,
                     _pickButton,
                     _startButton,
                     _pauseButton,
                     _resumeButton,
                     _stopButton,
                     _meetingsFolderButton,
                     _resetLayoutButton,
                     _hideButton
                 })
        {
            button.Margin = new Padding(0, 0, buttonGap, 8);
        }

        _statusBadge = new UiBadgeLabel
        {
            Text = "Waiting",
            Width = 110,
            Height = 28,
            FillColor = UiTheme.SurfaceTint,
            BorderColor = UiTheme.SurfaceTint,
            TextColor = UiTheme.TextMuted,
            Margin = new Padding(0, 4, 0, 0)
        };

        _topBarActions = CreateInlineButtonRow();
        _topBarActions.WrapContents = true;
        _topBarActions.Margin = Padding.Empty;
        _topBarActions.Padding = Padding.Empty;
        _topBarActions.Controls.AddRange([_meetingsFolderButton, _resetLayoutButton, _hideButton, _statusBadge]);

        var topBar = CreateTopBar();

        _stateTitleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(UiTheme.SectionFont.FontFamily, 14F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = UiTheme.TextPrimary,
            Text = "Waiting for a meeting"
        };

        _stateHintLabel = CreateMutedLabel("Recording can start manually at any time, or you can wait for meeting detection.");
        _meetingValueLabel = CreateStatusValueLabel();
        _modeValueLabel = CreateStatusValueLabel();
        _captureValueLabel = CreateStatusValueLabel();
        _nextStepValueLabel = CreateStatusValueLabel();

        var factsTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8),
            Padding = Padding.Empty
        };
        factsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
        factsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        factsTable.Controls.Add(CreateStatusKeyLabel("Meeting"), 0, 0);
        factsTable.Controls.Add(_meetingValueLabel, 1, 0);
        factsTable.Controls.Add(CreateStatusKeyLabel("Mode"), 0, 1);
        factsTable.Controls.Add(_modeValueLabel, 1, 1);
        factsTable.Controls.Add(CreateStatusKeyLabel("Capture"), 0, 2);
        factsTable.Controls.Add(_captureValueLabel, 1, 2);
        factsTable.Controls.Add(CreateStatusKeyLabel("Next"), 0, 3);
        factsTable.Controls.Add(_nextStepValueLabel, 1, 3);

        _statusBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = _settings.Dashboard.EnableHorizontalTextScroll && !_settings.Dashboard.StatusWordWrap
                ? ScrollBars.Both
                : ScrollBars.Vertical,
            WordWrap = _settings.Dashboard.StatusWordWrap
        };
        UiTheme.StyleTextBox(_statusBox, readOnly: true);

        var statusBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 6, 0, 0)
        };
        statusBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        statusBody.Controls.Add(CreateVerticalStack(_stateTitleLabel, _stateHintLabel), 0, 0);
        statusBody.Controls.Add(factsTable, 0, 1);
        statusBody.Controls.Add(CreateMinorSectionLabel("Details"), 0, 2);
        statusBody.Controls.Add(_statusBox, 0, 3);

        var statusCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            Padding = new Padding(16)
        };
        statusCard.Controls.Add(CreateCardContentLayout(
            UiTheme.CreateSectionTitle("Session rail", "Meeting target, capture target, and the next safe action."),
            statusBody));

        _recordingModeCombo = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        UiTheme.StyleComboBox(_recordingModeCombo);
        _recordingModeCombo.Items.AddRange(["Screen + audio", "Audio only"]);
        _recordingModeCombo.SelectedIndex = string.Equals(_settings.DefaultRecordingMode, "audio-only", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _recordingModeCombo.SelectedIndexChanged += (_, _) => RenderState();

        _captureModeCombo = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        UiTheme.StyleComboBox(_captureModeCombo);
        _captureModeCombo.Items.AddRange(["Full screen", "Meeting window"]);
        _captureModeCombo.SelectedIndex = string.Equals(_settings.DefaultCaptureSource, "window", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _captureModeCombo.SelectedIndexChanged += (_, _) => RenderState();

        _sourceGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 10),
            Padding = Padding.Empty
        };

        _pickerRow = CreateInlineButtonRow();
        _pickerRow.Controls.AddRange([_pickScreenButton, _pickButton]);

        _transportRow = CreateInlineButtonRow();
        _transportRow.Controls.AddRange([_startButton, _pauseButton, _resumeButton, _stopButton]);

        _startAvailabilityLabel = CreateMutedLabel("Start can begin a manual recording immediately, or you can pick a specific window first.");

        var controlsBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 6, 0, 0)
        };
        controlsBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        controlsBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controlsBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controlsBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controlsBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controlsBody.Controls.Add(_sourceGrid, 0, 0);
        controlsBody.Controls.Add(_pickerRow, 0, 1);
        controlsBody.Controls.Add(_transportRow, 0, 2);
        controlsBody.Controls.Add(_startAvailabilityLabel, 0, 3);

        var controlsCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Success,
            Padding = new Padding(16)
        };
        controlsCard.Controls.Add(CreateCardContentLayout(
            UiTheme.CreateSectionTitle("Recording controls", "Pick the source, then run the session from one compact control area."),
            controlsBody));

        _statusHostPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = _savedStatusPanelWidth,
            MinimumSize = new Size(280, 0),
            BackColor = Color.Transparent,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        _statusHostPanel.Controls.Add(statusCard);

        _controlsHostPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        _controlsHostPanel.Controls.Add(controlsCard);

        _topColumnsSplitter = CreateDashboardSplitter(DockStyle.Left);
        _topColumnsSplitter.SplitterMoved += (_, _) =>
        {
            if (_layoutMode != DashboardLayoutMode.Stacked)
            {
                _savedStatusPanelWidth = _statusHostPanel.Width;
                RefreshFooterHint();
            }
        };

        _topSectionHostPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = _savedTopSectionHeight,
            MinimumSize = new Size(0, 240),
            BackColor = Color.Transparent,
            Padding = Padding.Empty,
            Margin = new Padding(0, 0, 0, 12)
        };
        _topSectionHostPanel.Controls.Add(_controlsHostPanel);
        _topSectionHostPanel.Controls.Add(_topColumnsSplitter);
        _topSectionHostPanel.Controls.Add(_statusHostPanel);

        _workspaceSummaryLabel = CreateMutedLabel("Keep the current workspace visible while captions, history, and follow-up stay one tab away.");
        _workspaceSummaryLabel.Dock = DockStyle.Top;
        _workspaceSummaryLabel.Margin = new Padding(0, 0, 0, 8);

        _captionsStatusLabel = CreateMinorSectionLabel("Live caption stream");
        _captionsMetaLabel = CreateMutedLabel("Participant hints and caption diagnostics will appear here when available.");
        _captionsMetaLabel.Dock = DockStyle.Top;
        _captionsMetaLabel.Margin = new Padding(0, 0, 0, 8);

        _captionsBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = _settings.Dashboard.EnableHorizontalTextScroll ? ScrollBars.Both : ScrollBars.Vertical,
            WordWrap = _settings.Dashboard.CaptionsWordWrap
        };
        UiTheme.StyleTextBox(_captionsBox, readOnly: true);
        _captionsBox.Text = "Start a recording to stream caption content into this panel.";

        var captionsBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 6, 0, 0)
        };
        captionsBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        captionsBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        captionsBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        captionsBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        captionsBody.Controls.Add(_captionsStatusLabel, 0, 0);
        captionsBody.Controls.Add(_captionsMetaLabel, 0, 1);
        captionsBody.Controls.Add(_captionsBox, 0, 2);

        var captionsCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Warning,
            Padding = new Padding(16)
        };
        captionsCard.Controls.Add(CreateCardContentLayout(
            UiTheme.CreateSectionTitle("Live captions", "A clean caption surface with status and participant hints above it."),
            captionsBody));

        var captionsPage = new TabPage("Live Captions")
        {
            BackColor = UiTheme.AppBackground,
            Padding = new Padding(10)
        };
        captionsPage.Controls.Add(captionsCard);

        var historyPage = new TabPage("Meeting History")
        {
            BackColor = UiTheme.AppBackground,
            Padding = new Padding(10)
        };
        _historyHostPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        _historyHostPanel.Controls.Add(CreateEmptyStateCard("Meeting history opens here when you switch to that workspace."));
        historyPage.Controls.Add(_historyHostPanel);

        var actionsPage = new TabPage("Action Center")
        {
            BackColor = UiTheme.AppBackground,
            Padding = new Padding(10)
        };
        _actionsHostPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        _actionsHostPanel.Controls.Add(CreateEmptyStateCard("Action items and follow-up draft appear here when you open that workspace."));
        actionsPage.Controls.Add(_actionsHostPanel);

        _workspaceTabs = new UiTabControl
        {
            Dock = DockStyle.Fill,
            ItemSize = new Size(Math.Max(128, _settings.Dashboard.WorkspaceTabWidth), 40)
        };
        _workspaceTabs.TabPages.Add(captionsPage);
        _workspaceTabs.TabPages.Add(historyPage);
        _workspaceTabs.TabPages.Add(actionsPage);
        _workspaceTabs.SelectedIndexChanged += OnWorkspaceTabChanged;

        var workspaceBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 6, 0, 0)
        };
        workspaceBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        workspaceBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspaceBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        workspaceBody.Controls.Add(_workspaceSummaryLabel, 0, 0);
        workspaceBody.Controls.Add(_workspaceTabs, 0, 1);

        var workspaceCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            Padding = new Padding(16)
        };
        workspaceCard.Controls.Add(CreateCardContentLayout(
            UiTheme.CreateSectionTitle("Workspace", "Keep captions, history, and follow-up in one place."),
            workspaceBody));

        _footerLabel = CreateMutedLabel(string.Empty);
        _footerLabel.Dock = DockStyle.Bottom;
        _footerLabel.Height = 26;

        var workspaceHostPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        workspaceHostPanel.Controls.Add(workspaceCard);

        _topSectionSplitter = CreateDashboardSplitter(DockStyle.Top);
        _topSectionSplitter.SplitterMoved += (_, _) =>
        {
            _savedTopSectionHeight = _topSectionHostPanel.Height;
            RefreshFooterHint();
        };

        var chromePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.AppBackground,
            Padding = new Padding(14)
        };
        chromePanel.Controls.Add(workspaceHostPanel);
        chromePanel.Controls.Add(_footerLabel);
        chromePanel.Controls.Add(_topSectionSplitter);
        chromePanel.Controls.Add(_topSectionHostPanel);
        chromePanel.Controls.Add(topBar);
        Controls.Add(chromePanel);

        _pickButton.Click += (_, _) => PickRequested?.Invoke(this, EventArgs.Empty);
        _pickScreenButton.Click += (_, _) => PickScreenRequested?.Invoke(this, EventArgs.Empty);
        _startButton.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        _pauseButton.Click += (_, _) => PauseRequested?.Invoke(this, EventArgs.Empty);
        _resumeButton.Click += (_, _) => ResumeRequested?.Invoke(this, EventArgs.Empty);
        _stopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        Shown += (_, _) => BeginInvoke((Action)(() =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Activate();
            ApplyResponsiveLayout();
            ApplySavedLayout();
        }));
        FormClosing += OnFormClosing;
        Resize += (_, _) =>
        {
            ApplyResponsiveLayout();
            ApplySavedLayout();
        };

        ApplySelectedSection(WidgetSection.LiveCaptions, fireEvents: false);
        ApplyResponsiveLayout();
        RenderState();
    }

    public event EventHandler? PickRequested;
    public event EventHandler? PickScreenRequested;
    public event EventHandler? StartRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? ResumeRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? CaptionsRequested;
    public event EventHandler? HistoryRequested;
    public event EventHandler? ActionsRequested;

    public VideoCaptureSource SelectedCaptureSource
        => _captureModeCombo.SelectedIndex == 1 ? VideoCaptureSource.Window : VideoCaptureSource.Display;

    public string SelectedRecordingMode
        => _recordingModeCombo.SelectedIndex == 1 ? "audio-only" : "screen-and-audio";

    public void SetSelectedDisplay(DisplayCaptureTarget? display)
    {
        _selectedDisplayLabel = display?.Description ?? "Display will follow the meeting window";
        RenderState();
    }

    public void UpdateState(MeetingCandidate? candidate, bool isRecording, bool isPaused, string? activeSessionTitle = null)
    {
        _lastCandidate = candidate;
        _lastIsRecording = isRecording;
        _lastIsPaused = isPaused;
        _lastActiveSessionTitle = activeSessionTitle;
        RenderState();
    }

    public void UpdateCaptions(string status, string text)
    {
        _captionsStatusLabel.Text = string.IsNullOrWhiteSpace(status) ? "Live captions" : status;

        var metaText = "Participant hints and caption diagnostics will appear here when available.";
        var bodyText = text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var normalized = text.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            if (lines.Length > 0 && lines[0].StartsWith("Participants detected:", StringComparison.OrdinalIgnoreCase))
            {
                metaText = lines[0].Trim();
                bodyText = string.Join(Environment.NewLine, lines.Skip(1)).Trim();
            }
        }

        _captionsMetaLabel.Text = metaText;
        _captionsBox.Text = string.IsNullOrWhiteSpace(bodyText)
            ? "No caption content has arrived yet."
            : bodyText;
        _captionsBox.SelectionStart = _captionsBox.TextLength;
        _captionsBox.ScrollToCaret();
    }

    public void RefreshSessions()
    {
    }

    public void AttachHistoryForm(HistoryForm historyForm)
    {
        HostChildForm(_historyHostPanel, historyForm);
    }

    public void AttachActionItemsForm(ActionItemsForm actionItemsForm)
    {
        HostChildForm(_actionsHostPanel, actionItemsForm);
    }

    public void SelectSection(WidgetSection section)
    {
        _suppressTabEvents = true;
        _workspaceTabs.SelectedIndex = section switch
        {
            WidgetSection.History => 1,
            WidgetSection.ActionCenter => 2,
            _ => 0
        };
        _suppressTabEvents = false;
        ApplySelectedSection(section, fireEvents: false);

        if (IsHandleCreated)
        {
            if (!Visible)
            {
                Show();
            }

            BringToFront();
            Activate();
        }
    }

    public void PrepareForExit()
    {
        _allowClose = true;
    }

    private void OnWorkspaceTabChanged(object? sender, EventArgs e)
    {
        if (_suppressTabEvents)
        {
            return;
        }

        var section = _workspaceTabs.SelectedIndex switch
        {
            1 => WidgetSection.History,
            2 => WidgetSection.ActionCenter,
            _ => WidgetSection.LiveCaptions
        };

        ApplySelectedSection(section, fireEvents: true);
    }

    private void ApplySelectedSection(WidgetSection section, bool fireEvents)
    {
        _selectedSection = section;
        _workspaceSummaryLabel.Text = section switch
        {
            WidgetSection.History => "Search saved sessions, inspect transcript evidence, and ask grounded follow-up questions.",
            WidgetSection.ActionCenter => "Review extracted tasks, follow-up text, and session outputs without leaving the dashboard.",
            _ => "Keep live captions in view while the rest of the workspace stays one tab away."
        };
        _workspaceSummaryLabel.Visible = _settings.Dashboard.ShowWorkspaceSummary;

        RefreshFooterHint();

        if (!fireEvents)
        {
            return;
        }

        switch (section)
        {
            case WidgetSection.History:
                HistoryRequested?.Invoke(this, EventArgs.Empty);
                break;
            case WidgetSection.ActionCenter:
                ActionsRequested?.Invoke(this, EventArgs.Empty);
                break;
            default:
                CaptionsRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void ResetLayout()
    {
        Size = new Size(
            Math.Max(MinimumSize.Width, _settings.Dashboard.WindowWidth),
            Math.Max(MinimumSize.Height, _settings.Dashboard.WindowHeight));
        StartPosition = FormStartPosition.CenterScreen;
        _savedStatusPanelWidth = Math.Max(280, _settings.Dashboard.DefaultStatusPanelWidth);
        _savedTopSectionHeight = Math.Max(240, _settings.Dashboard.DefaultTopSectionHeight);
        ApplyResponsiveLayout();
        ApplySavedLayout();
    }

    private void RenderState()
    {
        var recordingModeLabel = _recordingModeCombo.SelectedIndex == 1 ? "Audio only" : "Screen + audio";
        var captureModeLabel = SelectedCaptureSource == VideoCaptureSource.Display
            ? $"Full screen on {_selectedDisplayLabel}"
            : "Meeting window only";

        var meetingLabel = _lastActiveSessionTitle
            ?? _lastCandidate?.Title
            ?? "No meeting is being tracked yet";

        string stateTitle;
        string nextStep;
        string narrative;
        string stateHint;

        if (_lastIsPaused)
        {
            stateTitle = "Recording paused";
            nextStep = "Resume to continue the session or Stop to finalize it";
            stateHint = "Resume keeps using the same meeting folder and caption stream.";
            narrative = _lastActiveSessionTitle is null
                ? "The session is paused. Resume when you want live captions and capture to continue."
                : $"The session for {_lastActiveSessionTitle} is paused. Resume to continue recording into the same folder, or stop it to start processing.";
            _statusBadge.Text = "Paused";
            _statusBadge.FillColor = UiTheme.WarningSoft;
            _statusBadge.BorderColor = UiTheme.WarningSoft;
            _statusBadge.TextColor = UiTheme.Warning;
            _statusBadge.GlowColor = UiTheme.WarningGlow;
            _statusBadge.ShowGlow = true;
        }
        else if (_lastIsRecording)
        {
            stateTitle = "Recording in progress";
            nextStep = "Pause if you need a break or Stop when the meeting ends";
            stateHint = "History and Action Center update after the session is finalized.";
            narrative = _lastActiveSessionTitle is null
                ? "Recording is active. Live captions and post-processing continue updating through this dashboard."
                : $"Recording is active for {_lastActiveSessionTitle}. Use the tabs for captions now, then review history or actions after the meeting completes.";
            _statusBadge.Text = "Recording";
            _statusBadge.FillColor = UiTheme.SuccessSoft;
            _statusBadge.BorderColor = UiTheme.SuccessSoft;
            _statusBadge.TextColor = UiTheme.Success;
            _statusBadge.GlowColor = UiTheme.SuccessGlow;
            _statusBadge.ShowGlow = true;
        }
        else if (_lastCandidate is not null)
        {
            stateTitle = "Meeting detected";
            nextStep = "Press Start or adjust the capture source first";
            stateHint = "The meeting target is stable, so recording can begin immediately.";
            narrative = $"Detected {_lastCandidate.Title} from {_lastCandidate.Platform}. You can start immediately or switch between full-screen and meeting-window capture first.";
            _statusBadge.Text = "Detected";
            _statusBadge.FillColor = UiTheme.AccentSoft;
            _statusBadge.BorderColor = UiTheme.AccentSoft;
            _statusBadge.TextColor = UiTheme.AccentStrong;
            _statusBadge.GlowColor = UiTheme.AccentGlow;
            _statusBadge.ShowGlow = true;
        }
        else
        {
            stateTitle = "Waiting for a meeting";
            nextStep = SelectedCaptureSource == VideoCaptureSource.Window && _recordingModeCombo.SelectedIndex == 0
                ? "Pick a window for targeted capture or switch to Full screen"
                : "Start now for manual capture or bring a meeting window forward";
            stateHint = SelectedCaptureSource == VideoCaptureSource.Window && _recordingModeCombo.SelectedIndex == 0
                ? "Detection is optional now, but a specific window target is still needed for window-only capture."
                : "Detection improves naming and context, but manual recording is available anytime.";
            narrative = SelectedCaptureSource == VideoCaptureSource.Window && _recordingModeCombo.SelectedIndex == 0
                ? "No meeting is detected right now. Use Pick window if you want to capture a single app window, or switch to Full screen to begin a manual recording immediately."
                : "No meeting is detected right now. You can still start a manual recording immediately, or bring a Google Meet, Zoom, Teams, or Webex window to the front for richer session context.";
            _statusBadge.Text = "Waiting";
            _statusBadge.FillColor = UiTheme.SurfaceTint;
            _statusBadge.BorderColor = UiTheme.SurfaceTint;
            _statusBadge.TextColor = UiTheme.TextMuted;
            _statusBadge.ShowGlow = false;
        }

        if (_recordingModeCombo.SelectedIndex == 1)
        {
            captureModeLabel = "Audio only - screen capture is skipped";
        }

        _stateTitleLabel.Text = stateTitle;
        _stateHintLabel.Text = stateHint;
        _meetingValueLabel.Text = meetingLabel;
        _modeValueLabel.Text = recordingModeLabel;
        _captureValueLabel.Text = captureModeLabel;
        _nextStepValueLabel.Text = nextStep;

        var statusLines = new List<string>
        {
            narrative,
            string.Empty,
            $"Mode: {recordingModeLabel}",
            $"Capture: {captureModeLabel}"
        };

        if (_lastCandidate is not null)
        {
            statusLines.Add($"Source: {_lastCandidate.Platform} via {_lastCandidate.ProcessName}");
        }

        if (_lastActiveSessionTitle is not null)
        {
            statusLines.Add($"Active session: {_lastActiveSessionTitle}");
        }

        _statusBox.Text = string.Join(Environment.NewLine, statusLines);

        var canStart = !_lastIsRecording && !_lastIsPaused;
        var canPause = _lastIsRecording && !_lastIsPaused;
        var canResume = _lastIsPaused;
        var canStop = _lastIsRecording || _lastIsPaused;
        var controlsLocked = _lastIsRecording || _lastIsPaused;

        _startButton.Enabled = canStart;
        _pauseButton.Enabled = canPause;
        _resumeButton.Enabled = canResume;
        _stopButton.Enabled = canStop;
        _recordingModeCombo.Enabled = !controlsLocked;
        _captureModeCombo.Enabled = !controlsLocked && _recordingModeCombo.SelectedIndex == 0;
        _pickScreenButton.Enabled = !controlsLocked && _captureModeCombo.SelectedIndex == 0 && _recordingModeCombo.SelectedIndex == 0;
        _pickButton.Enabled = !controlsLocked;
        _startAvailabilityLabel.Text = canStart
            ? "Ready to record. Start now or change the target first."
            : "Start is unavailable while another recording is active.";
        ApplyStatusValueWidths();
    }

    private Control CreateTopBar()
    {
        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(UiTheme.SectionFont.FontFamily, 15F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = UiTheme.TextPrimary,
            Text = "Meeting Recorder"
        };

        _topBarSubtitleLabel.Text = "Capture only from a detected meeting, then move straight into captions, history, and follow-up.";
        _topBarSubtitleLabel.Visible = _settings.Dashboard.ShowTopBarSubtitle;

        var titleStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        titleStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        titleStack.Controls.Add(titleLabel, 0, 0);
        titleStack.Controls.Add(_topBarSubtitleLabel, 0, 1);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(2, 0, 2, 10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(titleStack, 0, 0);
        layout.Controls.Add(_topBarActions, 1, 0);
        return layout;
    }

    private void ApplyResponsiveLayout()
    {
        _layoutMode = DetermineLayoutMode();
        ConfigureTopSection();
        ConfigureSourceGrid();
        ConfigureTabSizing();
        ApplyStatusValueWidths();
        RefreshFooterHint();
    }

    private DashboardLayoutMode DetermineLayoutMode()
    {
        var width = Math.Max(0, ClientSize.Width);
        if (width < _settings.Dashboard.StackedBreakpointWidth)
        {
            return DashboardLayoutMode.Stacked;
        }

        if (width < _settings.Dashboard.WideBreakpointWidth)
        {
            return DashboardLayoutMode.Medium;
        }

        return DashboardLayoutMode.Wide;
    }

    private void ConfigureTopSection()
    {
        var parent = _topSectionHostPanel.Parent;
        if (parent is null)
        {
            return;
        }

        if (_layoutMode == DashboardLayoutMode.Stacked)
        {
            _topColumnsSplitter.Visible = false;
            _topColumnsSplitter.Enabled = false;
            _statusHostPanel.Dock = DockStyle.Top;
            _statusHostPanel.Width = 0;
            _statusHostPanel.Height = Math.Max(_settings.Dashboard.StackedStatusPanelHeight, _settings.Dashboard.StatusDetailsHeight + 130);
            _statusHostPanel.MinimumSize = new Size(0, _settings.Dashboard.StackedStatusPanelHeight);
        }
        else
        {
            _topColumnsSplitter.Visible = true;
            _topColumnsSplitter.Enabled = true;
            _topColumnsSplitter.MinExtra = Math.Max(320, _settings.Dashboard.MinimumControlPanelWidth);
            _statusHostPanel.Dock = DockStyle.Left;
            _statusHostPanel.Height = 0;
            _statusHostPanel.MinimumSize = new Size(280, 0);

            var preferredWidth = _layoutMode == DashboardLayoutMode.Wide
                ? _settings.Dashboard.DefaultStatusPanelWidth
                : _settings.Dashboard.CompactStatusPanelWidth;
            var maxWidth = Math.Max(280, parent.ClientSize.Width - _topColumnsSplitter.Width - Math.Max(320, _settings.Dashboard.MinimumControlPanelWidth));
            var desiredWidth = _layoutMode == DashboardLayoutMode.Wide
                ? Math.Max(preferredWidth, _savedStatusPanelWidth)
                : Math.Min(Math.Max(280, _savedStatusPanelWidth), preferredWidth);
            _statusHostPanel.Width = Math.Clamp(desiredWidth, 280, maxWidth);
            _savedStatusPanelWidth = _statusHostPanel.Width;
        }
    }

    private void ConfigureSourceGrid()
    {
        _sourceGrid.SuspendLayout();
        _sourceGrid.Controls.Clear();
        _sourceGrid.ColumnStyles.Clear();
        _sourceGrid.RowStyles.Clear();

        var recordField = CreateFieldGroup("Record mode", _recordingModeCombo);
        var captureField = CreateFieldGroup("Capture source", _captureModeCombo);

        if (_layoutMode == DashboardLayoutMode.Stacked)
        {
            _sourceGrid.ColumnCount = 1;
            _sourceGrid.RowCount = 3;
            _sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _sourceGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _sourceGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _sourceGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _sourceGrid.Controls.Add(recordField, 0, 0);
            _sourceGrid.Controls.Add(captureField, 0, 1);
            _sourceGrid.Controls.Add(_pickerRow, 0, 2);
        }
        else
        {
            _sourceGrid.ColumnCount = 2;
            _sourceGrid.RowCount = 2;
            _sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _sourceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _sourceGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _sourceGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _sourceGrid.Controls.Add(recordField, 0, 0);
            _sourceGrid.Controls.Add(captureField, 1, 0);
            _sourceGrid.Controls.Add(_pickerRow, 0, 1);
            _sourceGrid.SetColumnSpan(_pickerRow, 2);
        }

        _sourceGrid.ResumeLayout();
    }

    private void ConfigureTabSizing()
    {
        var width = Math.Max(120, _workspaceTabs.ClientSize.Width);
        var padding = 36;
        var targetWidth = Math.Min(
            Math.Max(120, _settings.Dashboard.WorkspaceTabWidth),
            Math.Max(110, (width - padding) / Math.Max(1, _workspaceTabs.TabCount)));
        _workspaceTabs.ItemSize = new Size(targetWidth, 40);
    }

    private void ApplySavedLayout()
    {
        ApplyHorizontalSplit();
        RefreshFooterHint();
    }

    private void ApplyHorizontalSplit()
    {
        if (_topSectionHostPanel.Parent is null)
        {
            return;
        }

        var parentHeight = _topSectionHostPanel.Parent.ClientSize.Height;
        var reservedHeight = (_settings.Dashboard.ShowFooterHint ? _footerLabel.Height : 0) + _topSectionSplitter.Height + 96;
        var minHeight = _layoutMode == DashboardLayoutMode.Stacked
            ? Math.Max(460, _settings.Dashboard.StackedStatusPanelHeight + 220)
            : Math.Max(260, _settings.Dashboard.DefaultTopSectionHeight - 20);
        var maxHeight = Math.Max(minHeight, parentHeight - reservedHeight);
        var preferredHeight = _layoutMode == DashboardLayoutMode.Stacked
            ? Math.Max(_savedTopSectionHeight, minHeight)
            : _savedTopSectionHeight;
        _topSectionHostPanel.Height = Math.Clamp(preferredHeight, minHeight, maxHeight);
        _savedTopSectionHeight = _topSectionHostPanel.Height;
    }

    private void ApplyStatusValueWidths()
    {
        var statusWidth = _layoutMode == DashboardLayoutMode.Stacked
            ? _topSectionHostPanel.ClientSize.Width
            : _statusHostPanel.ClientSize.Width;
        var valueWidth = Math.Max(160, statusWidth - 150);
        _meetingValueLabel.MaximumSize = new Size(valueWidth, 0);
        _modeValueLabel.MaximumSize = new Size(valueWidth, 0);
        _captureValueLabel.MaximumSize = new Size(valueWidth, 0);
        _nextStepValueLabel.MaximumSize = new Size(valueWidth, 0);
        _stateHintLabel.MaximumSize = new Size(Math.Max(220, statusWidth - 36), 0);
        _topBarSubtitleLabel.MaximumSize = new Size(Math.Max(240, ClientSize.Width - 420), 0);
        _workspaceSummaryLabel.MaximumSize = new Size(Math.Max(280, _workspaceTabs.ClientSize.Width - 20), 0);
    }

    private void RefreshFooterHint()
    {
        _footerLabel.Visible = _settings.Dashboard.ShowFooterHint;
        if (!_settings.Dashboard.ShowFooterHint)
        {
            return;
        }

        var layoutText = _layoutMode switch
        {
            DashboardLayoutMode.Stacked => "Stacked layout",
            DashboardLayoutMode.Medium => "Compact split layout",
            _ => "Wide split layout"
        };
        _footerLabel.Text = $"{layoutText}. Drag the visible split bars to tune the workspace.";
    }

    private static TableLayoutPanel CreateCardContentLayout(Control header, Control body)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(body, 0, 1);
        return layout;
    }

    private static TableLayoutPanel CreateVerticalStack(params Control[] controls)
    {
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = controls.Length,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var index = 0; index < controls.Length; index++)
        {
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(controls[index], 0, index);
        }

        return stack;
    }

    private static Panel CreateFieldGroup(string labelText, Control control)
    {
        var label = CreateMinorSectionLabel(labelText);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 12, 8),
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Dock = DockStyle.Top;
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(control, 0, 1);

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        host.Controls.Add(layout);
        return host;
    }

    private static FlowLayoutPanel CreateInlineButtonRow()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 10),
            Padding = Padding.Empty
        };
    }

    private static UiButton CreateButton(string text, UiButtonTone tone, int width, int height)
    {
        return new UiButton
        {
            Text = text,
            Tone = tone,
            Height = height,
            Width = width,
            Margin = new Padding(0, 0, 10, 8)
        };
    }

    private static Label CreateStatusKeyLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFontSemibold,
            ForeColor = UiTheme.TextMuted,
            Margin = new Padding(0, 2, 12, 0),
            Text = text
        };
    }

    private static Label CreateStatusValueLabel()
    {
        return new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFont,
            ForeColor = UiTheme.TextPrimary,
            Margin = Padding.Empty
        };
    }

    private static Label CreateMutedLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFont,
            ForeColor = UiTheme.TextMuted,
            Margin = Padding.Empty,
            Text = text
        };
    }

    private static Label CreateMinorSectionLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFontSemibold,
            ForeColor = UiTheme.TextMuted,
            Margin = new Padding(0, 0, 0, 6),
            Text = text
        };
    }

    private static Splitter CreateDashboardSplitter(DockStyle dock)
    {
        return new Splitter
        {
            Dock = dock,
            BackColor = UiTheme.Border,
            Width = dock == DockStyle.Left ? 8 : 0,
            Height = dock == DockStyle.Top ? 8 : 0,
            MinExtra = dock == DockStyle.Left ? 420 : 300,
            MinSize = dock == DockStyle.Left ? 280 : 240,
            Cursor = dock == DockStyle.Left ? Cursors.VSplit : Cursors.HSplit,
            TabStop = false
        };
    }

    private static UiCardPanel CreateEmptyStateCard(string text)
    {
        var messageLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = UiTheme.BodyFont,
            ForeColor = UiTheme.TextMuted,
            Text = text
        };

        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            Padding = new Padding(16)
        };
        card.Controls.Add(messageLabel);
        return card;
    }

    private static void HostChildForm(Control host, Form child)
    {
        host.Controls.Clear();
        child.TopLevel = false;
        child.FormBorderStyle = FormBorderStyle.None;
        child.Dock = DockStyle.Fill;
        child.Visible = true;
        host.Controls.Add(child);
        child.Show();
    }

    private void OpenMeetingsFolder()
    {
        try
        {
            var path = Shared.Configuration.RecorderSettingsProvider.ResolveMeetingsRoot();
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception error)
        {
            MessageBox.Show(
                $"Unable to open the meetings folder.{Environment.NewLine}{Environment.NewLine}{error.Message}",
                "Meeting Recorder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose)
        {
            return;
        }

        eventArgs.Cancel = true;
        Hide();
    }
}
