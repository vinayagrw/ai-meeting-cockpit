using System.Diagnostics;
using MeetingRecorder.Shared.Configuration;
using MeetingRecorder.Windows.Models;

namespace MeetingRecorder.Windows.Shell;

public sealed partial class MeetingDashboardModernForm : Form
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
    private readonly UiButton _captionsNavButton;
    private readonly UiButton _historyNavButton;
    private readonly UiButton _actionsNavButton;
    private readonly UiButton _toggleDiagnosticsButton;
    private readonly UiButton _moreButton;
    private readonly UiBadgeLabel _statusBadge;
    private readonly Label _topBarTitleLabel;
    private readonly Label _topBarSubtitleLabel;
    private readonly Label _topBarMeetingLabel;
    private readonly Label _topBarStatusLabel;
    private readonly Label _summaryStateLabel;
    private readonly Label _summaryHintLabel;
    private readonly Label _meetingValueLabel;
    private readonly Label _modeValueLabel;
    private readonly Label _captureValueLabel;
    private readonly Label _nextStepValueLabel;
    private readonly TextBox _diagnosticsBox;
    private readonly Panel _diagnosticsHostPanel;
    private readonly Label _commandHintLabel;
    private readonly Label _workspaceTitleLabel;
    private readonly Label _workspaceSubtitleLabel;
    private readonly Label _captionsStatusLabel;
    private readonly Label _captionsMetaLabel;
    private readonly TextBox _captionsBox;
    private readonly Panel _historyHostPanel;
    private readonly Panel _actionsHostPanel;
    private readonly Panel _captionsHostPanel;
    private readonly Panel _workspaceHostPanel;
    private readonly Panel _topSectionHostPanel;
    private readonly Panel _summaryHostPanel;
    private readonly Panel _commandHostPanel;
    private readonly Splitter _topSectionSplitter;
    private readonly Splitter _workspaceSplitter;
    private readonly ContextMenuStrip _overflowMenu;

    private int _savedStatusPanelWidth;
    private int _savedTopSectionHeight;
    private int _savedSummaryHeight;
    private bool _allowClose;
    private bool _diagnosticsExpanded;
    private string _selectedDisplayLabel = "Display will follow the active screen";
    private MeetingCandidate? _lastCandidate;
    private bool _lastIsPaused;
    private bool _lastIsRecording;
    private string? _lastActiveSessionTitle;
    private WidgetSection _selectedSection = WidgetSection.LiveCaptions;
    private DashboardLayoutMode _layoutMode;

    public MeetingDashboardModernForm(RecorderWindowsSettings settings)
    {
        _settings = settings;
        _savedStatusPanelWidth = Math.Max(300, _settings.Dashboard.DefaultStatusPanelWidth);
        _savedTopSectionHeight = Math.Max(220, _settings.Dashboard.DefaultTopSectionHeight);
        _savedSummaryHeight = Math.Max(160, _settings.Dashboard.StackedStatusPanelHeight);
        _diagnosticsExpanded = !_settings.Dashboard.DiagnosticsCollapsedByDefault;

        UiTheme.ApplyForm(this);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(
            Math.Max(1024, _settings.Dashboard.MinWindowWidth),
            Math.Max(680, _settings.Dashboard.MinWindowHeight));
        StartPosition = FormStartPosition.CenterScreen;
        Width = Math.Max(MinimumSize.Width, _settings.Dashboard.WindowWidth);
        Height = Math.Max(MinimumSize.Height, _settings.Dashboard.WindowHeight);
        Text = "Meeting Recorder";
        Icon = AppIconProvider.Icon;

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

        _pickScreenButton = CreateButton("Pick screen", UiButtonTone.Secondary, Math.Max(100, _settings.Dashboard.PickerButtonWidth), _settings.Dashboard.ControlButtonHeight);
        _pickButton = CreateButton("Pick window", UiButtonTone.Neutral, Math.Max(104, _settings.Dashboard.PickerButtonWidth), _settings.Dashboard.ControlButtonHeight);
        _startButton = CreateButton("Start", UiButtonTone.Primary, Math.Max(88, _settings.Dashboard.PrimaryActionButtonWidth), _settings.Dashboard.ControlButtonHeight);
        _pauseButton = CreateButton("Pause", UiButtonTone.Neutral, Math.Max(88, _settings.Dashboard.PrimaryActionButtonWidth), _settings.Dashboard.ControlButtonHeight);
        _resumeButton = CreateButton("Resume", UiButtonTone.Secondary, Math.Max(88, _settings.Dashboard.PrimaryActionButtonWidth), _settings.Dashboard.ControlButtonHeight);
        _stopButton = CreateButton("Stop", UiButtonTone.Danger, Math.Max(88, _settings.Dashboard.PrimaryActionButtonWidth), _settings.Dashboard.ControlButtonHeight);
        _captionsNavButton = CreateNavButton("Live Captions");
        _historyNavButton = CreateNavButton("Meeting History");
        _actionsNavButton = CreateNavButton("Action Center");
        _toggleDiagnosticsButton = CreateButton(_diagnosticsExpanded ? "Hide diagnostics" : "Show diagnostics", UiButtonTone.Ghost, _settings.Dashboard.DiagnosticsToggleButtonWidth, 32);
        _moreButton = CreateButton("More", UiButtonTone.Ghost, _settings.Dashboard.OverflowButtonWidth, 34);

        _pickScreenButton.Click += (_, _) => PickScreenRequested?.Invoke(this, EventArgs.Empty);
        _pickButton.Click += (_, _) => PickRequested?.Invoke(this, EventArgs.Empty);
        _startButton.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        _pauseButton.Click += (_, _) => PauseRequested?.Invoke(this, EventArgs.Empty);
        _resumeButton.Click += (_, _) => ResumeRequested?.Invoke(this, EventArgs.Empty);
        _stopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        _captionsNavButton.Click += (_, _) => ApplySelectedSection(WidgetSection.LiveCaptions, fireEvents: true);
        _historyNavButton.Click += (_, _) => ApplySelectedSection(WidgetSection.History, fireEvents: true);
        _actionsNavButton.Click += (_, _) => ApplySelectedSection(WidgetSection.ActionCenter, fireEvents: true);
        _toggleDiagnosticsButton.Click += (_, _) =>
        {
            _diagnosticsExpanded = !_diagnosticsExpanded;
            ApplyDiagnosticsState();
        };

        _overflowMenu = new ContextMenuStrip();
        _overflowMenu.Items.Add("Meetings folder", null, (_, _) => OpenMeetingsFolder());
        _overflowMenu.Items.Add("Reset layout", null, (_, _) => ResetLayout());
        _overflowMenu.Items.Add("Hide app", null, (_, _) => Hide());
        _moreButton.Click += (_, _) => _overflowMenu.Show(_moreButton, new Point(0, _moreButton.Height));

        _statusBadge = new UiBadgeLabel
        {
            Text = "Waiting",
            Width = 112,
            Height = 32,
            FillColor = UiTheme.SurfaceTint,
            BorderColor = UiTheme.SurfaceTint,
            TextColor = UiTheme.TextMuted,
            Margin = new Padding(0, 0, 10, 0)
        };

        _topBarTitleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(UiTheme.SectionFont.FontFamily, 16F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = UiTheme.TextPrimary,
            Margin = Padding.Empty,
            Text = "Meeting Recorder"
        };
        _topBarSubtitleLabel = new Label
        {
            AutoSize = _settings.Dashboard.ShowTopBarSubtitle,
            Font = UiTheme.BodyFont,
            ForeColor = UiTheme.TextMuted,
            Margin = new Padding(0, 4, 0, 0),
            Text = "Record, caption, search, and follow up from one place.",
            Visible = _settings.Dashboard.ShowTopBarSubtitle
        };
        _topBarMeetingLabel = new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFontSemibold,
            ForeColor = UiTheme.TextPrimary,
            Margin = Padding.Empty,
            Text = "No active meeting target"
        };
        _topBarStatusLabel = new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFont,
            ForeColor = UiTheme.TextMuted,
            Margin = new Padding(0, 4, 0, 0),
            Text = "Manual recording is available."
        };

        _summaryStateLabel = new Label
        {
            AutoSize = true,
            Font = new Font(UiTheme.SectionFont.FontFamily, 15F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = UiTheme.TextPrimary,
            Margin = Padding.Empty,
            Text = "Waiting for a meeting"
        };
        _summaryHintLabel = CreateMutedLabel("Start manually or bring a meeting window forward.");
        _meetingValueLabel = CreateValueLabel();
        _modeValueLabel = CreateValueLabel();
        _captureValueLabel = CreateValueLabel();
        _nextStepValueLabel = CreateValueLabel();

        _diagnosticsBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true
        };
        UiTheme.StyleTextBox(_diagnosticsBox, readOnly: true);

        _diagnosticsHostPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };
        _diagnosticsHostPanel.Controls.Add(_diagnosticsBox);

        _commandHintLabel = CreateMutedLabel("Pick a window only when you want targeted window capture. Full screen and audio-only can start manually.");
        _workspaceTitleLabel = new Label
        {
            AutoSize = true,
            Font = UiTheme.SectionFont,
            ForeColor = UiTheme.TextPrimary,
            Margin = Padding.Empty,
            Text = "Live Captions"
        };
        _workspaceSubtitleLabel = CreateMutedLabel("Keep the latest speech visible while the recorder runs.");

        _captionsStatusLabel = new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFontSemibold,
            ForeColor = UiTheme.TextPrimary,
            Margin = Padding.Empty,
            Text = "Waiting"
        };
        _captionsMetaLabel = CreateMutedLabel("Participant hints and caption source details will appear here.");
        _captionsBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Text = "Start a recording to stream live captions here."
        };
        UiTheme.StyleTextBox(_captionsBox, readOnly: true);

        _historyHostPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
        _actionsHostPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
        _captionsHostPanel = BuildCaptionsWorkspace();
        _workspaceHostPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        _workspaceHostPanel.Controls.Add(_actionsHostPanel);
        _workspaceHostPanel.Controls.Add(_historyHostPanel);
        _workspaceHostPanel.Controls.Add(_captionsHostPanel);

        _summaryHostPanel = new Panel { Dock = DockStyle.Left, Width = _savedStatusPanelWidth, BackColor = Color.Transparent, Padding = Padding.Empty };
        _summaryHostPanel.Controls.Add(BuildSessionSummaryCard());
        _commandHostPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = Padding.Empty };
        _commandHostPanel.Controls.Add(BuildCommandCard());

        _topSectionSplitter = CreateSplitter(DockStyle.Left);
        _topSectionSplitter.SplitterMoved += (_, _) =>
        {
            if (_layoutMode == DashboardLayoutMode.Stacked)
            {
                _savedSummaryHeight = _summaryHostPanel.Height;
            }
            else
            {
                _savedStatusPanelWidth = _summaryHostPanel.Width;
            }
        };

        _topSectionHostPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = _savedTopSectionHeight,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 0, 0, 12)
        };
        _topSectionHostPanel.Controls.Add(_commandHostPanel);
        _topSectionHostPanel.Controls.Add(_topSectionSplitter);
        _topSectionHostPanel.Controls.Add(_summaryHostPanel);

        _workspaceSplitter = CreateSplitter(DockStyle.Top);
        _workspaceSplitter.SplitterMoved += (_, _) => _savedTopSectionHeight = _topSectionHostPanel.Height;

        var workspaceLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        workspaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        workspaceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspaceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        workspaceLayout.Controls.Add(BuildWorkspaceHeader(), 0, 0);
        workspaceLayout.Controls.Add(_workspaceHostPanel, 0, 1);

        var navRail = BuildNavRail();
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 18, 18, 18)
        };
        contentPanel.Controls.Add(workspaceLayout);
        contentPanel.Controls.Add(_workspaceSplitter);
        contentPanel.Controls.Add(_topSectionHostPanel);

        var bodyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _settings.Dashboard.NavRailWidth));
        bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bodyLayout.Controls.Add(navRail, 0, 0);
        bodyLayout.Controls.Add(contentPanel, 1, 0);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.AppBackground,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, _settings.Dashboard.AppBarHeight + 28));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(BuildAppBar(), 0, 0);
        rootLayout.Controls.Add(bodyLayout, 0, 1);
        Controls.Add(rootLayout);

        FormClosing += OnFormClosing;
        Load += (_, _) =>
        {
            ApplyDiagnosticsState();
            ApplyResponsiveLayout();
            ApplySelectedSection(WidgetSection.LiveCaptions, fireEvents: false);
            RenderState();
        };
        Shown += (_, _) =>
        {
            ApplyResponsiveLayout();
            ApplySelectedSection(_selectedSection, fireEvents: false);
        };
        Resize += (_, _) =>
        {
            ApplyResponsiveLayout();
            ApplySummaryLabelWidths();
        };
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
        _selectedDisplayLabel = display?.Description ?? "Display will follow the active screen";
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
        _captionsStatusLabel.Text = string.IsNullOrWhiteSpace(status) ? "Listening" : status;
        var metaText = "Participant hints and caption source details will appear here.";
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
        ApplySelectedSection(section, fireEvents: false);
        if (!Visible)
        {
            Show();
        }

        BringToFront();
        Activate();
    }

    public void PrepareForExit()
    {
        _allowClose = true;
    }

    private Control BuildAppBar()
    {
        var titleStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = _settings.Dashboard.ShowTopBarSubtitle ? 2 : 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        titleStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        titleStack.Controls.Add(_topBarTitleLabel, 0, 0);
        if (_settings.Dashboard.ShowTopBarSubtitle)
        {
            titleStack.Controls.Add(_topBarSubtitleLabel, 0, 1);
        }

        var statusTextStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        statusTextStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusTextStack.Controls.Add(_topBarMeetingLabel, 0, 0);
        statusTextStack.Controls.Add(_topBarStatusLabel, 0, 1);

        var centerStatusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        centerStatusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        centerStatusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        centerStatusLayout.Controls.Add(_statusBadge, 0, 0);
        centerStatusLayout.Controls.Add(statusTextStack, 1, 0);

        var appBarLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(18, 14, 18, 6)
        };
        appBarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
        appBarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46F));
        appBarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        appBarLayout.Controls.Add(titleStack, 0, 0);
        appBarLayout.Controls.Add(centerStatusLayout, 1, 0);
        appBarLayout.Controls.Add(_moreButton, 2, 0);
        return appBarLayout;
    }

    private Control BuildNavRail()
    {
        _captionsNavButton.Height = _settings.Dashboard.NavRailButtonHeight;
        _historyNavButton.Height = _settings.Dashboard.NavRailButtonHeight;
        _actionsNavButton.Height = _settings.Dashboard.NavRailButtonHeight;

        var navStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        navStack.Controls.Add(_captionsNavButton);
        navStack.Controls.Add(_historyNavButton);
        navStack.Controls.Add(_actionsNavButton);

        var railLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(18, 18, 12, 18)
        };
        railLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        railLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        railLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        railLayout.Controls.Add(CreateNavHeader(), 0, 0);
        railLayout.Controls.Add(navStack, 0, 1);
        return railLayout;
    }

    private Control BuildSessionSummaryCard()
    {
        var factsTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 10, 0, 0),
            Padding = Padding.Empty
        };
        factsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
        factsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        factsTable.Controls.Add(CreateKeyLabel("Meeting"), 0, 0);
        factsTable.Controls.Add(_meetingValueLabel, 1, 0);
        factsTable.Controls.Add(CreateKeyLabel("Mode"), 0, 1);
        factsTable.Controls.Add(_modeValueLabel, 1, 1);
        factsTable.Controls.Add(CreateKeyLabel("Capture"), 0, 2);
        factsTable.Controls.Add(_captureValueLabel, 1, 2);
        factsTable.Controls.Add(CreateKeyLabel("Next"), 0, 3);
        factsTable.Controls.Add(_nextStepValueLabel, 1, 3);

        var diagHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 12, 0, 0),
            Padding = Padding.Empty
        };
        diagHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        diagHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        diagHeader.Controls.Add(CreateMinorLabel("Diagnostics"), 0, 0);
        diagHeader.Controls.Add(_toggleDiagnosticsButton, 1, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        body.Controls.Add(CreateStack(_summaryStateLabel, _summaryHintLabel), 0, 0);
        body.Controls.Add(factsTable, 0, 1);
        body.Controls.Add(diagHeader, 0, 2);
        body.Controls.Add(_diagnosticsHostPanel, 0, 3);

        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            Padding = new Padding(16),
            ShowAccent = false
        };
        card.Controls.Add(CreateCardLayout(
            CreateSectionHeader("Session Summary", "Current target, capture path, and next step."),
            body));
        return card;
    }

    private Control BuildCommandCard()
    {
        var fieldsGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 0),
            Padding = Padding.Empty
        };
        fieldsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        fieldsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        fieldsGrid.Controls.Add(CreateFieldGroup("Record Mode", _recordingModeCombo), 0, 0);
        fieldsGrid.Controls.Add(CreateFieldGroup("Capture Source", _captureModeCombo), 1, 0);

        var secondaryActions = CreateButtonRow();
        secondaryActions.Controls.Add(_pickScreenButton);
        secondaryActions.Controls.Add(_pickButton);

        var transportActions = CreateButtonRow();
        transportActions.Controls.Add(_startButton);
        transportActions.Controls.Add(_pauseButton);
        transportActions.Controls.Add(_resumeButton);
        transportActions.Controls.Add(_stopButton);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.Controls.Add(fieldsGrid, 0, 0);
        body.Controls.Add(secondaryActions, 0, 1);
        body.Controls.Add(transportActions, 0, 2);
        body.Controls.Add(_commandHintLabel, 0, 3);

        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Success,
            Padding = new Padding(16),
            ShowAccent = false
        };
        card.Controls.Add(CreateCardLayout(
            CreateSectionHeader("Command Bar", "Primary transport controls first, picker actions second."),
            body));
        return card;
    }

    private Panel BuildCaptionsWorkspace()
    {
        var statusStrip = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        statusStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusStrip.Controls.Add(_captionsStatusLabel, 0, 0);
        statusStrip.Controls.Add(_captionsMetaLabel, 0, 1);

        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            ShowAccent = false,
            Padding = new Padding(16)
        };
        card.Controls.Add(_captionsBox);
        card.Controls.Add(statusStrip);

        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Controls = { card }
        };
    }

    private Control BuildWorkspaceHeader()
    {
        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, 10)
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerLayout.Controls.Add(_workspaceTitleLabel, 0, 0);
        headerLayout.Controls.Add(_workspaceSubtitleLabel, 0, 1);
        return headerLayout;
    }

    private void ApplySelectedSection(WidgetSection section, bool fireEvents)
    {
        _selectedSection = section;
        _captionsHostPanel.Visible = section == WidgetSection.LiveCaptions;
        _historyHostPanel.Visible = section == WidgetSection.History;
        _actionsHostPanel.Visible = section == WidgetSection.ActionCenter;

        _captionsNavButton.Tone = section == WidgetSection.LiveCaptions ? UiButtonTone.Primary : UiButtonTone.Ghost;
        _historyNavButton.Tone = section == WidgetSection.History ? UiButtonTone.Primary : UiButtonTone.Ghost;
        _actionsNavButton.Tone = section == WidgetSection.ActionCenter ? UiButtonTone.Primary : UiButtonTone.Ghost;

        switch (section)
        {
            case WidgetSection.History:
                _workspaceTitleLabel.Text = "Meeting History";
                _workspaceSubtitleLabel.Text = "Search sessions, inspect saved evidence, and ask grounded follow-up questions.";
                break;
            case WidgetSection.ActionCenter:
                _workspaceTitleLabel.Text = "Action Center";
                _workspaceSubtitleLabel.Text = "Track tasks, refresh outputs, and reuse follow-up text without leaving the dashboard.";
                break;
            default:
                _workspaceTitleLabel.Text = "Live Captions";
                _workspaceSubtitleLabel.Text = "Keep the latest speech visible while the recorder runs.";
                break;
        }

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

    private void ApplyDiagnosticsState()
    {
        _diagnosticsHostPanel.Visible = _diagnosticsExpanded;
        _diagnosticsHostPanel.Height = _diagnosticsExpanded
            ? Math.Max(88, _settings.Dashboard.StatusDetailsHeight)
            : 0;
        _toggleDiagnosticsButton.Text = _diagnosticsExpanded ? "Hide diagnostics" : "Show diagnostics";
    }

    private void ApplyResponsiveLayout()
    {
        _layoutMode = DetermineLayoutMode();
        _topSectionHostPanel.SuspendLayout();

        if (_layoutMode == DashboardLayoutMode.Stacked)
        {
            _summaryHostPanel.Dock = DockStyle.Top;
            _summaryHostPanel.Width = 0;
            _summaryHostPanel.Height = Math.Max(160, _savedSummaryHeight);
            _topSectionSplitter.Dock = DockStyle.Top;
            _topSectionSplitter.Width = 0;
            _topSectionSplitter.Height = 8;
            _topSectionSplitter.Cursor = Cursors.HSplit;
        }
        else
        {
            var targetWidth = _layoutMode == DashboardLayoutMode.Wide
                ? _savedStatusPanelWidth
                : Math.Max(_settings.Dashboard.CompactStatusPanelWidth, Math.Min(_savedStatusPanelWidth, 360));
            var maxWidth = Math.Max(320, _topSectionHostPanel.ClientSize.Width - _settings.Dashboard.MinimumControlPanelWidth);

            _summaryHostPanel.Dock = DockStyle.Left;
            _summaryHostPanel.Height = 0;
            _summaryHostPanel.Width = Math.Max(280, Math.Min(targetWidth, maxWidth));
            _topSectionSplitter.Dock = DockStyle.Left;
            _topSectionSplitter.Height = 0;
            _topSectionSplitter.Width = 8;
            _topSectionSplitter.Cursor = Cursors.VSplit;
        }

        _topSectionHostPanel.Height = Math.Max(190, _savedTopSectionHeight);
        _topSectionHostPanel.ResumeLayout();
        ApplySummaryLabelWidths();
    }

    private DashboardLayoutMode DetermineLayoutMode()
    {
        var contentWidth = Math.Max(0, ClientSize.Width - _settings.Dashboard.NavRailWidth - 60);
        if (contentWidth >= _settings.Dashboard.WideBreakpointWidth)
        {
            return DashboardLayoutMode.Wide;
        }

        return contentWidth < _settings.Dashboard.StackedBreakpointWidth
            ? DashboardLayoutMode.Stacked
            : DashboardLayoutMode.Medium;
    }

    private void ApplySummaryLabelWidths()
    {
        var summaryWidth = _layoutMode == DashboardLayoutMode.Stacked
            ? Math.Max(260, _topSectionHostPanel.ClientSize.Width - 36)
            : Math.Max(260, _summaryHostPanel.ClientSize.Width - 120);

        foreach (var label in new[] { _meetingValueLabel, _modeValueLabel, _captureValueLabel, _nextStepValueLabel, _summaryHintLabel })
        {
            label.MaximumSize = new Size(summaryWidth, 0);
        }

        _captionsMetaLabel.MaximumSize = new Size(Math.Max(260, _workspaceHostPanel.ClientSize.Width - 28), 0);
        _topBarMeetingLabel.MaximumSize = new Size(_settings.Dashboard.TopBarStatusMaxWidth, 0);
        _topBarStatusLabel.MaximumSize = new Size(_settings.Dashboard.TopBarStatusMaxWidth, 0);
    }

    private void RenderState()
    {
        var recordingModeLabel = _recordingModeCombo.SelectedIndex == 1 ? "Audio only" : "Screen + audio";
        var captureModeLabel = _recordingModeCombo.SelectedIndex == 1
            ? "Audio only - screen capture is skipped"
            : SelectedCaptureSource == VideoCaptureSource.Display
                ? $"Full screen on {_selectedDisplayLabel}"
                : "Meeting window only";

        var meetingLabel = _lastActiveSessionTitle
            ?? _lastCandidate?.Title
            ?? "No meeting is being tracked yet";

        string stateTitle;
        string nextStep;
        string hint;
        string diagnostics;

        if (_lastIsPaused)
        {
            stateTitle = "Recording paused";
            nextStep = "Resume to continue the session, or stop to finalize it.";
            hint = "The current session folder stays active while paused.";
            diagnostics = _lastActiveSessionTitle is null
                ? "The recorder is paused. Resume to continue capture into the current meeting folder."
                : $"The recorder is paused for {_lastActiveSessionTitle}. Resume to continue recording into the same session.";
            ApplyBadge("Paused", UiTheme.WarningSoft, UiTheme.WarningSoft, UiTheme.Warning);
        }
        else if (_lastIsRecording)
        {
            stateTitle = "Recording now";
            nextStep = "Pause for a break, or stop when the session is complete.";
            hint = "Live captions continue updating while capture is active.";
            diagnostics = _lastActiveSessionTitle is null
                ? "The recorder is active. Use the left rail to inspect captions, history, or action items."
                : $"The recorder is active for {_lastActiveSessionTitle}. The session is writing artifacts now.";
            ApplyBadge("Recording", UiTheme.SuccessSoft, UiTheme.SuccessSoft, UiTheme.Success);
        }
        else if (_lastCandidate is not null)
        {
            stateTitle = "Meeting detected";
            nextStep = "Start now, or change capture source before starting.";
            hint = $"Detected {_lastCandidate.Platform}. You can start immediately.";
            diagnostics = $"Detected {_lastCandidate.Title} from {_lastCandidate.ProcessName} ({_lastCandidate.Platform}).";
            ApplyBadge("Detected", UiTheme.AccentSoft, UiTheme.AccentSoft, UiTheme.AccentStrong);
        }
        else
        {
            stateTitle = "Waiting for a meeting";
            nextStep = SelectedCaptureSource == VideoCaptureSource.Window && _recordingModeCombo.SelectedIndex == 0
                ? "Pick a window for targeted capture, or switch to Full screen."
                : "Start manually now, or bring a meeting window forward.";
            hint = "Manual recording is available even without active meeting detection.";
            diagnostics = SelectedCaptureSource == VideoCaptureSource.Window && _recordingModeCombo.SelectedIndex == 0
                ? "Window-only capture needs a specific target. Use Pick window to bind the recorder to one app window."
                : "Bring a Google Meet, Zoom, Teams, or Webex window to the front if you want richer platform context in the saved session.";
            ApplyBadge("Waiting", UiTheme.SurfaceTint, UiTheme.SurfaceTint, UiTheme.TextMuted);
        }

        _summaryStateLabel.Text = stateTitle;
        _summaryHintLabel.Text = hint;
        _meetingValueLabel.Text = meetingLabel;
        _modeValueLabel.Text = recordingModeLabel;
        _captureValueLabel.Text = captureModeLabel;
        _nextStepValueLabel.Text = nextStep;
        _diagnosticsBox.Text = diagnostics;

        _topBarMeetingLabel.Text = _lastCandidate?.DisplayLabel ?? (_lastActiveSessionTitle ?? "No active meeting target");
        _topBarStatusLabel.Text = _lastIsPaused
            ? "Paused. Resume to continue the active session."
            : _lastIsRecording
                ? "Capture is active. Navigate the workspace with the left rail."
                : nextStep;

        var controlsLocked = _lastIsRecording || _lastIsPaused;
        _startButton.Enabled = !controlsLocked;
        _pauseButton.Enabled = _lastIsRecording && !_lastIsPaused;
        _resumeButton.Enabled = _lastIsPaused;
        _stopButton.Enabled = _lastIsRecording || _lastIsPaused;
        _recordingModeCombo.Enabled = !controlsLocked;
        _captureModeCombo.Enabled = !controlsLocked && _recordingModeCombo.SelectedIndex == 0;
        _pickScreenButton.Enabled = !controlsLocked && _recordingModeCombo.SelectedIndex == 0 && SelectedCaptureSource == VideoCaptureSource.Display;
        _pickButton.Enabled = !controlsLocked;
        _commandHintLabel.Text = _lastIsRecording || _lastIsPaused
            ? "Recording controls are focused on the active session until it is stopped."
            : SelectedCaptureSource == VideoCaptureSource.Window && _recordingModeCombo.SelectedIndex == 0
                ? "Meeting window mode works best after you use Pick window."
                : "Start can run a manual recording immediately. Use Pick window only when you want a specific app target.";

        ApplySummaryLabelWidths();
    }

    private void ApplyBadge(string text, Color fill, Color border, Color fore)
    {
        _statusBadge.Text = text;
        _statusBadge.FillColor = fill;
        _statusBadge.BorderColor = border;
        _statusBadge.TextColor = fore;
        _statusBadge.Invalidate();
    }

    private void ResetLayout()
    {
        _savedStatusPanelWidth = Math.Max(300, _settings.Dashboard.DefaultStatusPanelWidth);
        _savedTopSectionHeight = Math.Max(220, _settings.Dashboard.DefaultTopSectionHeight);
        _savedSummaryHeight = Math.Max(160, _settings.Dashboard.StackedStatusPanelHeight);
        _diagnosticsExpanded = !_settings.Dashboard.DiagnosticsCollapsedByDefault;
        ApplyDiagnosticsState();
        ApplyResponsiveLayout();
    }

    private void OpenMeetingsFolder()
    {
        try
        {
            var path = RecorderSettingsProvider.ResolveMeetingsRoot();
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

    private static UiButton CreateButton(string text, UiButtonTone tone, int width, int height)
    {
        return new UiButton
        {
            Text = text,
            Tone = tone,
            Width = width,
            Height = height,
            Margin = new Padding(0, 0, 8, 8)
        };
    }

    private static UiButton CreateNavButton(string text)
    {
        return new UiButton
        {
            Text = text,
            Tone = UiButtonTone.Ghost,
            Width = 122,
            Height = 42,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 8)
        };
    }

    private static FlowLayoutPanel CreateButtonRow()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 10, 0, 0),
            Padding = Padding.Empty
        };
    }

    private static Control CreateNavHeader()
    {
        var title = new Label
        {
            AutoSize = true,
            Font = UiTheme.EyebrowFont,
            ForeColor = UiTheme.TextMuted,
            Margin = Padding.Empty,
            Text = "WORKSPACE"
        };
        var subtitle = CreateMutedLabel("Switch between captions, history, and action review.");
        return CreateStack(title, subtitle);
    }

    private static Control CreateSectionHeader(string title, string subtitle)
    {
        return CreateStack(
            new Label
            {
                AutoSize = true,
                Font = UiTheme.SectionFont,
                ForeColor = UiTheme.TextPrimary,
                Margin = Padding.Empty,
                Text = title
            },
            CreateMutedLabel(subtitle));
    }

    private static Control CreateFieldGroup(string labelText, Control control)
    {
        var label = CreateMinorLabel(labelText);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 12, 0),
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(control, 0, 1);
        return layout;
    }

    private static TableLayoutPanel CreateCardLayout(Control header, Control body)
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

    private static TableLayoutPanel CreateStack(params Control[] controls)
    {
        var layout = new TableLayoutPanel
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
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var index = 0; index < controls.Length; index++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(controls[index], 0, index);
        }

        return layout;
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

    private static Label CreateKeyLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFontSemibold,
            ForeColor = UiTheme.TextMuted,
            Margin = new Padding(0, 2, 10, 0),
            Text = text
        };
    }

    private static Label CreateValueLabel()
    {
        return new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFont,
            ForeColor = UiTheme.TextPrimary,
            Margin = Padding.Empty
        };
    }

    private static Label CreateMinorLabel(string text)
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

    private static Splitter CreateSplitter(DockStyle dock)
    {
        return new Splitter
        {
            Dock = dock,
            BackColor = UiTheme.Border,
            Width = dock == DockStyle.Left ? 8 : 0,
            Height = dock == DockStyle.Top ? 8 : 0,
            Cursor = dock == DockStyle.Left ? Cursors.VSplit : Cursors.HSplit,
            TabStop = false
        };
    }
}
