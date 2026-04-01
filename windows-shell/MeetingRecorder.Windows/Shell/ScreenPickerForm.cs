using MeetingRecorder.Windows.Models;

namespace MeetingRecorder.Windows.Shell;

public sealed class ScreenPickerForm : Form
{
    private readonly ListBox _screensList;

    public DisplayCaptureTarget? SelectedDisplay => _screensList.SelectedItem as DisplayCaptureTarget;

    public ScreenPickerForm(IReadOnlyList<DisplayCaptureTarget> displays, DisplayCaptureTarget? selectedDisplay = null)
    {
        Text = "Pick Screen";
        Icon = AppIconProvider.Icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Width = 560;
        Height = 320;

        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(16, 16, 16, 0),
            Text = "Choose which screen to record when Full screen mode is enabled."
        };

        _screensList = new ListBox
        {
            Dock = DockStyle.Fill
        };

        foreach (var display in displays)
        {
            _screensList.Items.Add(display);
        }

        if (selectedDisplay is not null)
        {
            var match = displays.FirstOrDefault(display => string.Equals(display.DeviceName, selectedDisplay.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _screensList.SelectedItem = match;
            }
        }

        if (_screensList.SelectedIndex < 0 && _screensList.Items.Count > 0)
        {
            _screensList.SelectedIndex = 0;
        }

        var startButton = new Button
        {
            Text = "Use screen",
            Width = 100,
            Height = 34,
            DialogResult = DialogResult.OK
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Width = 100,
            Height = 34,
            DialogResult = DialogResult.Cancel
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12)
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(startButton);

        Controls.Add(_screensList);
        Controls.Add(buttons);
        Controls.Add(label);

        AcceptButton = startButton;
        CancelButton = cancelButton;
    }
}
