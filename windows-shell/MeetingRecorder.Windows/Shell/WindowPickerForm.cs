using MeetingRecorder.Windows.Models;

namespace MeetingRecorder.Windows.Shell;

public sealed class WindowPickerForm : Form
{
    private readonly ListBox _listBox;

    public MeetingCandidate? SelectedCandidate { get; private set; }

    public WindowPickerForm(IReadOnlyList<MeetingCandidate> candidates)
    {
        Text = "Pick Meeting Window";
        Icon = AppIconProvider.Icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 640;
        Height = 420;

        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(14, 14, 14, 0),
            Text = "Choose the meeting or browser window you want to record."
        };

        _listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false
        };
        _listBox.DisplayMember = nameof(MeetingCandidate.DisplayLabel);
        _listBox.Items.AddRange(candidates.Cast<object>().ToArray());
        if (_listBox.Items.Count > 0)
        {
            _listBox.SelectedIndex = 0;
        }

        var startButton = new Button
        {
            Text = "Start",
            Width = 96,
            Height = 34
        };
        startButton.Click += (_, _) =>
        {
            SelectedCandidate = _listBox.SelectedItem as MeetingCandidate;
            if (SelectedCandidate is null)
            {
                MessageBox.Show("Select a window first.", "Meeting Recorder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Width = 96,
            Height = 34
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12)
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(startButton);

        Controls.Add(_listBox);
        Controls.Add(buttons);
        Controls.Add(label);
    }
}
