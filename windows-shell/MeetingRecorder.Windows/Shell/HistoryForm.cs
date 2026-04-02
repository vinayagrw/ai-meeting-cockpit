using System.Diagnostics;
using MeetingRecorder.Shared.Configuration;
using MeetingRecorder.Windows.Interop;

namespace MeetingRecorder.Windows.Shell;

public sealed class HistoryForm : Form
{
    private static int _savedMainSplitDistance = 440;
    private static int _savedDetailSplitDistance = 340;

    private readonly CompanionCliClient _cliClient;
    private readonly MeetingsRepository _repository;
    private readonly Button _askButton;
    private readonly TextBox _answerBox;
    private readonly SplitContainer _detailSplit;
    private readonly SplitContainer _mainSplit;
    private readonly TextBox _insightsBox;
    private readonly Button _openFolderButton;
    private readonly TextBox _previewBox;
    private readonly UiTabControl _previewTabs;
    private readonly TextBox _queryBox;
    private readonly ListView _resultsView;
    private readonly CheckBox _semanticSearchCheckBox;
    private readonly Label _statusLabel;
    private readonly TextBox _questionBox;
    private readonly UiBadgeLabel _resultBadge;
    private readonly bool _embedded;
    private readonly RecorderWindowsDashboardSettings _dashboard = RecorderSettingsProvider.Current.Windows.Dashboard;

    public HistoryForm(MeetingsRepository repository, CompanionCliClient cliClient, bool embedded = false)
    {
        _embedded = embedded;
        _repository = repository;
        _cliClient = cliClient;

        UiTheme.ApplyForm(this);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = _embedded ? FormBorderStyle.None : FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = _embedded ? new Size(320, 260) : new Size(980, 660);
        Width = _embedded ? 960 : 1180;
        Height = _embedded ? 640 : 760;
        Text = "Meeting History";
        Icon = AppIconProvider.Icon;

        _resultBadge = new UiBadgeLabel
        {
            Text = "0 meetings",
            Width = 140,
            Height = 34,
            FillColor = UiTheme.AccentSoft,
            BorderColor = UiTheme.AccentSoft,
            TextColor = UiTheme.AccentStrong
        };

        var headerPanel = UiTheme.CreateHeaderPanel(
            "Meeting memory",
            "Search completed sessions and inspect grounded evidence",
            "Filter by title, platform, transcript text, or summary content, then review the stored preview and ask targeted questions against the meeting context.",
            _resultBadge);

        _queryBox = new TextBox
        {
            Width = _embedded ? 240 : 320,
            PlaceholderText = "Search meetings, transcript text, or summaries"
        };
        UiTheme.StyleTextBox(_queryBox);
        _queryBox.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                eventArgs.SuppressKeyPress = true;
                await SearchAsync();
            }
        };

        var searchButton = CreateButton("Search", UiButtonTone.Primary, 110);
        searchButton.Click += async (_, _) => await SearchAsync();

        var recentButton = CreateButton("Recent", UiButtonTone.Secondary, 104);
        recentButton.Click += (_, _) => LoadRecent();

        var resetLayoutButton = CreateButton("Reset layout", UiButtonTone.Ghost, 118);
        resetLayoutButton.Click += (_, _) => ResetLayout();

        _openFolderButton = CreateButton("Open folder", UiButtonTone.Ghost, 116);
        _openFolderButton.Enabled = false;
        _openFolderButton.Click += (_, _) => OpenSelectedFolder();

        _semanticSearchCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "Semantic",
            Checked = true,
            BackColor = Color.Transparent,
            ForeColor = UiTheme.TextMuted,
            Font = UiTheme.BodyFont,
            Margin = new Padding(4, 10, 8, 0)
        };

        _statusLabel = new Label
        {
            AutoEllipsis = true,
            ForeColor = UiTheme.TextMuted,
            Font = UiTheme.BodyFont,
            Text = _embedded
                ? "Search, inspect, or ask from this workspace."
                : "Search your saved meeting history. Drag the split bars to resize the workspace."
        };

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
        toolbarRow.Controls.Add(_queryBox);
        toolbarRow.Controls.Add(searchButton);
        toolbarRow.Controls.Add(recentButton);
        toolbarRow.Controls.Add(_semanticSearchCheckBox);
        toolbarRow.Controls.Add(resetLayoutButton);
        toolbarRow.Controls.Add(_openFolderButton);

        var toolbarBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = _embedded ? 1 : 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        toolbarBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        toolbarBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (_embedded)
        {
            toolbarBody.Controls.Add(toolbarRow, 0, 0);
        }
        else
        {
            toolbarBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            toolbarBody.Controls.Add(
                UiTheme.CreateSectionTitle(
                    "Search and filter",
                    "Jump into saved meetings quickly, then open the folder or inspect the details in place."),
                0,
                0);
            toolbarBody.Controls.Add(toolbarRow, 0, 1);
        }

        var toolbarCard = new UiCardPanel
        {
            Dock = DockStyle.Top,
            AccentColor = UiTheme.Accent,
            Padding = _embedded ? new Padding(8, 8, 8, 6) : new Padding(18, 16, 18, 14),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ShowAccent = !_embedded
        };
        toolbarCard.Controls.Add(toolbarBody);

        _resultsView = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            View = View.Details
        };
        UiTheme.StyleListView(_resultsView);
        _resultsView.Columns.Add("Title", 240);
        _resultsView.Columns.Add("Platform", 90);
        _resultsView.Columns.Add("Started", 130);
        _resultsView.Columns.Add("Snippet", 360);
        _resultsView.SelectedIndexChanged += (_, _) => LoadSelectedPreview();

        var resultsCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Accent,
            ShowAccent = !_embedded,
            Padding = _embedded ? new Padding(6) : new Padding(16)
        };
        resultsCard.Controls.Add(_resultsView);
        if (_embedded)
        {
            resultsCard.Controls.Add(CreateEmbeddedPaneLabel("Saved meetings"));
        }
        else
        {
            resultsCard.Controls.Add(UiTheme.CreateSectionTitle(
                "Saved meetings",
                "Pick a processed session to inspect the preview, transcript evidence, and Q&A pane."));
        }

        _previewBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false
        };
        UiTheme.StyleTextBox(_previewBox, readOnly: true, code: false);

        _insightsBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false
        };
        UiTheme.StyleTextBox(_insightsBox, readOnly: true, code: false);

        _questionBox = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = "Ask this meeting a grounded question"
        };
        UiTheme.StyleTextBox(_questionBox);
        _questionBox.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter && !eventArgs.Shift)
            {
                eventArgs.SuppressKeyPress = true;
                await AskAsync();
            }
        };

        _askButton = CreateButton("Ask", UiButtonTone.Primary, 100);
        _askButton.Dock = DockStyle.Top;
        _askButton.Height = 38;
        _askButton.Enabled = false;
        _askButton.Margin = Padding.Empty;
        _askButton.Click += async (_, _) => await AskAsync();

        _answerBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false
        };
        UiTheme.StyleTextBox(_answerBox, readOnly: true);

        var previewCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Success,
            ShowAccent = !_embedded,
            Padding = _embedded ? new Padding(6) : new Padding(16)
        };
        previewCard.Controls.Add(_previewBox);
        if (_embedded)
        {
            previewCard.Controls.Add(CreateEmbeddedPaneLabel("Preview"));
        }
        else
        {
            previewCard.Controls.Add(UiTheme.CreateSectionTitle(
                "Preview",
                "A stitched view of the stored summary, transcript, and any detected participant context."));
        }

        var insightsCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Warning,
            ShowAccent = !_embedded,
            Padding = _embedded ? new Padding(6) : new Padding(16)
        };
        insightsCard.Controls.Add(_insightsBox);
        if (_embedded)
        {
            insightsCard.Controls.Add(CreateEmbeddedPaneLabel("Insights"));
        }
        else
        {
            insightsCard.Controls.Add(UiTheme.CreateSectionTitle(
                "Insights",
                "Smart title, meeting type, sentiment, agenda, speakers, highlight clips, and commitments extracted for this session."));
        }

        _previewTabs = new UiTabControl
        {
            Dock = DockStyle.Fill,
            ItemSize = new Size(140, 36)
        };
        _previewTabs.TabPages.Add(new TabPage("Preview") { Padding = new Padding(8) });
        _previewTabs.TabPages.Add(new TabPage("Insights") { Padding = new Padding(8) });
        _previewTabs.TabPages[0].Controls.Add(previewCard);
        _previewTabs.TabPages[1].Controls.Add(insightsCard);

        var askBody = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };
        askBody.Controls.Add(_answerBox);
        askBody.Controls.Add(_askButton);
        askBody.Controls.Add(_questionBox);

        var askCard = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            AccentColor = UiTheme.Warning,
            ShowAccent = !_embedded,
            Padding = _embedded ? new Padding(6) : new Padding(16)
        };
        askCard.Controls.Add(askBody);
        if (_embedded)
        {
            askCard.Controls.Add(CreateEmbeddedPaneLabel("Ask this meeting"));
        }
        else
        {
            askCard.Controls.Add(UiTheme.CreateSectionTitle(
                "Ask this meeting",
                "Use the saved transcript and summary context to answer targeted follow-up questions in English."));
        }

        _detailSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = _embedded ? 8 : 10
        };
        UiTheme.StyleSplitContainer(_detailSplit);
        _detailSplit.Panel1.Controls.Add(_previewTabs);
        _detailSplit.Panel2.Controls.Add(askCard);
        _detailSplit.SplitterMoved += (_, _) => _savedDetailSplitDistance = _detailSplit.SplitterDistance;

        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = _embedded ? 8 : 10
        };
        UiTheme.StyleSplitContainer(_mainSplit);
        _mainSplit.Panel1.Controls.Add(resultsCard);
        _mainSplit.Panel2.Controls.Add(_detailSplit);
        _mainSplit.SplitterMoved += (_, _) => _savedMainSplitDistance = _mainSplit.SplitterDistance;

        var statusStrip = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = _embedded ? 0 : 34,
            BackColor = Color.Transparent,
            Padding = new Padding(8, 10, 8, 0)
        };
        statusStrip.Controls.Add(_statusLabel);
        statusStrip.Visible = !_embedded;

        var chromePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.AppBackground,
            Padding = _embedded ? new Padding(2) : new Padding(14)
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
            AdjustResultColumns();
        };

        LoadRecent();
    }

    public void RefreshSessions() => LoadRecent();

    private void ResetLayout()
    {
        _savedMainSplitDistance = Math.Max(
            _dashboard.WorkspaceListMinWidth,
            (int)(_mainSplit.Width * _dashboard.HistoryMainSplitRatio));
        _savedDetailSplitDistance = Math.Max(
            _dashboard.WorkspaceBottomPaneMinHeight,
            (int)(_detailSplit.Height * _dashboard.HistoryDetailSplitRatio));
        ClampSplitters();
        _statusLabel.Text = "Meeting History layout reset.";
    }

    private void LoadRecent()
    {
        PopulateResults(_repository.GetRecentSessions());
        _statusLabel.Text = "Showing recent meetings.";
    }

    private async Task SearchAsync()
    {
        var query = _queryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            LoadRecent();
            return;
        }

        if (_semanticSearchCheckBox.Checked)
        {
            var semanticResults = await _cliClient.SemanticSearchResultsAsync(query, 25);
            PopulateSemanticResults(semanticResults);
            _statusLabel.Text = semanticResults.Count == 0
                ? $"Semantic search found no matches for \"{query}\"."
                : $"Semantic search completed for \"{query}\".";
            return;
        }

        PopulateResults(_repository.SearchSessions(query));
        _statusLabel.Text = $"Keyword search completed for \"{query}\".";
    }

    private void PopulateResults(IReadOnlyList<SavedMeetingSession> sessions)
    {
        _resultsView.BeginUpdate();
        _resultsView.Items.Clear();
        foreach (var session in sessions)
        {
            var snippet = session.SearchBlob.Length > 220
                ? session.SearchBlob[..220].Replace(Environment.NewLine, " ")
                : session.SearchBlob.Replace(Environment.NewLine, " ");
            var item = new ListViewItem(session.Title)
            {
                Tag = session
            };
            item.SubItems.Add(session.Platform);
            item.SubItems.Add(session.StartedAt?.ToLocalTime().ToString("g") ?? string.Empty);
            item.SubItems.Add(snippet);
            _resultsView.Items.Add(item);
        }

        _resultsView.EndUpdate();
        AdjustResultColumns();
        _previewBox.Text = sessions.Count == 0 ? "No saved meetings matched that query." : string.Empty;
        _answerBox.Clear();
        _openFolderButton.Enabled = false;
        _askButton.Enabled = false;
        UpdateResultBadge(sessions.Count);

        if (_resultsView.Items.Count > 0)
        {
            _resultsView.Items[0].Selected = true;
            _resultsView.Select();
            LoadSelectedPreview();
        }
        else
        {
            _statusLabel.Text = "No saved meetings matched that query.";
        }
    }

    private void PopulateSemanticResults(IReadOnlyList<SemanticSearchResultItem> results)
    {
        var sessionsByDirectory = _repository.GetRecentSessions(500)
            .ToDictionary(session => session.SessionDirectory, StringComparer.OrdinalIgnoreCase);

        _resultsView.BeginUpdate();
        _resultsView.Items.Clear();
        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.SessionDir) ||
                !sessionsByDirectory.TryGetValue(result.SessionDir, out var session))
            {
                continue;
            }

            var excerpt = result.Excerpts?.FirstOrDefault();
            var snippet = excerpt?.Text;
            if (string.IsNullOrWhiteSpace(snippet))
            {
                snippet = session.SearchBlob.Length > 220
                    ? session.SearchBlob[..220].Replace(Environment.NewLine, " ")
                    : session.SearchBlob.Replace(Environment.NewLine, " ");
            }

            var item = new ListViewItem(session.Title)
            {
                Tag = session
            };
            item.SubItems.Add(session.Platform);
            item.SubItems.Add(session.StartedAt?.ToLocalTime().ToString("g") ?? string.Empty);
            item.SubItems.Add(snippet ?? string.Empty);
            _resultsView.Items.Add(item);
        }

        _resultsView.EndUpdate();
        AdjustResultColumns();
        _previewBox.Text = _resultsView.Items.Count == 0 ? "No semantically related meetings matched that query." : string.Empty;
        _answerBox.Clear();
        _openFolderButton.Enabled = false;
        _askButton.Enabled = false;
        UpdateResultBadge(_resultsView.Items.Count);

        if (_resultsView.Items.Count > 0)
        {
            _resultsView.Items[0].Selected = true;
            _resultsView.Select();
            LoadSelectedPreview();
        }
        else
        {
            _statusLabel.Text = "No semantic matches were found.";
        }
    }

    private void LoadSelectedPreview()
    {
        var session = GetSelectedSession();
        if (session is null)
        {
            _previewBox.Text = string.Empty;
            _insightsBox.Text = string.Empty;
            _answerBox.Clear();
            _openFolderButton.Enabled = false;
            _askButton.Enabled = false;
            return;
        }

        _previewBox.Text = _repository.LoadPreview(session);
        _insightsBox.Text = _repository.LoadInsightsSummary(session);
        _answerBox.Clear();
        _openFolderButton.Enabled = true;
        _askButton.Enabled = true;
        _statusLabel.Text = $"Loaded {session.Title}.";
    }

    private async Task AskAsync()
    {
        var session = GetSelectedSession();
        var question = _questionBox.Text.Trim();
        if (session is null)
        {
            _statusLabel.Text = "Select a meeting first, then ask your question.";
            return;
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            _statusLabel.Text = "Type a question first.";
            return;
        }

        _askButton.Enabled = false;
        _answerBox.Text = "Looking through the saved meeting context...";
        _statusLabel.Text = "Running ask-this-meeting...";
        try
        {
            var answer = await _cliClient.AskMeetingAsync(session.SessionDirectory, question);
            _answerBox.Text = answer;
            _statusLabel.Text = "Answer ready.";
        }
        catch (Exception error)
        {
            _answerBox.Text = error.Message;
            _statusLabel.Text = "Ask failed.";
        }
        finally
        {
            _askButton.Enabled = true;
        }
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

    private SavedMeetingSession? GetSelectedSession()
        => _resultsView.SelectedItems.Count == 0
            ? null
            : _resultsView.SelectedItems[0].Tag as SavedMeetingSession;

    private void ClampSplitters()
    {
        if (_mainSplit.Width > 0)
        {
            ApplyMainSplitConstraints();
            var maxMain = Math.Max(_mainSplit.Panel1MinSize, _mainSplit.Width - _mainSplit.Panel2MinSize - _mainSplit.SplitterWidth);
            _mainSplit.SplitterDistance = Math.Max(_mainSplit.Panel1MinSize, Math.Min(maxMain, _savedMainSplitDistance));
            _savedMainSplitDistance = _mainSplit.SplitterDistance;
        }

        if (_detailSplit.Height > 0)
        {
            ApplyDetailSplitConstraints();
            var maxDetail = Math.Max(_detailSplit.Panel1MinSize, _detailSplit.Height - _detailSplit.Panel2MinSize - _detailSplit.SplitterWidth);
            _detailSplit.SplitterDistance = Math.Max(_detailSplit.Panel1MinSize, Math.Min(maxDetail, _savedDetailSplitDistance));
            _savedDetailSplitDistance = _detailSplit.SplitterDistance;
        }
    }

    private void ApplyMainSplitConstraints()
    {
        var available = Math.Max(0, _mainSplit.Width - _mainSplit.SplitterWidth);
        if (available <= 0)
        {
            return;
        }

        var desiredListMin = _embedded ? Math.Max(200, _dashboard.WorkspaceListMinWidth - 40) : _dashboard.WorkspaceListMinWidth;
        var desiredDetailMin = _embedded ? Math.Max(260, _dashboard.WorkspaceDetailMinWidth - 40) : _dashboard.WorkspaceDetailMinWidth;
        var leftMin = Math.Min(desiredListMin, Math.Max(180, available - desiredDetailMin));
        var rightMin = Math.Min(desiredDetailMin, Math.Max(220, available - leftMin));
        if (leftMin + rightMin > available)
        {
            leftMin = Math.Max(180, available / 2);
            rightMin = Math.Max(180, available - leftMin);
        }

        _mainSplit.Panel1MinSize = Math.Max(0, Math.Min(leftMin, Math.Max(0, available)));
        _mainSplit.Panel2MinSize = Math.Max(0, Math.Min(rightMin, Math.Max(0, available - _mainSplit.Panel1MinSize)));
    }

    private void ApplyDetailSplitConstraints()
    {
        var available = Math.Max(0, _detailSplit.Height - _detailSplit.SplitterWidth);
        if (available <= 0)
        {
            return;
        }

        var desiredBottomMin = _embedded ? Math.Max(140, _dashboard.WorkspaceBottomPaneMinHeight - 40) : _dashboard.WorkspaceBottomPaneMinHeight;
        var topMin = Math.Min(_embedded ? 180 : 220, Math.Max(140, available - desiredBottomMin));
        var bottomMin = Math.Min(desiredBottomMin, Math.Max(120, available - topMin));
        if (topMin + bottomMin > available)
        {
            topMin = Math.Max(120, available / 2);
            bottomMin = Math.Max(120, available - topMin);
        }

        _detailSplit.Panel1MinSize = Math.Max(0, Math.Min(topMin, Math.Max(0, available)));
        _detailSplit.Panel2MinSize = Math.Max(0, Math.Min(bottomMin, Math.Max(0, available - _detailSplit.Panel1MinSize)));
    }

    private void UpdateResultBadge(int count)
    {
        _resultBadge.Text = count == 1 ? "1 meeting" : $"{count} meetings";
        _resultBadge.Invalidate();
    }

    private void AdjustResultColumns()
    {
        if (_resultsView.Columns.Count < 4 || _resultsView.ClientSize.Width <= 0)
        {
            return;
        }

        var available = Math.Max(320, _resultsView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
        var titleWidth = Math.Max(180, (int)(available * 0.34));
        var platformWidth = Math.Max(84, (int)(available * 0.12));
        var startedWidth = Math.Max(124, (int)(available * 0.19));
        var snippetWidth = Math.Max(140, available - titleWidth - platformWidth - startedWidth);

        _resultsView.Columns[0].Width = titleWidth;
        _resultsView.Columns[1].Width = platformWidth;
        _resultsView.Columns[2].Width = startedWidth;
        _resultsView.Columns[3].Width = snippetWidth;
    }

    private static Control CreateEmbeddedPaneLabel(string text)
        => new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 20,
            Font = UiTheme.BodyFontSemibold,
            ForeColor = UiTheme.TextPrimary,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 0, 0, 2),
            Text = text
        };

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
