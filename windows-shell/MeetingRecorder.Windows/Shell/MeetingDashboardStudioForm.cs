using System.Diagnostics;
using MeetingRecorder.Shared.Configuration;
using MeetingRecorder.Windows.Models;

namespace MeetingRecorder.Windows.Shell;

public sealed class MeetingDashboardStudioForm : Form
{
    private enum DashboardLayoutMode
    {
        Wide,
        Medium,
        Stacked
    }

    private readonly RecorderWindowsSettings _settings;
    private readonly UiComboBox _captureModeCombo;
    private readonly UiComboBox _recordingModeCombo;
    private readonly UiButton _pickScreenButton;
    private readonly UiButton _pickButton;
    private readonly UiButton _startButton;
    private readonly UiButton _pauseButton;
    private readonly UiButton _resumeButton;
    private readonly UiButton _stopButton;
    private readonly UiNavItem _captionsNavButton;
    private readonly UiNavItem _historyNavButton;
    private readonly UiNavItem _actionsNavButton;
    private readonly UiButton _toggleDiagnosticsButton;
    private readonly UiButton _moreButton;
    private readonly UiButton _popoutCaptionsButton;
    private readonly UiBadgeLabel _statusBadge;
    private readonly Label _appTitleLabel;
    private readonly Label _appSubtitleLabel;
    private readonly Label _meetingHeadlineLabel;
    private readonly Label _meetingStatusLabel;
    private readonly Label _summaryStateLabel;
    private readonly Label _summaryHintLabel;
    private readonly Label _meetingValueLabel;
    private readonly Label _modeValueLabel;
    private readonly Label _captureValueLabel;
    private readonly Label _nextStepValueLabel;
    private readonly Label _commandHintLabel;
    private readonly Label _workspaceTitleLabel;
    private readonly Label _workspaceSubtitleLabel;
    private readonly Label _captionsStatusLabel;
    private readonly Label _captionsMetaLabel;
    private readonly TextBox _captionsBox;
    private readonly TextBox _diagnosticsBox;
    private readonly UiTextBoxWrapper _captionsBoxWrapper;
    private readonly UiTextBoxWrapper _diagnosticsBoxWrapper;
    private readonly Panel _historyHostPanel;
    private readonly Panel _actionsHostPanel;
    private readonly Panel _captionsHostPanel;
    private readonly Panel _workspaceHostPanel;
    private readonly Panel _diagnosticsCardHostPanel;
    private readonly SplitContainer _bodySplit;
    private readonly ContextMenuStrip _overflowMenu;

    private int _savedSidebarWidth;
    private int _savedSidebarStackedHeight;
    private bool _allowClose;
    private bool _diagnosticsExpanded;
    private string _selectedDisplayLabel = "Display will follow the active screen";
    private MeetingCandidate? _lastCandidate;
    private bool _lastIsPaused;
    private bool _lastIsRecording;
    private string? _lastActiveSessionTitle;
    private WidgetSection _selectedSection = WidgetSection.LiveCaptions;
    private DashboardLayoutMode _layoutMode;

    public MeetingDashboardStudioForm(RecorderWindowsSettings settings)
    {
        _settings = settings;
        _savedSidebarWidth = Math.Max(_settings.Dashboard.WorkspaceSidebarMinWidth, _settings.Dashboard.WorkspaceSidebarWidth);
        _savedSidebarStackedHeight = Math.Max(220, _settings.Dashboard.WorkspaceSidebarStackedHeight);
        _diagnosticsExpanded = !_settings.Dashboard.DiagnosticsCollapsedByDefault;

        UiTheme.ApplyForm(this);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(
            Math.Max(1040, _settings.Dashboard.MinWindowWidth),
            Math.Max(700, _settings.Dashboard.MinWindowHeight));
        StartPosition = FormStartPosition.CenterScreen;
        Width = Math.Max(MinimumSize.Width, _settings.Dashboard.WindowWidth);
        Height = Math.Max(MinimumSize.Height, _settings.Dashboard.WindowHeight);
        Text = "Meeting Recorder";
        Icon = AppIconProvider.Icon;

        _recordingModeCombo = new UiComboBox
        {
            Dock = DockStyle.Top
        };
        _recordingModeCombo.Items.AddRange(["Screen + audio", "Audio only"]);
        _recordingModeCombo.SelectedIndex = string.Equals(_settings.DefaultRecordingMode, "audio-only", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _recordingModeCombo.SelectedIndexChanged += (_, _) => RenderState();

        _captureModeCombo = new UiComboBox
        {
            Dock = DockStyle.Top
        };
        _captureModeCombo.Items.AddRange(["Full screen", "Meeting window"]);
        _captureModeCombo.SelectedIndex = string.Equals(_settings.DefaultCaptureSource, "window", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _captureModeCombo.SelectedIndexChanged += (_, _) => RenderState();

        _pickScreenButton = CreateButton("Pick screen", UiButtonTone.Secondary, _settings.Dashboard.PickerButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _pickButton = CreateButton("Pick window", UiButtonTone.Neutral, _settings.Dashboard.PickerButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _startButton = CreateButton("Start", UiButtonTone.Primary, _settings.Dashboard.PrimaryActionButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _pauseButton = CreateButton("Pause", UiButtonTone.Neutral, _settings.Dashboard.PrimaryActionButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _resumeButton = CreateButton("Resume", UiButtonTone.Secondary, _settings.Dashboard.PrimaryActionButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _stopButton = CreateButton("Stop", UiButtonTone.Danger, _settings.Dashboard.PrimaryActionButtonWidth, _settings.Dashboard.ControlButtonHeight);
        _captionsNavButton = new UiNavItem { Text = "Live Captions", Height = Math.Max(40, _settings.Dashboard.NavRailButtonHeight) };
        _historyNavButton = new UiNavItem { Text = "Meeting History", Height = Math.Max(40, _settings.Dashboard.NavRailButtonHeight) };
        _actionsNavButton = new UiNavItem { Text = "Action Center", Height = Math.Max(40, _settings.Dashboard.NavRailButtonHeight) };
        _toggleDiagnosticsButton = CreateButton("Show diagnostics", UiButtonTone.Ghost, _settings.Dashboard.DiagnosticsToggleButtonWidth, 30);
        _moreButton = CreateButton("More", UiButtonTone.Ghost, _settings.Dashboard.OverflowButtonWidth, 34);
        _popoutCaptionsButton = CreateButton("Pop out captions", UiButtonTone.Ghost, 132, 32);
        foreach (var button in new[] { _pickScreenButton, _pickButton, _startButton, _pauseButton, _resumeButton, _stopButton })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 8, 8);
        }

        _pickScreenButton.Click += (_, _) => PickScreenRequested?.Invoke(this, EventArgs.Empty);
        _pickButton.Click += (_, _) => PickRequested?.Invoke(this, EventArgs.Empty);
        _startButton.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        _pauseButton.Click += (_, _) => PauseRequested?.Invoke(this, EventArgs.Empty);
        _resumeButton.Click += (_, _) => ResumeRequested?.Invoke(this, EventArgs.Empty);
        _stopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        _captionsNavButton.Click += (_, _) => ApplySelectedSection(WidgetSection.LiveCaptions, true);
        _historyNavButton.Click += (_, _) => ApplySelectedSection(WidgetSection.History, true);
        _actionsNavButton.Click += (_, _) => ApplySelectedSection(WidgetSection.ActionCenter, true);
        _popoutCaptionsButton.Click += (_, _) => PopoutCaptionsRequested?.Invoke(this, EventArgs.Empty);
        _toggleDiagnosticsButton.Click += (_, _) =>
        {
            _diagnosticsExpanded = !_diagnosticsExpanded;
            ApplyDiagnosticsState();
        };

        _overflowMenu = new ContextMenuStrip();
        _overflowMenu.Items.Add("Pop out live captions", null, (_, _) => PopoutCaptionsRequested?.Invoke(this, EventArgs.Empty));
        _overflowMenu.Items.Add("Meetings folder", null, (_, _) => OpenMeetingsFolder());
        _overflowMenu.Items.Add("Reset layout", null, (_, _) => ResetLayout());
        _overflowMenu.Items.Add("Hide app", null, (_, _) => Hide());
        _moreButton.Click += (_, _) => _overflowMenu.Show(_moreButton, new Point(0, _moreButton.Height));

        _statusBadge = new UiBadgeLabel
        {
            Text = "Waiting",
            Width = 108,
            Height = 30,
            FillColor = UiTheme.SurfaceTint,
            BorderColor = UiTheme.SurfaceTint,
            TextColor = UiTheme.TextMuted
        };

        _appTitleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(UiTheme.SectionFont.FontFamily, 15F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = UiTheme.TextPrimary,
            Text = "Meeting Recorder"
        };
        _appSubtitleLabel = new Label
        {
            AutoSize = _settings.Dashboard.ShowTopBarSubtitle,
            Visible = _settings.Dashboard.ShowTopBarSubtitle,
            Font = UiTheme.BodyFont,
            ForeColor = UiTheme.TextMuted,
            Text = "A focused workspace for recording, captions, history, and follow-up."
        };
        _meetingHeadlineLabel = new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFontSemibold,
            ForeColor = UiTheme.TextPrimary,
            Text = "No active meeting target"
        };
        _meetingStatusLabel = new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFont,
            ForeColor = UiTheme.TextMuted,
            Text = "Manual recording is available."
        };

        _summaryStateLabel = new Label
        {
            AutoSize = true,
            Font = new Font(UiTheme.SectionFont.FontFamily, 13F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = UiTheme.TextPrimary,
            Text = "Waiting for a meeting"
        };
        _summaryHintLabel = CreateMutedLabel("Start manually, or bring a meeting window forward.");
        _meetingValueLabel = CreateValueLabel();
        _modeValueLabel = CreateValueLabel();
        _captureValueLabel = CreateValueLabel();
        _nextStepValueLabel = CreateValueLabel();
        _commandHintLabel = CreateMutedLabel("Pick a window only when you want targeted window capture.");
        _workspaceTitleLabel = new Label
        {
            AutoSize = true,
            Font = UiTheme.SectionFont,
            ForeColor = UiTheme.TextPrimary,
            Text = "Live Captions"
        };
        _workspaceSubtitleLabel = CreateMutedLabel("Keep the latest speech visible while the recorder runs.");

        _diagnosticsBoxWrapper = new UiTextBoxWrapper(multiline: true, readOnly: true) { Dock = DockStyle.Fill };
        _diagnosticsBox = _diagnosticsBoxWrapper.InnerTextBox;

        _captionsStatusLabel = new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFontSemibold,
            ForeColor = UiTheme.TextPrimary,
            Text = "Waiting"
        };
        _captionsMetaLabel = CreateMutedLabel("Participant hints and caption source details will appear here.");
        _captionsBoxWrapper = new UiTextBoxWrapper(multiline: true, readOnly: true) { Dock = DockStyle.Fill };
        _captionsBox = _captionsBoxWrapper.InnerTextBox;
        _captionsBox.Text = "Start a recording to stream live captions here.";

        _historyHostPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
        _actionsHostPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
        _captionsHostPanel = BuildCaptionsWorkspace();
        _workspaceHostPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        _workspaceHostPanel.Controls.Add(_actionsHostPanel);
        _workspaceHostPanel.Controls.Add(_historyHostPanel);
        _workspaceHostPanel.Controls.Add(_captionsHostPanel);

        _diagnosticsCardHostPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 0,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 10, 0, 0)
        };
        _diagnosticsCardHostPanel.Controls.Add(BuildDiagnosticsCard());

        _bodySplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = 6,
            SplitterDistance = _savedSidebarWidth,
            BackColor = UiTheme.AppBackground
        };
        _bodySplit.Panel1.BackColor = UiTheme.AppBackground;
        _bodySplit.Panel2.BackColor = UiTheme.AppBackground;
        _bodySplit.SplitterMoved += (_, _) =>
        {
            if (_layoutMode == DashboardLayoutMode.Stacked)
            {
                _savedSidebarStackedHeight = _bodySplit.SplitterDistance;
            }
            else
            {
                _savedSidebarWidth = _bodySplit.SplitterDistance;
            }
        };

        _bodySplit.Panel1.Controls.Add(BuildSidebarSurface());
        _bodySplit.Panel2.Controls.Add(BuildWorkspaceSurface());

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.AppBackground,
            Margin = Padding.Empty,
            Padding = new Padding(18, 12, 18, 18)
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, _settings.Dashboard.AppBarHeight));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, _settings.Dashboard.SummaryStripHeight));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(BuildAppBar(), 0, 0);
        rootLayout.Controls.Add(BuildSummaryStrip(), 0, 1);
        rootLayout.Controls.Add(_bodySplit, 0, 2);
        Controls.Add(rootLayout);

        FormClosing += OnFormClosing;
        Load += (_, _) =>
        {
            ApplyDiagnosticsState();
            ApplyResponsiveLayout();
            ApplySelectedSection(WidgetSection.LiveCaptions, false);
            RenderState();
        };
        Shown += (_, _) =>
        {
            ApplyResponsiveLayout();
            ApplySelectedSection(_selectedSection, false);
        };
        Resize += (_, _) =>
        {
            ApplyResponsiveLayout();
            ApplyLabelWidths();
        };
    }

    public event EventHandler? PickRequested;
    public event EventHandler? PickScreenRequested;
    public event EventHandler? StartRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? ResumeRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? CaptionsRequested;
    public event EventHandler? PopoutCaptionsRequested;
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

    public void UpdateCaptions(string status, string text, string? meta = null)
    {
        _captionsStatusLabel.Text = string.IsNullOrWhiteSpace(status) ? "Listening" : status;

        var metaText = string.IsNullOrWhiteSpace(meta)
            ? "Participant hints and caption source details will appear here."
            : meta.Trim();
        var bodyText = text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var normalized = text.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            if (lines.Length > 0 && lines[0].StartsWith("Participants detected:", StringComparison.OrdinalIgnoreCase))
            {
                metaText = string.IsNullOrWhiteSpace(metaText)
                    ? lines[0].Trim()
                    : $"{metaText} | {lines[0].Trim()}";
                bodyText = lines.Length > 1
                    ? string.Join(Environment.NewLine, lines, 1, lines.Length - 1).Trim()
                    : string.Empty;
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
        ApplySelectedSection(section, false);
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
        titleStack.Controls.Add(_appTitleLabel, 0, 0);
        if (_settings.Dashboard.ShowTopBarSubtitle)
        {
            titleStack.Controls.Add(_appSubtitleLabel, 0, 1);
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
        statusTextStack.Controls.Add(_meetingHeadlineLabel, 0, 0);
        statusTextStack.Controls.Add(_meetingStatusLabel, 0, 1);

        var centerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        centerLayout.Controls.Add(_statusBadge, 0, 0);
        centerLayout.Controls.Add(statusTextStack, 1, 0);

        var appBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, 12)
        };
        appBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
        appBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46F));
        appBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        appBar.Controls.Add(titleStack, 0, 0);
        appBar.Controls.Add(centerLayout, 1, 0);
        appBar.Controls.Add(_moreButton, 2, 0);
        return appBar;
    }

    private Control BuildSummaryStrip()
    {
        var summaryGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        summaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
        summaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14F));
        summaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14F));
        summaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17F));
        summaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17F));

        var stateStack = CreateStack(_summaryStateLabel, _summaryHintLabel);
        summaryGrid.Controls.Add(stateStack, 0, 0);
        summaryGrid.Controls.Add(CreateFactTile("Meeting", _meetingValueLabel), 1, 0);
        summaryGrid.Controls.Add(CreateFactTile("Mode", _modeValueLabel), 2, 0);
        summaryGrid.Controls.Add(CreateFactTile("Capture", _captureValueLabel), 3, 0);
        summaryGrid.Controls.Add(CreateFactTile("Next", _nextStepValueLabel), 4, 0);

        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            ShowAccent = false,
            Padding = new Padding(16, 12, 16, 12)
        };
        card.Controls.Add(summaryGrid);
        return card;
    }

    private Control BuildSidebarSurface()
    {
        foreach (var button in new[] { _captionsNavButton, _historyNavButton, _actionsNavButton })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = Padding.Empty;
        }

        var navGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 12),
            Padding = Padding.Empty
        };
        navGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        navGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, _settings.Dashboard.NavRailButtonHeight));
        navGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, _settings.Dashboard.NavRailButtonHeight));
        navGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, _settings.Dashboard.NavRailButtonHeight));
        navGrid.Controls.Add(_captionsNavButton, 0, 0);
        navGrid.Controls.Add(_historyNavButton, 0, 1);
        navGrid.Controls.Add(_actionsNavButton, 0, 2);

        var pickerGrid = CreateCompactButtonGrid();
        pickerGrid.Margin = new Padding(0, 8, 0, 8);
        pickerGrid.Controls.Add(_pickScreenButton, 0, 0);
        pickerGrid.Controls.Add(_pickButton, 1, 0);

        var transportGrid = CreateCompactButtonGrid();
        transportGrid.Margin = new Padding(0, 8, 0, 8);
        transportGrid.Controls.Add(_startButton, 0, 0);
        transportGrid.Controls.Add(_pauseButton, 1, 0);
        transportGrid.Controls.Add(_resumeButton, 0, 1);
        transportGrid.Controls.Add(_stopButton, 1, 1);

        _diagnosticsCardHostPanel.Padding = new Padding(0, 8, 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 12,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var index = 0; index < 11; index++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(CreateSidebarSectionLabel("Workspace"), 0, 0);
        layout.Controls.Add(navGrid, 0, 1);
        layout.Controls.Add(CreateSidebarSectionLabel("Record mode"), 0, 2);
        layout.Controls.Add(_recordingModeCombo, 0, 3);
        layout.Controls.Add(CreateSidebarSectionLabel("Capture source"), 0, 4);
        layout.Controls.Add(_captureModeCombo, 0, 5);
        layout.Controls.Add(CreateSidebarSectionLabel("Target tools"), 0, 6);
        layout.Controls.Add(pickerGrid, 0, 7);
        layout.Controls.Add(CreateSidebarSectionLabel("Transport"), 0, 8);
        layout.Controls.Add(transportGrid, 0, 9);
        layout.Controls.Add(_commandHintLabel, 0, 10);
        layout.Controls.Add(_toggleDiagnosticsButton, 0, 11);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = Padding.Empty
        };
        body.Controls.Add(layout);

        var shell = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            ShowAccent = false,
            FillColor = UiTheme.SurfaceAlt,
            Padding = new Padding(10)
        };
        shell.Controls.Add(body);
        shell.Controls.Add(_diagnosticsCardHostPanel);
        return shell;
    }

    private Control BuildNavigationCard()
    {
        foreach (var button in new[] { _captionsNavButton, _historyNavButton, _actionsNavButton })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = Padding.Empty;
        }

        var navGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 10, 0, 0),
            Padding = Padding.Empty
        };
        navGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        navGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, _settings.Dashboard.NavRailButtonHeight));
        navGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, _settings.Dashboard.NavRailButtonHeight));
        navGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, _settings.Dashboard.NavRailButtonHeight));
        navGrid.Controls.Add(_captionsNavButton, 0, 0);
        navGrid.Controls.Add(_historyNavButton, 0, 1);
        navGrid.Controls.Add(_actionsNavButton, 0, 2);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.Controls.Add(CreateSectionHeader("Workspace", "Choose the active pane for captions, history, or action review."), 0, 0);
        body.Controls.Add(navGrid, 0, 1);

        var card = new UiCardPanel
        {
            Dock = DockStyle.Top,
            AccentColor = UiTheme.Accent,
            ShowAccent = false,
            Padding = new Padding(14),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 10)
        };
        card.Controls.Add(body);
        return card;
    }

    private Control BuildCommandCard()
    {
        var fieldsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        fieldsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fieldsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fieldsLayout.Controls.Add(CreateFieldGroup("Record mode", _recordingModeCombo), 0, 0);
        fieldsLayout.Controls.Add(CreateFieldGroup("Capture source", _captureModeCombo), 0, 1);

        var pickerGrid = CreateCompactButtonGrid();
        pickerGrid.Controls.Add(_pickScreenButton, 0, 0);
        pickerGrid.Controls.Add(_pickButton, 1, 0);

        var transportGrid = CreateCompactButtonGrid();
        transportGrid.Controls.Add(_startButton, 0, 0);
        transportGrid.Controls.Add(_pauseButton, 1, 0);
        transportGrid.Controls.Add(_resumeButton, 0, 1);
        transportGrid.Controls.Add(_stopButton, 1, 1);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.Controls.Add(CreateSectionHeader("Command Bar", "Primary controls stay visible here while the workspace stays focused on content."), 0, 0);
        body.Controls.Add(fieldsLayout, 0, 1);
        body.Controls.Add(pickerGrid, 0, 2);
        body.Controls.Add(transportGrid, 0, 3);
        body.Controls.Add(_commandHintLabel, 0, 4);
        body.Controls.Add(_toggleDiagnosticsButton, 0, 5);

        var card = new UiCardPanel
        {
            Dock = DockStyle.Top,
            AccentColor = UiTheme.Success,
            ShowAccent = false,
            Padding = new Padding(14),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty
        };
        card.Controls.Add(body);
        return card;
    }

    private Control BuildDiagnosticsCard()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        body.Controls.Add(CreateSectionHeader("Diagnostics", "Collapsed by default so the sidebar stays focused."), 0, 0);
        body.Controls.Add(_diagnosticsBoxWrapper, 0, 1);

        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Warning,
            ShowAccent = false,
            Padding = new Padding(16)
        };
        card.Controls.Add(body);
        return card;
    }

    private Control BuildWorkspaceSurface()
    {
        var workspaceLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(10, 0, 0, 0)
        };
        workspaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        workspaceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspaceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        workspaceLayout.Controls.Add(BuildWorkspaceHeader(), 0, 0);
        workspaceLayout.Controls.Add(_workspaceHostPanel, 0, 1);
        return workspaceLayout;
    }

    private Control BuildWorkspaceHeader()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var textStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        textStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        textStack.Controls.Add(_workspaceTitleLabel, 0, 0);
        textStack.Controls.Add(_workspaceSubtitleLabel, 0, 1);

        _popoutCaptionsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _popoutCaptionsButton.Margin = new Padding(12, 0, 0, 0);

        layout.Controls.Add(textStack, 0, 0);
        layout.Controls.Add(_popoutCaptionsButton, 1, 0);
        return layout;
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

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        body.Controls.Add(statusStrip, 0, 0);
        body.Controls.Add(_captionsBoxWrapper, 0, 1);

        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            ShowAccent = false,
            Padding = new Padding(14)
        };
        card.Controls.Add(body);

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        host.Controls.Add(card);
        return host;
    }

    private void ApplySelectedSection(WidgetSection section, bool fireEvents)
    {
        _selectedSection = section;
        _captionsHostPanel.Visible = section == WidgetSection.LiveCaptions;
        _historyHostPanel.Visible = section == WidgetSection.History;
        _actionsHostPanel.Visible = section == WidgetSection.ActionCenter;

        _captionsNavButton.Selected = section == WidgetSection.LiveCaptions;
        _historyNavButton.Selected = section == WidgetSection.History;
        _actionsNavButton.Selected = section == WidgetSection.ActionCenter;

        switch (section)
        {
            case WidgetSection.History:
                _workspaceTitleLabel.Text = "Meeting History";
                _workspaceSubtitleLabel.Text = "Search sessions, inspect saved evidence, and ask grounded follow-up questions.";
                break;
            case WidgetSection.ActionCenter:
                _workspaceTitleLabel.Text = "Action Center";
                _workspaceSubtitleLabel.Text = "Review extracted tasks and follow-up drafts without leaving the dashboard.";
                break;
            default:
                _workspaceTitleLabel.Text = "Live Captions";
                _workspaceSubtitleLabel.Text = "Keep the latest speech visible while the recorder runs.";
                break;
        }

        _popoutCaptionsButton.Visible = section == WidgetSection.LiveCaptions;

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
        _diagnosticsCardHostPanel.Visible = _diagnosticsExpanded;
        _diagnosticsCardHostPanel.Height = _diagnosticsExpanded ? 170 : 0;
        _toggleDiagnosticsButton.Text = _diagnosticsExpanded ? "Hide diagnostics" : "Show diagnostics";
    }

    private void ApplyResponsiveLayout()
    {
        _layoutMode = DetermineLayoutMode();
        _bodySplit.SuspendLayout();

        if (_layoutMode == DashboardLayoutMode.Stacked)
        {
            _bodySplit.Orientation = Orientation.Horizontal;
            _bodySplit.Panel1MinSize = 220;
            _bodySplit.Panel2MinSize = 260;
            _bodySplit.SplitterDistance = Math.Max(220, Math.Min(_savedSidebarStackedHeight, _bodySplit.Height - 260));
        }
        else
        {
            _bodySplit.Orientation = Orientation.Vertical;
            _bodySplit.Panel1MinSize = _settings.Dashboard.WorkspaceSidebarMinWidth;
            _bodySplit.Panel2MinSize = Math.Max(420, _settings.Dashboard.MinimumControlPanelWidth);
            var targetWidth = _layoutMode == DashboardLayoutMode.Wide
                ? _savedSidebarWidth
                : Math.Min(_savedSidebarWidth, 320);
            _bodySplit.SplitterDistance = Math.Max(
                _settings.Dashboard.WorkspaceSidebarMinWidth,
                Math.Min(targetWidth, _bodySplit.Width - _bodySplit.Panel2MinSize));
        }

        _bodySplit.ResumeLayout();
        ApplyLabelWidths();
    }

    private DashboardLayoutMode DetermineLayoutMode()
    {
        if (ClientSize.Width >= _settings.Dashboard.WideBreakpointWidth)
        {
            return DashboardLayoutMode.Wide;
        }

        return ClientSize.Width < _settings.Dashboard.StackedBreakpointWidth
            ? DashboardLayoutMode.Stacked
            : DashboardLayoutMode.Medium;
    }

    private void ApplyLabelWidths()
    {
        var summaryWidth = Math.Max(180, (ClientSize.Width - 120) / 5);
        _summaryHintLabel.MaximumSize = new Size(Math.Max(260, ClientSize.Width / 4), 0);
        _meetingValueLabel.MaximumSize = new Size(summaryWidth, 0);
        _modeValueLabel.MaximumSize = new Size(summaryWidth, 0);
        _captureValueLabel.MaximumSize = new Size(summaryWidth, 0);
        _nextStepValueLabel.MaximumSize = new Size(summaryWidth, 0);
        _meetingHeadlineLabel.MaximumSize = new Size(_settings.Dashboard.TopBarStatusMaxWidth, 0);
        _meetingStatusLabel.MaximumSize = new Size(_settings.Dashboard.TopBarStatusMaxWidth, 0);
        _captionsMetaLabel.MaximumSize = new Size(Math.Max(280, _workspaceHostPanel.ClientSize.Width - 32), 0);
    }

    private void RenderState()
    {
        var recordingModeLabel = _recordingModeCombo.SelectedIndex == 1 ? "Audio only" : "Screen + audio";
        var captureModeLabel = _recordingModeCombo.SelectedIndex == 1
            ? "Audio only"
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
            nextStep = "Resume or stop the active session.";
            hint = "The current session folder stays active while paused.";
            diagnostics = _lastActiveSessionTitle is null
                ? "The recorder is paused. Resume to continue capture into the current session."
                : $"The recorder is paused for {_lastActiveSessionTitle}. Resume to continue the same session.";
            ApplyBadge("Paused", UiTheme.WarningSoft, UiTheme.WarningSoft, UiTheme.Warning, true, UiTheme.WarningGlow);
        }
        else if (_lastIsRecording)
        {
            stateTitle = "Recording now";
            nextStep = "Pause for a break, or stop when the session is complete.";
            hint = "Live captions continue updating while capture is active.";
            diagnostics = _lastActiveSessionTitle is null
                ? "The recorder is active. Use the sidebar to switch between captions, history, and actions."
                : $"The recorder is active for {_lastActiveSessionTitle}. Session artifacts are being written now.";
            ApplyBadge("Recording", UiTheme.SuccessSoft, UiTheme.SuccessSoft, UiTheme.Success, true, UiTheme.SuccessGlow);
        }
        else if (_lastCandidate is not null)
        {
            stateTitle = "Meeting detected";
            nextStep = "Start now, or change capture source before recording.";
            hint = $"Detected {_lastCandidate.Platform}. You can start immediately.";
            diagnostics = $"Detected {_lastCandidate.Title} from {_lastCandidate.ProcessName} ({_lastCandidate.Platform}).";
            ApplyBadge("Detected", UiTheme.AccentSoft, UiTheme.AccentSoft, UiTheme.AccentStrong, true, UiTheme.AccentGlow);
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

        _meetingHeadlineLabel.Text = _lastCandidate?.DisplayLabel ?? (_lastActiveSessionTitle ?? "No active meeting target");
        _meetingStatusLabel.Text = _lastIsPaused
            ? "Paused. Resume to continue the active session."
            : _lastIsRecording
                ? "Capture is active. Use the sidebar to navigate the workspace."
                : nextStep;

        var controlsLocked = _lastIsRecording || _lastIsPaused;
        _startButton.Enabled = !controlsLocked;
        _pauseButton.Enabled = _lastIsRecording && !_lastIsPaused;
        _resumeButton.Enabled = _lastIsPaused;
        _stopButton.Enabled = _lastIsRecording || _lastIsPaused;
        _recordingModeCombo.Enabled = !controlsLocked;
        _captureModeCombo.Enabled = !_lastIsPaused && _recordingModeCombo.SelectedIndex == 0;
        _pickScreenButton.Enabled = !_lastIsPaused && (_lastIsRecording || (_recordingModeCombo.SelectedIndex == 0 && SelectedCaptureSource == VideoCaptureSource.Display));
        _pickButton.Enabled = !_lastIsPaused;
        _commandHintLabel.Text = _lastIsRecording || _lastIsPaused
            ? _lastIsPaused
                ? "Resume the active session before changing the capture target."
                : _recordingModeCombo.SelectedIndex == 1
                    ? "This session started as audio only. Use Pick screen or Pick window to upgrade it without stopping."
                    : "You can retarget the active session with Pick screen or Pick window without stopping the recording."
            : SelectedCaptureSource == VideoCaptureSource.Window && _recordingModeCombo.SelectedIndex == 0
                ? "Meeting window mode works best after you use Pick window."
                : "Start can run a manual recording immediately. Use Pick window only when you want a specific app target.";

        ApplyLabelWidths();
    }

    private void ApplyBadge(string text, Color fill, Color border, Color fore, bool showGlow = false, Color? glowColor = null)
    {
        _statusBadge.Text = text;
        _statusBadge.FillColor = fill;
        _statusBadge.BorderColor = border;
        _statusBadge.TextColor = fore;
        _statusBadge.ShowGlow = showGlow;
        if (glowColor.HasValue) _statusBadge.GlowColor = glowColor.Value;
        _statusBadge.Invalidate();
    }

    private void ResetLayout()
    {
        _savedSidebarWidth = Math.Max(_settings.Dashboard.WorkspaceSidebarMinWidth, _settings.Dashboard.WorkspaceSidebarWidth);
        _savedSidebarStackedHeight = Math.Max(220, _settings.Dashboard.WorkspaceSidebarStackedHeight);
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
            Width = Math.Max(82, width),
            Height = height,
            Margin = new Padding(0, 0, 8, 8)
        };
    }

    private static TableLayoutPanel CreateCompactButtonGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 10, 0, 0),
            Padding = Padding.Empty
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        return grid;
    }

    private static Label CreateSidebarSectionLabel(string text)
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

    private static Control CreateFieldGroup(string labelText, Control control)
    {
        var label = CreateMinorLabel(labelText);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 10),
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(control, 0, 1);
        return layout;
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

    private static Control CreateFactTile(string labelText, Label valueLabel)
    {
        var keyLabel = new Label
        {
            AutoSize = true,
            Font = UiTheme.EyebrowFont,
            ForeColor = UiTheme.TextMuted,
            Margin = Padding.Empty,
            Text = labelText.ToUpperInvariant()
        };
        valueLabel.Margin = new Padding(0, 6, 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(8, 0, 0, 0),
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(keyLabel, 0, 0);
        layout.Controls.Add(valueLabel, 0, 1);

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(12, 4, 4, 4)
        };
        host.Controls.Add(layout);
        return host;
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
}
