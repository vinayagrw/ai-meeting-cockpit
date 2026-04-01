using MeetingRecorder.Windows.Models;

namespace MeetingRecorder.Windows.Shell;

public sealed class PromptForm : Form
{
    public enum PromptDecision
    {
        Start,
        Dismiss,
        OpenWidget
    }

    public PromptDecision Decision { get; private set; } = PromptDecision.Dismiss;

    public PromptForm(MeetingCandidate candidate)
    {
        UiTheme.ApplyForm(this);
        Text = "Meeting Recorder";
        Icon = AppIconProvider.Icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Width = 520;
        Height = 280;

        var badge = new UiBadgeLabel
        {
            Text = "Meeting detected",
            Width = 154,
            Height = 34,
            FillColor = UiTheme.AccentSoft,
            BorderColor = UiTheme.AccentSoft,
            TextColor = UiTheme.AccentStrong
        };

        var headerPanel = UiTheme.CreateHeaderPanel(
            "Auto prompt",
            "A meeting looks ready to record",
            "Start in the background right away, or open the full dashboard if you want to adjust capture mode first.",
            badge);

        var meetingCard = new UiCardPanel
        {
            Dock = DockStyle.Top,
            Height = 112,
            AccentColor = UiTheme.Accent,
            Padding = new Padding(18, 16, 18, 14)
        };

        var titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Font = UiTheme.SectionFont,
            ForeColor = UiTheme.TextPrimary,
            Text = candidate.Title
        };
        var detailLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = UiTheme.BodyFont,
            ForeColor = UiTheme.TextMuted,
            Text = $"Source: {candidate.ProcessName}  |  Platform: {candidate.Platform}{Environment.NewLine}Recording starts with meeting audio and microphone. You can still pause, resume, or stop from the dashboard."
        };

        meetingCard.Controls.Add(detailLabel);
        meetingCard.Controls.Add(titleLabel);
        meetingCard.Controls.Add(UiTheme.CreateSectionTitle("Detected meeting", "This prompt only appears after the meeting has stayed stable long enough to avoid false starts."));

        var startButton = CreateButton("Start now", UiButtonTone.Primary, 122);
        startButton.Click += (_, _) =>
        {
            Decision = PromptDecision.Start;
            Close();
        };

        var widgetButton = CreateButton("Open dashboard", UiButtonTone.Secondary, 138);
        widgetButton.Click += (_, _) =>
        {
            Decision = PromptDecision.OpenWidget;
            Close();
        };

        var dismissButton = CreateButton("Not now", UiButtonTone.Ghost, 108);
        dismissButton.Click += (_, _) =>
        {
            Decision = PromptDecision.Dismiss;
            Close();
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = Color.Transparent
        };
        buttons.Controls.Add(dismissButton);
        buttons.Controls.Add(widgetButton);
        buttons.Controls.Add(startButton);

        var chromePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.AppBackground,
            Padding = new Padding(16)
        };
        chromePanel.Controls.Add(buttons);
        chromePanel.Controls.Add(meetingCard);
        chromePanel.Controls.Add(headerPanel);
        Controls.Add(chromePanel);
    }

    private static Button CreateButton(string text, UiButtonTone tone, int width)
        => new UiButton
        {
            Text = text,
            Tone = tone,
            Width = width,
            Height = 38,
            Margin = new Padding(10, 0, 0, 0)
        };
}
