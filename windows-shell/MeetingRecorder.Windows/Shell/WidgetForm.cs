using System.Diagnostics;
using MeetingRecorder.Windows.Models;

namespace MeetingRecorder.Windows.Shell;

public enum WidgetSection
{
    LiveCaptions,
    History,
    ActionCenter
}

public sealed class WidgetForm : Form
{
    private static int _savedRootSplitDistance = 258;
    private static int _savedHeaderSplitDistance = 448;
    private static int _savedWorkspaceSplitDistance = 212;

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
    private readonly UiButton _navCaptionsButton;
    private readonly UiButton _navHistoryButton;
    private readonly UiButton _navActionsButton;
    private readonly Label _footerLabel;
    private readonly Label _captionsStatusLabel;
    private readonly Label _stateTitleLabel;
    private readonly Label _meetingValueLabel;
    private readonly Label _modeValueLabel;
    private readonly Label _captureValueLabel;
    private readonly Label _nextStepValueLabel;
    private readonly Label _workspaceTitleLabel;
    private readonly Label _workspaceSubtitleLabel;
    private readonly SplitContainer _rootSplit;
    private readonly SplitContainer _headerSplit;
    private readonly SplitContainer _workspaceSplit;
    private readonly Panel _workspaceHostPanel;
    private readonly Panel _captionsPanel;
    private readonly Panel _historyHostPanel;
    private readonly Panel _actionsHostPanel;
    private readonly TextBox _captionsBox;
    private readonly TextBox _statusBox;
    private readonly UiBadgeLabel _statusBadge;

    private bool _allowClose;
    private string _selectedDisplayLabel = "Display will follow the meeting window";
    private MeetingCandidate? _lastCandidate;
    private bool _lastIsPaused;
    private bool _lastIsRecording;
    private string? _lastActiveSessionTitle;
    private WidgetSection _selectedSection = WidgetSection.LiveCaptions;

    public WidgetForm()
    {
        UiTheme.ApplyForm(this);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(1120, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1500;
        Height = 930;
        Text = "Meeting Recorder";
        Icon = AppIconProvider.Icon;

        _statusBadge = new UiBadgeLabel
        {
            Text = "Waiting",
            Width = 132,
            Height = 34,
            FillColor = UiTheme.SurfaceTint,
            BorderColor = UiTheme.SurfaceTint,
            TextColor = UiTheme.TextMuted,
            Margin = new Padding(0, 10, 0, 0)
        };

        var topBar = CreateTopBar();

        _stateTitleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(UiTheme.SectionFont.FontFamily, 14F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = UiTheme.TextPrimary,
            Text = "Waiting for a meeting"
        };

        var factsTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 10),
            Padding = Padding.Empty
        };
        factsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
        factsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _meetingValueLabel = CreateStatusValueLabel();
        _modeValueLabel = CreateStatusValueLabel();
        _captureValueLabel = CreateStatusValueLabel();
        _nextStepValueLabel = CreateStatusValueLabel();

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
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle
        };
        UiTheme.StyleTextBox(_statusBox, readOnly: true);

        var statusBody = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };
        statusBody.Controls.Add(_statusBox);
        statusBody.Controls.Add(factsTable);
        statusBody.Controls.Add(_stateTitleLabel);

        var statusCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            Padding = new Padding(18)
        };
        statusCard.Controls.Add(statusBody);
        statusCard.Controls.Add(UiTheme.CreateSectionTitle("Session snapshot", "See what is detected, what will be captured, and the best next action at a glance."));

        _recordingModeCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        UiTheme.StyleComboBox(_recordingModeCombo);
        _recordingModeCombo.Items.AddRange(["Screen + audio", "Audio only"]);
        _recordingModeCombo.SelectedIndex = 0;
        _recordingModeCombo.SelectedIndexChanged += (_, _) => RenderState();

        _captureModeCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        UiTheme.StyleComboBox(_captureModeCombo);
        _captureModeCombo.Items.AddRange(["Full screen", "Meeting window"]);
        _captureModeCombo.SelectedIndex = 0;
        _captureModeCombo.SelectedIndexChanged += (_, _) => RenderState();

        _pickScreenButton = CreateButton("Pick screen", UiButtonTone.Secondary, 132);
        _pickButton = CreateButton("Pick window", UiButtonTone.Neutral, 132);
        _startButton = CreateButton("Start", UiButtonTone.Primary, 128);
        _pauseButton = CreateButton("Pause", UiButtonTone.Neutral, 128);
        _resumeButton = CreateButton("Resume", UiButtonTone.Secondary, 128);
        _stopButton = CreateButton("Stop", UiButtonTone.Danger, 128);
        _meetingsFolderButton = CreateButton("Meetings folder", UiButtonTone.Ghost, 144);
        _resetLayoutButton = CreateButton("Reset layout", UiButtonTone.Ghost, 120);
        _hideButton = CreateButton("Hide app", UiButtonTone.Ghost, 104);

        _meetingsFolderButton.Click += (_, _) => OpenMeetingsFolder();
        _resetLayoutButton.Click += (_, _) => ResetLayout();
        _hideButton.Click += (_, _) => Hide();

        var modeGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 88,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 12),
            Padding = Padding.Empty
        };
        modeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        modeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        modeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        modeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        modeGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        modeGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        modeGrid.Controls.Add(CreateGridLabel("Record"), 0, 0);
        modeGrid.Controls.Add(CreateGridLabel("Capture"), 1, 0);
        modeGrid.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent }, 2, 0);
        modeGrid.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent }, 3, 0);
        modeGrid.Controls.Add(_recordingModeCombo, 0, 1);
        modeGrid.Controls.Add(_captureModeCombo, 1, 1);
        modeGrid.Controls.Add(WrapButtonCell(_pickScreenButton), 2, 1);
        modeGrid.Controls.Add(WrapButtonCell(_pickButton), 3, 1);

        var actionGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 12),
            Padding = Padding.Empty
        };
        for (var i = 0; i < 4; i++)
        {
            actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        }
        actionGrid.Controls.Add(WrapButtonCell(_startButton), 0, 0);
        actionGrid.Controls.Add(WrapButtonCell(_pauseButton), 1, 0);
        actionGrid.Controls.Add(WrapButtonCell(_resumeButton), 2, 0);
        actionGrid.Controls.Add(WrapButtonCell(_stopButton), 3, 0);

        var utilityGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        utilityGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        utilityGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        utilityGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        utilityGrid.Controls.Add(WrapButtonCell(_meetingsFolderButton), 0, 0);
        utilityGrid.Controls.Add(WrapButtonCell(_resetLayoutButton), 1, 0);
        utilityGrid.Controls.Add(WrapButtonCell(_hideButton), 2, 0);

        var controlsBody = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };
        controlsBody.Controls.Add(utilityGrid);
        controlsBody.Controls.Add(actionGrid);
        controlsBody.Controls.Add(modeGrid);

        var controlsCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Success,
            Padding = new Padding(18)
        };
        controlsCard.Controls.Add(controlsBody);
        controlsCard.Controls.Add(UiTheme.CreateSectionTitle("Recording controls", "Choose recording mode, pick a screen or window when needed, then control the session from one compact surface."));

        _headerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = 10
        };
        _headerSplit.Panel1.Controls.Add(statusCard);
        _headerSplit.Panel2.Controls.Add(controlsCard);
        _headerSplit.SplitterMoved += (_, _) => _savedHeaderSplitDistance = _headerSplit.SplitterDistance;

        _navCaptionsButton = CreateNavButton("Live captions", "See the running caption stream while the session is active.");
        _navHistoryButton = CreateNavButton("Meeting history", "Search saved meetings, inspect evidence, and ask focused questions.");
        _navActionsButton = CreateNavButton("Action center", "Review action items and follow-up text generated from meetings.");

        _navCaptionsButton.Click += (_, _) =>
        {
            SelectSection(WidgetSection.LiveCaptions);
            CaptionsRequested?.Invoke(this, EventArgs.Empty);
        };
        _navHistoryButton.Click += (_, _) =>
        {
            SelectSection(WidgetSection.History);
            HistoryRequested?.Invoke(this, EventArgs.Empty);
        };
        _navActionsButton.Click += (_, _) =>
        {
            SelectSection(WidgetSection.ActionCenter);
            ActionsRequested?.Invoke(this, EventArgs.Empty);
        };

        var navStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        navStack.Controls.Add(_navCaptionsButton);
        navStack.Controls.Add(_navHistoryButton);
        navStack.Controls.Add(_navActionsButton);

        var navBody = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };
        navBody.Controls.Add(navStack);

        var navCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Warning,
            Padding = new Padding(16)
        };
        navCard.Controls.Add(navBody);
        navCard.Controls.Add(UiTheme.CreateSectionTitle("Workspace", "Move between captions, meeting history, and action review without spawning extra windows."));

        _workspaceTitleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(UiTheme.SectionFont.FontFamily, 13F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = UiTheme.TextPrimary,
            Text = "Live captions"
        };

        _workspaceSubtitleLabel = new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFont,
            ForeColor = UiTheme.TextMuted,
            Text = "Monitor the caption stream while the rest of the workspace stays visible."
        };

        var workspaceHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            BackColor = Color.Transparent
        };
        workspaceHeader.Controls.Add(_workspaceSubtitleLabel);
        workspaceHeader.Controls.Add(_workspaceTitleLabel);
        _workspaceTitleLabel.Location = new Point(0, 0);
        _workspaceSubtitleLabel.Location = new Point(0, 26);

        _captionsStatusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Font = UiTheme.BodyFontSemibold,
            ForeColor = UiTheme.TextMuted,
            Text = "Captions will appear here when recording starts."
        };

        _captionsBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical
        };
        UiTheme.StyleTextBox(_captionsBox, readOnly: true);
        _captionsBox.Text = "Start a recording to stream live captions, participant hints, and caption diagnostics into this panel.";

        var captionsCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Warning,
            Padding = new Padding(18)
        };
        captionsCard.Controls.Add(_captionsBox);
        captionsCard.Controls.Add(_captionsStatusLabel);
        captionsCard.Controls.Add(UiTheme.CreateSectionTitle("Live caption stream", "Keep the current session visible here while you continue working in the rest of the dashboard."));

        _captionsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        _captionsPanel.Controls.Add(captionsCard);

        _historyHostPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        _historyHostPanel.Controls.Add(CreateEmptyStateCard("Meeting history will load here when you open that workspace."));

        _actionsHostPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        _actionsHostPanel.Controls.Add(CreateEmptyStateCard("Action items and follow-up will appear here when you open the action center."));

        _workspaceHostPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        _workspaceHostPanel.Controls.Add(_captionsPanel);
        _workspaceHostPanel.Controls.Add(_historyHostPanel);
        _workspaceHostPanel.Controls.Add(_actionsHostPanel);

        var workspaceCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            Padding = new Padding(18)
        };
        workspaceCard.Controls.Add(_workspaceHostPanel);
        workspaceCard.Controls.Add(workspaceHeader);

        _workspaceSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = 10
        };
        _workspaceSplit.Panel1.Controls.Add(navCard);
        _workspaceSplit.Panel2.Controls.Add(workspaceCard);
        _workspaceSplit.SplitterMoved += (_, _) => _savedWorkspaceSplitDistance = _workspaceSplit.SplitterDistance;

        _rootSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 10
        };
        _rootSplit.Panel1.Controls.Add(_headerSplit);
        _rootSplit.Panel2.Controls.Add(_workspaceSplit);
        _rootSplit.SplitterMoved += (_, _) => _savedRootSplitDistance = _rootSplit.SplitterDistance;

        _footerLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            ForeColor = UiTheme.TextMuted,
            Font = UiTheme.BodyFont,
            Text = "Resize the window or drag the split bars to fit your meeting workflow."
        };

        var chromePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.AppBackground,
            Padding = new Padding(14)
        };
        chromePanel.Controls.Add(_rootSplit);
        chromePanel.Controls.Add(_footerLabel);
        chromePanel.Controls.Add(topBar);
        Controls.Add(chromePanel);

        _pickButton.Click += (_, _) => PickRequested?.Invoke(this, EventArgs.Empty);
        _pickScreenButton.Click += (_, _) => PickScreenRequested?.Invoke(this, EventArgs.Empty);
        _startButton.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        _pauseButton.Click += (_, _) => PauseRequested?.Invoke(this, EventArgs.Empty);
        _resumeButton.Click += (_, _) => ResumeRequested?.Invoke(this, EventArgs.Empty);
        _stopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        Load += (_, _) => ClampSplitters();
        Shown += (_, _) => BeginInvoke((Action)ClampSplitters);
        Resize += (_, _) => ClampSplitters();
        FormClosing += OnFormClosing;

        SelectSection(WidgetSection.LiveCaptions);
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
        _captionsBox.Text = string.IsNullOrWhiteSpace(text)
            ? "No caption content has arrived yet."
            : text;
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
        _selectedSection = section;
        _captionsPanel.Visible = section == WidgetSection.LiveCaptions;
        _historyHostPanel.Visible = section == WidgetSection.History;
        _actionsHostPanel.Visible = section == WidgetSection.ActionCenter;

        if (section == WidgetSection.History)
        {
            _historyHostPanel.BringToFront();
        }
        else if (section == WidgetSection.ActionCenter)
        {
            _actionsHostPanel.BringToFront();
        }
        else
        {
            _captionsPanel.BringToFront();
        }

        (_workspaceTitleLabel.Text, _workspaceSubtitleLabel.Text) = section switch
        {
            WidgetSection.History => (
                "Meeting history",
                "Search saved sessions, inspect transcript evidence, and ask grounded follow-up questions."
            ),
            WidgetSection.ActionCenter => (
                "Action center",
                "Review extracted action items, follow-up text, and the outputs tied to a completed session."
            ),
            _ => (
                "Live captions",
                "Watch the current caption stream while you keep the dashboard open."
            )
        };

        ApplyNavSelection();
        if (IsHandleCreated)
        {
            if (!Visible)
            {
                Show();
            }
            BringWindowToFront();
        }
    }

    public void PrepareForExit()
    {
        _allowClose = true;
    }

    private void ResetLayout()
    {
        _savedRootSplitDistance = 258;
        _savedHeaderSplitDistance = 448;
        _savedWorkspaceSplitDistance = 212;
        ClampSplitters();
        _footerLabel.Text = "Layout reset. Resize the window or drag the split bars to fit your workflow again.";
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

        if (_lastIsPaused)
        {
            stateTitle = "Recording paused";
            nextStep = "Resume to continue the current session or Stop to finalize it";
            narrative = _lastActiveSessionTitle is null
                ? "The session is paused. Resume when you want live captions and capture to continue."
                : $"The session for {_lastActiveSessionTitle} is paused. Resume to continue recording into the same session folder, or stop it to start processing.";
            _statusBadge.Text = "Paused";
            _statusBadge.FillColor = UiTheme.WarningSoft;
            _statusBadge.BorderColor = UiTheme.WarningSoft;
            _statusBadge.TextColor = UiTheme.Warning;
        }
        else if (_lastIsRecording)
        {
            stateTitle = "Recording in progress";
            nextStep = "Pause if you need a break or Stop when the meeting ends";
            narrative = _lastActiveSessionTitle is null
                ? "Recording is active. Live captions and post-processing will continue updating through this dashboard."
                : $"Recording is active for {_lastActiveSessionTitle}. Use the workspace below for captions now, then open history or action review after the meeting completes.";
            _statusBadge.Text = "Recording";
            _statusBadge.FillColor = UiTheme.SuccessSoft;
            _statusBadge.BorderColor = UiTheme.SuccessSoft;
            _statusBadge.TextColor = UiTheme.Success;
        }
        else if (_lastCandidate is not null)
        {
            stateTitle = "Meeting detected";
            nextStep = "Press Start or use Pick window if you want manual control";
            narrative = $"Detected {_lastCandidate.Title} from {_lastCandidate.Platform}. You can start immediately, or change the capture target first if you want the full screen or a different window.";
            _statusBadge.Text = "Detected";
            _statusBadge.FillColor = UiTheme.AccentSoft;
            _statusBadge.BorderColor = UiTheme.AccentSoft;
            _statusBadge.TextColor = UiTheme.AccentStrong;
        }
        else
        {
            stateTitle = "Waiting for a meeting";
            nextStep = "Bring a meeting window forward or use Pick window";
            narrative = "Bring a Google Meet, Zoom, Teams, or Webex window to the front for a few seconds, or use Pick window to take control manually.";
            _statusBadge.Text = "Waiting";
            _statusBadge.FillColor = UiTheme.SurfaceTint;
            _statusBadge.BorderColor = UiTheme.SurfaceTint;
            _statusBadge.TextColor = UiTheme.TextMuted;
        }

        if (_recordingModeCombo.SelectedIndex == 1)
        {
            captureModeLabel = "Audio only - screen capture is skipped";
        }

        _stateTitleLabel.Text = stateTitle;
        _meetingValueLabel.Text = meetingLabel;
        _modeValueLabel.Text = recordingModeLabel;
        _captureValueLabel.Text = captureModeLabel;
        _nextStepValueLabel.Text = nextStep;

        var statusLines = new List<string>
        {
            narrative,
            string.Empty,
            $"Mode - {recordingModeLabel}",
            $"Capture - {captureModeLabel}"
        };

        if (_lastCandidate is not null)
        {
            statusLines.Add($"Source - {_lastCandidate.Platform} via {_lastCandidate.ProcessName}");
        }

        if (_lastActiveSessionTitle is not null)
        {
            statusLines.Add($"Active session - {_lastActiveSessionTitle}");
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

        _footerLabel.Text = _selectedSection switch
        {
            WidgetSection.History => "Search the saved meeting library, then inspect evidence or ask targeted questions.",
            WidgetSection.ActionCenter => "Review extracted tasks, update status, and use generated follow-up without leaving the dashboard.",
            _ => "Resize the window or drag the split bars to fit your meeting workflow."
        };

        ApplyNavSelection();
    }

    private void ApplyNavSelection()
    {
        ApplyNavTone(_navCaptionsButton, _selectedSection == WidgetSection.LiveCaptions);
        ApplyNavTone(_navHistoryButton, _selectedSection == WidgetSection.History);
        ApplyNavTone(_navActionsButton, _selectedSection == WidgetSection.ActionCenter);
    }

    private static void ApplyNavTone(UiButton button, bool selected)
    {
        button.Tone = selected ? UiButtonTone.Primary : UiButtonTone.Ghost;
        button.Invalidate();
    }

    private void ClampSplitters()
    {
        ApplyRootSplitConstraints();
        ApplyHeaderSplitConstraints();
        ApplyWorkspaceSplitConstraints();
    }

    private void ApplyRootSplitConstraints()
    {
        SetSafeSplitterDistance(_rootSplit, _savedRootSplitDistance, 220, 320);
    }

    private void ApplyHeaderSplitConstraints()
    {
        SetSafeSplitterDistance(_headerSplit, _savedHeaderSplitDistance, 320, 520);
    }

    private void ApplyWorkspaceSplitConstraints()
    {
        SetSafeSplitterDistance(_workspaceSplit, _savedWorkspaceSplitDistance, 180, 620);
    }

    private static void SetSafeSplitterDistance(SplitContainer splitContainer, int desiredDistance, int panel1Min, int panel2Min)
    {
        if (!splitContainer.IsHandleCreated)
        {
            return;
        }

        splitContainer.Panel1MinSize = panel1Min;
        splitContainer.Panel2MinSize = panel2Min;

        var total = splitContainer.Orientation == Orientation.Horizontal
            ? splitContainer.ClientSize.Height
            : splitContainer.ClientSize.Width;

        if (total <= splitContainer.SplitterWidth + panel1Min + panel2Min)
        {
            return;
        }

        var max = total - panel2Min - splitContainer.SplitterWidth;
        var safeDistance = Math.Max(panel1Min, Math.Min(max, desiredDistance));
        splitContainer.SplitterDistance = safeDistance;
    }

    private Panel CreateTopBar()
    {
        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(UiTheme.TitleFont.FontFamily, 16F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = UiTheme.TextPrimary,
            Text = "Meeting Recorder workspace",
            Location = new Point(0, 0)
        };

        var subtitleLabel = new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFont,
            ForeColor = UiTheme.TextMuted,
            Text = "Capture meetings, keep captions visible, then move straight into search and follow-up.",
            Location = new Point(0, 26)
        };

        var titlePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Height = 54
        };
        titlePanel.Controls.Add(subtitleLabel);
        titlePanel.Controls.Add(titleLabel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 62,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(2, 0, 2, 12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(titlePanel, 0, 0);
        layout.Controls.Add(_statusBadge, 1, 0);
        return layout;
    }

    private static Label CreateStatusKeyLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Font = UiTheme.BodyFontSemibold,
            ForeColor = UiTheme.TextMuted,
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
            MaximumSize = new Size(480, 0)
        };
    }

    private static Label CreateGridLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = UiTheme.BodyFontSemibold,
            ForeColor = UiTheme.TextMuted,
            TextAlign = ContentAlignment.BottomLeft,
            Text = text
        };
    }

    private static Panel WrapButtonCell(Control control)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(6, 0, 6, 0),
            Margin = Padding.Empty
        };

        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control);
        return panel;
    }

    private static UiButton CreateButton(string text, UiButtonTone tone, int width)
    {
        return new UiButton
        {
            Text = text,
            Tone = tone,
            Height = 40,
            Width = width,
            Margin = Padding.Empty
        };
    }

    private static UiButton CreateNavButton(string title, string description)
    {
        return new UiButton
        {
            Text = title,
            Tone = UiButtonTone.Ghost,
            Width = 180,
            Height = 46,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0, 0, 0, 10)
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
            Padding = new Padding(18)
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

    private void BringWindowToFront()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        if (!Visible)
        {
            Show();
        }

        BringToFront();
        Activate();
    }
}
