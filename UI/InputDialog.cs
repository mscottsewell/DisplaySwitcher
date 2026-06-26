namespace DisplaySwitcher.UI;

/// <summary>Minimal modal text-input dialog (replacement for VB's InputBox).</summary>
public sealed class InputDialog : Form
{
    private readonly TextBox _textBox;

    public string InputText => _textBox.Text;

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(360, 130);

        var label = new Label
        {
            Text = prompt,
            AutoSize = false,
            Location = new Point(12, 12),
            Size = new Size(336, 20),
        };

        _textBox = new TextBox
        {
            Text = defaultValue,
            Location = new Point(12, 38),
            Size = new Size(336, 23),
        };

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(192, 86),
            Size = new Size(75, 28),
        };

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(273, 86),
            Size = new Size(75, 28),
        };

        Controls.Add(label);
        Controls.Add(_textBox);
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public static string? Show(string title, string prompt, string defaultValue = "")
    {
        using var dialog = new InputDialog(title, prompt, defaultValue);
        return dialog.ShowDialog() == DialogResult.OK && dialog.InputText.Trim().Length > 0
            ? dialog.InputText.Trim()
            : null;
    }
}
