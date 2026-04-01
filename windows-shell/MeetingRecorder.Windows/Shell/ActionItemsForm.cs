using System.Diagnostics;
using MeetingRecorder.Shared.Configuration;

namespace MeetingRecorder.Windows.Shell;

public sealed class ActionItemsForm : Form
{
    private static int _savedMainSplitDistance = 340;
    private static int _savedRightSplitDistance = 280;

    private readonly MeetingsRepository _repository;
    private readonly Action<string> _reprocessSession;
    private readonly Button _copyFollowUpButton;
    private readonly ListView _itemsView;
    private readonly Button _openFolderButton;
    private readonly ListBox _sessionList;
    private readonly Label _statusLabel;
    private readonly TextBox _followUpBox;
    private readonly SplitContainer _mainSplit;
    private readonly SplitContainer _rightSplit;
    private readonly UiBadgeLabel _sessionBadge;
    private readonly bool _embedded;
    private readonly RecorderWindowsDashboardSettings _dashboard = RecorderSettingsProvider.Current.Windows.Dashboard;

    private bool _suppressItemEvents;

    public ActionItemsForm(MeetingsRepository repository, Action<string> reprocessSession, bool embedded = false)
    {
        _embedded = embedded;
        _repository = repository;
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

        _rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 10
        };
        UiTheme.StyleSplitContainer(_rightSplit);
        _rightSplit.Panel1.Controls.Add(itemsCard);
        _rightSplit.Panel2.Controls.Add(followUpCard);
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
            .Where(session => session.HasActionItems || session.HasFollowUp)
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
            _followUpBox.Text = "No action-item or follow-up files have been generated yet.";
        }

        _statusLabel.Text = sessions.Count == 0
            ? "No action items found yet."
            : $"Loaded {sessions.Count} meetings with action items or follow-up drafts.";
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

        _followUpBox.Text = _repository.LoadFollowUp(session);
        _statusLabel.Text = $"Loaded action center for {session.Title}.";
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
            return;
        }

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
