namespace MeetingRecorder.Windows.Shell;

public sealed class LiveCaptionsForm : Form
{
    private readonly Label _statusLabel;
    private readonly TextBox _captionsBox;

    public LiveCaptionsForm()
    {
        UiTheme.ApplyForm(this);
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        TopMost = true;
        ShowInTaskbar = false;
        Width = 560;
        Height = 360;
        MinimumSize = new Size(420, 240);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(Screen.PrimaryScreen?.WorkingArea.Right - Width - 24 ?? 50, 232);
        Text = "Live Captions";
        Icon = AppIconProvider.Icon;

        _statusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            Padding = new Padding(12, 10, 12, 0),
            Font = UiTheme.BodyFontSemibold,
            ForeColor = UiTheme.TextPrimary,
            Text = "Captions will appear here when recording starts."
        };

        _captionsBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            WordWrap = true,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.TextPrimary,
            Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point),
            Text = "Bring the meeting audio in, then start recording to see live captions."
        };

        var hideButton = new Button
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Text = "Hide"
        };
        hideButton.Click += (_, _) => Hide();

        Controls.Add(_captionsBox);
        Controls.Add(_statusLabel);
        Controls.Add(hideButton);
        FormClosing += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Hide();
        };
    }

    public void UpdateCaptions(string status, string text)
    {
        _statusLabel.Text = status;
        var nextText = string.IsNullOrWhiteSpace(text)
            ? "Listening for speech..."
            : text;

        if (string.Equals(_captionsBox.Text, nextText, StringComparison.Ordinal))
        {
            return;
        }

        _captionsBox.Text = nextText;
        _captionsBox.SelectionStart = _captionsBox.TextLength;
        _captionsBox.ScrollToCaret();
    }
}
