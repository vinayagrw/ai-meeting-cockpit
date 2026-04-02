using System.Diagnostics;
using System.Text.Json;
using MeetingRecorder.Shared.Configuration;
using MeetingRecorder.Windows.Interop;

namespace MeetingRecorder.Windows.Shell;

public sealed class ActionItemsForm : Form
{
    private static int _savedMainSplitDistance = 340;
    private static int _savedRightSplitDistance = 280;

    private readonly CompanionCliClient _cliClient;
    private readonly MeetingsRepository _repository;
    private readonly Action<string> _reprocessSession;
    private readonly Button _copyFollowUpButton;
    private readonly Button _generateOpenCommitmentsButton;
    private readonly Button _generatePrepButton;
    private readonly Button _loadRecurringSpeakersButton;
    private readonly Button _generateTimelineButton;
    private readonly ListView _itemsView;
    private readonly ListView _commitmentsView;
    private readonly ListView _openCommitmentsView;
    private readonly ListView _recurringSpeakersView;
    private readonly Button _openFolderButton;
    private readonly ListBox _sessionList;
    private readonly Label _statusLabel;
    private readonly TextBox _followUpBox;
    private readonly TextBox _prepBox;
    private readonly TextBox _recurringSpeakersSummaryBox;
    private readonly TextBox _timelineBox;
    private readonly TextBox _timelineQueryBox;
    private readonly SplitContainer _mainSplit;
    private readonly SplitContainer _rightSplit;
    private readonly UiTabControl _detailTabs;
    private readonly UiBadgeLabel _sessionBadge;
    private readonly bool _embedded;
    private readonly RecorderWindowsDashboardSettings _dashboard = RecorderSettingsProvider.Current.Windows.Dashboard;

    private bool _suppressItemEvents;

    public ActionItemsForm(MeetingsRepository repository, CompanionCliClient cliClient, Action<string> reprocessSession, bool embedded = false)
    {
        _embedded = embedded;
        _repository = repository;
        _cliClient = cliClient;
        _reprocessSession = reprocessSession;

        UiTheme.ApplyForm(this);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = _embedded ? FormBorderStyle.None : FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = _embedded ? new Size(320, 260) : new Size(1040, 700);
        Width = _embedded ? 980 : 1220;
        Height = _embedded ? 680 : 800;
        Text = "Action Center";
        Icon = AppIconProvider.Icon;

        _sessionBadge = new UiBadgeLabel
        {
            Text = "0 meetings",
            Width = 140,
            Height = 34,
            FillColor = UiTheme.WarningSoft,
            BorderColor = UiTheme.WarningSoft,
            TextColor = UiTheme.Warning
        };

        var headerPanel = UiTheme.CreateHeaderPanel(
            "Follow-through workspace",
            "Turn meeting outputs into action items and reusable follow-up copy",
            "Review generated tasks, mark work complete, reopen the meeting folder, or reprocess a session when you want fresh outputs.",
            _sessionBadge);

        var refreshButton = CreateButton("Refresh", UiButtonTone.Secondary, 104);
        refreshButton.Click += (_, _) => RefreshSessions();

        var reprocessButton = CreateButton("Reprocess", UiButtonTone.Primary, 112);
        reprocessButton.Click += (_, _) => ReprocessSelectedSession();

        _openFolderButton = CreateButton("Open folder", UiButtonTone.Ghost, 116);
        _openFolderButton.Click += (_, _) => OpenSelectedFolder();

        _copyFollowUpButton = CreateButton("Copy follow-up", UiButtonTone.Secondary, 132);
        _copyFollowUpButton.Click += (_, _) => CopyFollowUp();

        _generateOpenCommitmentsButton = CreateButton("Open commitments", UiButtonTone.Secondary, 142);
        _generateOpenCommitmentsButton.Click += async (_, _) => await LoadOpenCommitmentsAsync();

        _generatePrepButton = CreateButton("Prep brief", UiButtonTone.Ghost, 112);
        _generatePrepButton.Click += async (_, _) => await GeneratePrepBriefAsync();

        _loadRecurringSpeakersButton = CreateButton("Recurring speakers", UiButtonTone.Ghost, 146);
        _loadRecurringSpeakersButton.Click += (_, _) => LoadRecurringSpeakers();

        _generateTimelineButton = CreateButton("Timeline", UiButtonTone.Ghost, 104);
        _generateTimelineButton.Click += async (_, _) => await GenerateTimelineAsync();

        var toolbarRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            AutoScroll = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        toolbarRow.Controls.Add(refreshButton);
        toolbarRow.Controls.Add(reprocessButton);
        toolbarRow.Controls.Add(_openFolderButton);
        toolbarRow.Controls.Add(_copyFollowUpButton);
        toolbarRow.Controls.Add(_generateOpenCommitmentsButton);
        toolbarRow.Controls.Add(_generatePrepButton);
        toolbarRow.Controls.Add(_loadRecurringSpeakersButton);
        toolbarRow.Controls.Add(_generateTimelineButton);

        var toolbarBody = new TableLayoutPanel
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
        toolbarBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        toolbarBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        toolbarBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        toolbarBody.Controls.Add(
            UiTheme.CreateSectionTitle(
                "Action workflow",
                _embedded
                    ? "Refresh, reprocess, or copy the next follow-up from one place."
                    : "Refresh the list, reprocess a session, copy follow-up text, or jump to the underlying meeting folder."),
            0,
            0);
        toolbarBody.Controls.Add(toolbarRow, 0, 1);

        var toolbarCard = new UiCardPanel
        {
            Dock = DockStyle.Top,
            AccentColor = UiTheme.Warning,
            Padding = _embedded ? new Padding(14, 12, 14, 12) : new Padding(18, 16, 18, 14),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ShowAccent = !_embedded
        };
        toolbarCard.Controls.Add(toolbarBody);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.TextMuted,
            Font = UiTheme.BodyFont,
            Text = _embedded
                ? "Select a processed meeting to review tasks and follow-up."
                : "Open a completed meeting to review action items and follow-up."
        };

        _sessionList = new ListBox
        {
            Dock = DockStyle.Fill,
            DisplayMember = nameof(SavedMeetingSession.Title)
        };
        UiTheme.StyleListBox(_sessionList);
        _sessionList.SelectedIndexChanged += (_, _) => LoadSelectedSession();

        var sessionsCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            ShowAccent = !_embedded
        };
        sessionsCard.Controls.Add(_sessionList);
        sessionsCard.Controls.Add(UiTheme.CreateSectionTitle(
            "Meetings",
            _embedded
                ? "Select a processed meeting."
                : "Select a processed meeting to inspect generated tasks and the draft you can send out."));

        _itemsView = new ListView
        {
            Dock = DockStyle.Fill,
            CheckBoxes = true,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            View = View.Details
        };
        UiTheme.StyleListView(_itemsView);
        _itemsView.Columns.Add("Task", 360);
        _itemsView.Columns.Add("Owner", 120);
        _itemsView.Columns.Add("Due", 110);
        _itemsView.Columns.Add("Status", 90);
        _itemsView.Columns.Add("Source", 130);
        _itemsView.ItemChecked += (_, eventArgs) => UpdateActionItemState(eventArgs.Item);

        var itemsCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Success,
            ShowAccent = !_embedded
        };
        itemsCard.Controls.Add(_itemsView);
        itemsCard.Controls.Add(UiTheme.CreateSectionTitle(
            "Action items",
            _embedded
                ? "Track extracted tasks and completion state."
                : "Check items off as they complete, then keep the session folder and generated assets close at hand."));

        _followUpBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false
        };
        UiTheme.StyleTextBox(_followUpBox, readOnly: true);

        var followUpCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            ShowAccent = !_embedded
        };
        followUpCard.Controls.Add(_followUpBox);
        followUpCard.Controls.Add(UiTheme.CreateSectionTitle(
            "Follow-up draft",
            _embedded
                ? "Review or copy the generated recap."
                : "Copy or refine the generated meeting recap after you review the extracted tasks."));

        _commitmentsView = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            View = View.Details
        };
        UiTheme.StyleListView(_commitmentsView);
        _commitmentsView.Columns.Add("Commitment", 320);
        _commitmentsView.Columns.Add("Owner", 120);
        _commitmentsView.Columns.Add("Due", 110);
        _commitmentsView.Columns.Add("Status", 90);

        var commitmentsCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Warning,
            ShowAccent = !_embedded
        };
        commitmentsCard.Controls.Add(_commitmentsView);
        commitmentsCard.Controls.Add(UiTheme.CreateSectionTitle(
            "Commitments",
            _embedded
                ? "Promises and owners extracted from this meeting."
                : "Review the explicit commitments and owners extracted from this meeting."));

        _openCommitmentsView = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            View = View.Details
        };
        UiTheme.StyleListView(_openCommitmentsView);
        _openCommitmentsView.Columns.Add("Owner", 120);
        _openCommitmentsView.Columns.Add("Commitment", 320);
        _openCommitmentsView.Columns.Add("Due", 110);
        _openCommitmentsView.Columns.Add("State", 90);
        _openCommitmentsView.Columns.Add("Meeting date", 110);
        _openCommitmentsView.Columns.Add("Session", 180);

        var openCommitmentsCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Warning,
            ShowAccent = !_embedded
        };
        openCommitmentsCard.Controls.Add(_openCommitmentsView);
        openCommitmentsCard.Controls.Add(UiTheme.CreateSectionTitle(
            "Cross-meeting commitments",
            _embedded
                ? "Open commitments across saved meetings."
                : "Track open commitments across saved meetings and use meeting dates to spot overdue follow-through."));

        _prepBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false
        };
        UiTheme.StyleTextBox(_prepBox, readOnly: true);

        var prepCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Success,
            ShowAccent = !_embedded
        };
        prepCard.Controls.Add(_prepBox);
        prepCard.Controls.Add(UiTheme.CreateSectionTitle(
            "Prep brief",
            _embedded
                ? "Generate a brief for the next related meeting."
                : "Generate a prep brief from related meetings before the next session."));

        _timelineQueryBox = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = "Topic for timeline search"
        };
        UiTheme.StyleTextBox(_timelineQueryBox);

        _timelineBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false
        };
        UiTheme.StyleTextBox(_timelineBox, readOnly: true);

        var timelineBody = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };
        timelineBody.Controls.Add(_timelineBox);
        timelineBody.Controls.Add(_timelineQueryBox);

        var timelineCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            ShowAccent = !_embedded
        };
        timelineCard.Controls.Add(timelineBody);
        timelineCard.Controls.Add(UiTheme.CreateSectionTitle(
            "Topic timeline",
            _embedded
                ? "Track a topic across saved meetings."
                : "Track how a topic evolved across saved meetings."));

        _recurringSpeakersView = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            View = View.Details
        };
        UiTheme.StyleListView(_recurringSpeakersView);
        _recurringSpeakersView.Columns.Add("Speaker", 180);
        _recurringSpeakersView.Columns.Add("Sessions", 90);
        _recurringSpeakersView.Columns.Add("Platforms", 150);
        _recurringSpeakersView.Columns.Add("Example meetings", 320);

        _recurringSpeakersSummaryBox = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 72,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Text = "Load recurring speakers to review participants who show up across saved meetings."
        };
        UiTheme.StyleTextBox(_recurringSpeakersSummaryBox, readOnly: true);

        var recurringSpeakersBody = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };
        recurringSpeakersBody.Controls.Add(_recurringSpeakersView);
        recurringSpeakersBody.Controls.Add(_recurringSpeakersSummaryBox);

        var recurringSpeakersCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            ShowAccent = !_embedded
        };
        recurringSpeakersCard.Controls.Add(recurringSpeakersBody);
        recurringSpeakersCard.Controls.Add(UiTheme.CreateSectionTitle(
            "Recurring speakers",
            _embedded
                ? "Repeated participant names across meetings."
                : "Track recurring named speakers across sessions. This is name-based today, not a voice-embedding identity model."));

        _detailTabs = new UiTabControl
        {
            Dock = DockStyle.Fill
        };
        _detailTabs.TabPages.Add(new TabPage("Follow-up") { Padding = new Padding(8) });
        _detailTabs.TabPages.Add(new TabPage("Commitments") { Padding = new Padding(8) });
        _detailTabs.TabPages.Add(new TabPage("Open commitments") { Padding = new Padding(8) });
        _detailTabs.TabPages.Add(new TabPage("Prep") { Padding = new Padding(8) });
        _detailTabs.TabPages.Add(new TabPage("Timeline") { Padding = new Padding(8) });
        _detailTabs.TabPages.Add(new TabPage("Recurring speakers") { Padding = new Padding(8) });
        _detailTabs.TabPages[0].Controls.Add(followUpCard);
        _detailTabs.TabPages[1].Controls.Add(commitmentsCard);
        _detailTabs.TabPages[2].Controls.Add(openCommitmentsCard);
        _detailTabs.TabPages[3].Controls.Add(prepCard);
        _detailTabs.TabPages[4].Controls.Add(timelineCard);
        _detailTabs.TabPages[5].Controls.Add(recurringSpeakersCard);

        _rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 10
        };
        UiTheme.StyleSplitContainer(_rightSplit);
        _rightSplit.Panel1.Controls.Add(itemsCard);
        _rightSplit.Panel2.Controls.Add(_detailTabs);
        _rightSplit.SplitterMoved += (_, _) => _savedRightSplitDistance = _rightSplit.SplitterDistance;

        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = 10
        };
        UiTheme.StyleSplitContainer(_mainSplit);
        _mainSplit.Panel1.Controls.Add(sessionsCard);
        _mainSplit.Panel2.Controls.Add(_rightSplit);
        _mainSplit.SplitterMoved += (_, _) => _savedMainSplitDistance = _mainSplit.SplitterDistance;

        var statusStrip = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = _embedded ? 28 : 34,
            BackColor = Color.Transparent,
            Padding = new Padding(8, 10, 8, 0)
        };
        statusStrip.Controls.Add(_statusLabel);

        var chromePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.AppBackground,
            Padding = _embedded ? new Padding(8) : new Padding(14)
        };
        chromePanel.Controls.Add(_mainSplit);
        chromePanel.Controls.Add(statusStrip);
        chromePanel.Controls.Add(toolbarCard);
        if (!_embedded)
        {
            chromePanel.Controls.Add(headerPanel);
        }
        Controls.Add(chromePanel);

        Load += (_, _) => ClampSplitters();
        Shown += (_, _) => ClampSplitters();
        Resize += (_, _) =>
        {
            ClampSplitters();
            AdjustItemColumns();
        };

        RefreshSessions();
    }

    public void RefreshSessions()
    {
        var sessions = _repository.GetRecentSessions(100)
            .Where(session => session.HasActionItems || session.HasFollowUp || session.HasCommitments)
            .ToList();

        _sessionList.BeginUpdate();
        _sessionList.Items.Clear();
        foreach (var session in sessions)
        {
            _sessionList.Items.Add(session);
        }
        _sessionList.EndUpdate();

        UpdateSessionBadge(sessions.Count);
        AdjustItemColumns();

        if (_sessionList.Items.Count > 0)
        {
            _sessionList.SelectedIndex = 0;
        }
        else
        {
            _itemsView.Items.Clear();
            _commitmentsView.Items.Clear();
            _openCommitmentsView.Items.Clear();
            _recurringSpeakersView.Items.Clear();
            _followUpBox.Text = "No action-item or follow-up files have been generated yet.";
            _prepBox.Text = "No meeting selected.";
            _recurringSpeakersSummaryBox.Text = "No recurring speaker data loaded yet.";
            _timelineBox.Text = "No meeting selected.";
        }

        _statusLabel.Text = sessions.Count == 0
            ? "No action items, commitments, or follow-up drafts found yet."
            : $"Loaded {sessions.Count} meetings with action items, commitments, or follow-up drafts.";

        LoadRecurringSpeakers();
        _ = LoadOpenCommitmentsAsync();
    }

    private void LoadSelectedSession()
    {
        var session = GetSelectedSession();
        if (session is null)
        {
            return;
        }

        var actionItems = _repository.LoadActionItems(session);
        _suppressItemEvents = true;
        _itemsView.BeginUpdate();
        _itemsView.Items.Clear();
        foreach (var item in actionItems.Items)
        {
            var listViewItem = new ListViewItem(item.Task)
            {
                Checked = string.Equals(item.Status, "done", StringComparison.OrdinalIgnoreCase),
                Tag = item
            };
            listViewItem.SubItems.Add(item.Owner ?? string.Empty);
            listViewItem.SubItems.Add(item.DueDate ?? string.Empty);
            listViewItem.SubItems.Add(item.Status);
            listViewItem.SubItems.Add(item.Source ?? string.Empty);
            _itemsView.Items.Add(listViewItem);
        }
        _itemsView.EndUpdate();
        _suppressItemEvents = false;

        var commitments = _repository.LoadCommitments(session);
        _commitmentsView.BeginUpdate();
        _commitmentsView.Items.Clear();
        foreach (var item in commitments.Items)
        {
            var listViewItem = new ListViewItem(item.Commitment);
            listViewItem.SubItems.Add(item.Owner ?? string.Empty);
            listViewItem.SubItems.Add(item.DueDate ?? string.Empty);
            listViewItem.SubItems.Add(item.Status);
            _commitmentsView.Items.Add(listViewItem);
        }
        _commitmentsView.EndUpdate();

        _followUpBox.Text = _repository.LoadFollowUp(session);
        _prepBox.Text = $"Click \"Prep brief\" to generate a prep brief for {session.Title}.";
        _timelineQueryBox.Text = string.IsNullOrWhiteSpace(_timelineQueryBox.Text) ? session.Title : _timelineQueryBox.Text;
        _timelineBox.Text = "Enter a topic or leave the current title, then click \"Timeline\".";
        _statusLabel.Text = $"Loaded action center for {session.Title}.";
    }

    private async Task LoadOpenCommitmentsAsync()
    {
        _generateOpenCommitmentsButton.Enabled = false;
        try
        {
            var raw = await _cliClient.GetOpenCommitmentsAsync();
            PopulateOpenCommitments(raw);
            _statusLabel.Text = "Loaded cross-meeting commitments.";
        }
        catch (Exception error)
        {
            _openCommitmentsView.Items.Clear();
            _statusLabel.Text = $"Unable to load open commitments: {error.Message}";
        }
        finally
        {
            _generateOpenCommitmentsButton.Enabled = true;
        }
    }

    private void PopulateOpenCommitments(string raw)
    {
        _openCommitmentsView.BeginUpdate();
        _openCommitmentsView.Items.Clear();

        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.TryGetProperty("commitments", out var commitmentsElement) &&
                    commitmentsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in commitmentsElement.EnumerateArray())
                    {
                        var owner = GetString(item, "owner") ?? "Unassigned";
                        var commitment = GetString(item, "commitment") ?? string.Empty;
                        var dueDate = GetString(item, "dueDate") ?? string.Empty;
                        var state = ResolveCommitmentState(dueDate, GetString(item, "status"));
                        var meetingDate = GetString(item, "meetingDate") ?? string.Empty;
                        var sessionDir = GetString(item, "sessionDir") ?? string.Empty;
                        var sessionName = string.IsNullOrWhiteSpace(sessionDir) ? string.Empty : Path.GetFileName(sessionDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                        var listViewItem = new ListViewItem(owner);
                        listViewItem.SubItems.Add(commitment);
                        listViewItem.SubItems.Add(dueDate);
                        listViewItem.SubItems.Add(state);
                        listViewItem.SubItems.Add(meetingDate);
                        listViewItem.SubItems.Add(sessionName);
                        _openCommitmentsView.Items.Add(listViewItem);
                    }
                }
            }
            catch
            {
                _openCommitmentsView.Items.Clear();
            }
        }

        _openCommitmentsView.EndUpdate();
        AdjustItemColumns();
    }

    private void LoadRecurringSpeakers()
    {
        var speakers = _repository.GetRecurringSpeakers();
        _recurringSpeakersView.BeginUpdate();
        _recurringSpeakersView.Items.Clear();
        foreach (var speaker in speakers)
        {
            var item = new ListViewItem(speaker.Name);
            item.SubItems.Add(speaker.SessionCount.ToString());
            item.SubItems.Add(string.Join(", ", speaker.Platforms));
            item.SubItems.Add(string.Join("; ", speaker.ExampleMeetings));
            _recurringSpeakersView.Items.Add(item);
        }
        _recurringSpeakersView.EndUpdate();

        _recurringSpeakersSummaryBox.Text = speakers.Count == 0
            ? "No recurring speaker names have been detected yet. Named participants or diarized speaker labels need to appear in saved sessions first."
            : $"Found {speakers.Count} recurring speakers across saved sessions. This view is name-based today and helps surface repeated participants while voice-embedding identity work is still pending.";
        AdjustItemColumns();
    }

    private void UpdateActionItemState(ListViewItem item)
    {
        if (_suppressItemEvents)
        {
            return;
        }

        var session = GetSelectedSession();
        if (session is null)
        {
            return;
        }

        var current = _repository.LoadActionItems(session);
        var updated = current.Items
            .Select(existing =>
            {
                if (!string.Equals(existing.Task, item.Text, StringComparison.Ordinal))
                {
                    return existing;
                }

                return existing with
                {
                    Status = item.Checked ? "done" : "pending"
                };
            })
            .ToList();

        _repository.SaveActionItems(new SessionActionItems(current.Path, updated));
        item.SubItems[3].Text = item.Checked ? "done" : "pending";
        _statusLabel.Text = $"Updated action item state for {session.Title}.";
    }

    private void ReprocessSelectedSession()
    {
        var session = GetSelectedSession();
        if (session is null)
        {
            return;
        }

        _reprocessSession(session.SessionDirectory);
        _statusLabel.Text = $"Reprocessing launched for {session.Title}.";
    }

    private void CopyFollowUp()
    {
        if (string.IsNullOrWhiteSpace(_followUpBox.Text))
        {
            return;
        }

        Clipboard.SetText(_followUpBox.Text);
        _statusLabel.Text = "Follow-up draft copied to the clipboard.";
    }

    private void OpenSelectedFolder()
    {
        var session = GetSelectedSession();
        if (session is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = session.SessionDirectory,
            UseShellExecute = true
        });
    }

    private SavedMeetingSession? GetSelectedSession() => _sessionList.SelectedItem as SavedMeetingSession;

    private async Task GeneratePrepBriefAsync()
    {
        var session = GetSelectedSession();
        if (session is null)
        {
            _statusLabel.Text = "Select a meeting first.";
            return;
        }

        _generatePrepButton.Enabled = false;
        _prepBox.Text = "Generating prep brief...";
        _detailTabs.SelectedIndex = 3;
        try
        {
            var result = await _cliClient.GetMeetingPrepAsync(session.Title);
            _prepBox.Text = string.IsNullOrWhiteSpace(result) ? "No prep brief could be generated." : result;
            _statusLabel.Text = $"Prep brief generated for {session.Title}.";
        }
        catch (Exception error)
        {
            _prepBox.Text = error.Message;
            _statusLabel.Text = "Prep brief generation failed.";
        }
        finally
        {
            _generatePrepButton.Enabled = true;
        }
    }

    private async Task GenerateTimelineAsync()
    {
        var session = GetSelectedSession();
        var query = _timelineQueryBox.Text.Trim();
        if (session is null)
        {
            _statusLabel.Text = "Select a meeting first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            query = session.Title;
            _timelineQueryBox.Text = query;
        }

        _generateTimelineButton.Enabled = false;
        _timelineBox.Text = "Building topic timeline...";
        _detailTabs.SelectedIndex = 4;
        try
        {
            var result = await _cliClient.GetTopicTimelineAsync(query);
            _timelineBox.Text = string.IsNullOrWhiteSpace(result) ? "No topic timeline could be generated." : result;
            _statusLabel.Text = $"Topic timeline generated for \"{query}\".";
        }
        catch (Exception error)
        {
            _timelineBox.Text = error.Message;
            _statusLabel.Text = "Topic timeline generation failed.";
        }
        finally
        {
            _generateTimelineButton.Enabled = true;
        }
    }

    private void ClampSplitters()
    {
        if (_mainSplit.Width > 0)
        {
            ApplyMainSplitConstraints();
            var maxMain = Math.Max(_mainSplit.Panel1MinSize, _mainSplit.Width - _mainSplit.Panel2MinSize - _mainSplit.SplitterWidth);
            _mainSplit.SplitterDistance = Math.Max(_mainSplit.Panel1MinSize, Math.Min(maxMain, _savedMainSplitDistance));
            _savedMainSplitDistance = _mainSplit.SplitterDistance;
        }

        if (_rightSplit.Height > 0)
        {
            ApplyRightSplitConstraints();
            var maxRight = Math.Max(_rightSplit.Panel1MinSize, _rightSplit.Height - _rightSplit.Panel2MinSize - _rightSplit.SplitterWidth);
            _rightSplit.SplitterDistance = Math.Max(_rightSplit.Panel1MinSize, Math.Min(maxRight, _savedRightSplitDistance));
            _savedRightSplitDistance = _rightSplit.SplitterDistance;
        }
    }

    private void ApplyMainSplitConstraints()
    {
        var available = Math.Max(0, _mainSplit.Width - _mainSplit.SplitterWidth);
        if (available <= 0)
        {
            return;
        }

        var leftMin = Math.Min(_dashboard.WorkspaceListMinWidth, Math.Max(180, available - _dashboard.WorkspaceDetailMinWidth));
        var rightMin = Math.Min(_dashboard.WorkspaceDetailMinWidth, Math.Max(220, available - leftMin));
        if (leftMin + rightMin > available)
        {
            leftMin = Math.Max(180, available / 2);
            rightMin = Math.Max(180, available - leftMin);
        }

        _mainSplit.Panel1MinSize = Math.Max(0, Math.Min(leftMin, Math.Max(0, available)));
        _mainSplit.Panel2MinSize = Math.Max(0, Math.Min(rightMin, Math.Max(0, available - _mainSplit.Panel1MinSize)));
    }

    private void ApplyRightSplitConstraints()
    {
        var available = Math.Max(0, _rightSplit.Height - _rightSplit.SplitterWidth);
        if (available <= 0)
        {
            return;
        }

        var topMin = Math.Min(230, Math.Max(150, available - _dashboard.WorkspaceBottomPaneMinHeight));
        var bottomMin = Math.Min(_dashboard.WorkspaceBottomPaneMinHeight, Math.Max(140, available - topMin));
        if (topMin + bottomMin > available)
        {
            topMin = Math.Max(140, available / 2);
            bottomMin = Math.Max(140, available - topMin);
        }

        _rightSplit.Panel1MinSize = Math.Max(0, Math.Min(topMin, Math.Max(0, available)));
        _rightSplit.Panel2MinSize = Math.Max(0, Math.Min(bottomMin, Math.Max(0, available - _rightSplit.Panel1MinSize)));
    }

    private void UpdateSessionBadge(int count)
    {
        _sessionBadge.Text = count == 1 ? "1 meeting" : $"{count} meetings";
        _sessionBadge.Invalidate();
    }

    private void AdjustItemColumns()
    {
        if (_itemsView.Columns.Count < 5 || _itemsView.ClientSize.Width <= 0)
        {
            if (_commitmentsView.Columns.Count < 4 || _commitmentsView.ClientSize.Width <= 0)
            {
                return;
            }
        }

        if (_itemsView.Columns.Count >= 5 && _itemsView.ClientSize.Width > 0)
        {
            var available = Math.Max(420, _itemsView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
            var taskWidth = Math.Max(180, (int)(available * 0.42));
            var ownerWidth = Math.Max(88, (int)(available * 0.15));
            var dueWidth = Math.Max(82, (int)(available * 0.13));
            var statusWidth = Math.Max(82, (int)(available * 0.12));
            var sourceWidth = Math.Max(92, available - taskWidth - ownerWidth - dueWidth - statusWidth);

            _itemsView.Columns[0].Width = taskWidth;
            _itemsView.Columns[1].Width = ownerWidth;
            _itemsView.Columns[2].Width = dueWidth;
            _itemsView.Columns[3].Width = statusWidth;
            _itemsView.Columns[4].Width = sourceWidth;
        }

        if (_commitmentsView.Columns.Count >= 4 && _commitmentsView.ClientSize.Width > 0)
        {
            var available = Math.Max(360, _commitmentsView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
            var commitmentWidth = Math.Max(180, (int)(available * 0.52));
            var ownerWidth = Math.Max(88, (int)(available * 0.18));
            var dueWidth = Math.Max(82, (int)(available * 0.15));
            var statusWidth = Math.Max(82, available - commitmentWidth - ownerWidth - dueWidth);

            _commitmentsView.Columns[0].Width = commitmentWidth;
            _commitmentsView.Columns[1].Width = ownerWidth;
            _commitmentsView.Columns[2].Width = dueWidth;
            _commitmentsView.Columns[3].Width = statusWidth;
        }

        if (_openCommitmentsView.Columns.Count >= 6 && _openCommitmentsView.ClientSize.Width > 0)
        {
            var available = Math.Max(420, _openCommitmentsView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
            var ownerWidth = Math.Max(96, (int)(available * 0.14));
            var commitmentWidth = Math.Max(170, (int)(available * 0.33));
            var dueWidth = Math.Max(88, (int)(available * 0.12));
            var stateWidth = Math.Max(82, (int)(available * 0.1));
            var meetingDateWidth = Math.Max(110, (int)(available * 0.14));
            var sessionWidth = Math.Max(110, available - ownerWidth - commitmentWidth - dueWidth - stateWidth - meetingDateWidth);

            _openCommitmentsView.Columns[0].Width = ownerWidth;
            _openCommitmentsView.Columns[1].Width = commitmentWidth;
            _openCommitmentsView.Columns[2].Width = dueWidth;
            _openCommitmentsView.Columns[3].Width = stateWidth;
            _openCommitmentsView.Columns[4].Width = meetingDateWidth;
            _openCommitmentsView.Columns[5].Width = sessionWidth;
        }

        if (_recurringSpeakersView.Columns.Count >= 4 && _recurringSpeakersView.ClientSize.Width > 0)
        {
            var available = Math.Max(420, _recurringSpeakersView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
            var speakerWidth = Math.Max(120, (int)(available * 0.22));
            var sessionsWidth = Math.Max(82, (int)(available * 0.12));
            var platformsWidth = Math.Max(120, (int)(available * 0.2));
            var meetingsWidth = Math.Max(140, available - speakerWidth - sessionsWidth - platformsWidth);

            _recurringSpeakersView.Columns[0].Width = speakerWidth;
            _recurringSpeakersView.Columns[1].Width = sessionsWidth;
            _recurringSpeakersView.Columns[2].Width = platformsWidth;
            _recurringSpeakersView.Columns[3].Width = meetingsWidth;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) ? value.GetString() : null;

    private static string ResolveCommitmentState(string? dueDate, string? status)
    {
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "open", StringComparison.OrdinalIgnoreCase))
        {
            return status!;
        }

        if (DateTime.TryParse(dueDate, out var due) && due.Date < DateTime.Today)
        {
            return "overdue";
        }

        return "open";
    }

    private static Button CreateButton(string text, UiButtonTone tone, int width)
        => new UiButton
        {
            Text = text,
            Tone = tone,
            Width = width,
            Height = 38,
            Margin = new Padding(0, 0, 10, 0)
        };
}
