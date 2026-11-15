using System.ComponentModel;

namespace MediocreSoundDesigner;

public sealed class TrackNameDialog : Form
{
    private readonly TextBox nameInput = new();
    private readonly Label validation = new();
    private readonly Button confirm = new();
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string TrackName => nameInput.Text.Trim();

    public TrackNameDialog(string name)
    {
        Text = "Rename Track"; ClientSize = new Size(400, 142);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Label prompt = new() { Text = "Track name:", AutoSize = true, Location = new Point(12, 12) };
        nameInput.Name = "nameInput"; nameInput.Text = name; nameInput.MaxLength = 100;
        nameInput.SetBounds(12, 34, 376, 24);
        nameInput.AccessibleName = "Track name";
        validation.ForeColor = Color.Firebrick; validation.SetBounds(12, 63, 376, 25);
        confirm.Text = "Rename"; confirm.SetBounds(212, 102, 84, 28);
        confirm.Click += (sender, e) =>
        {
            if (Track.IsValidTrackName(nameInput.Text)) DialogResult = DialogResult.OK;
            else validation.Text = "Enter a name with 1-100 characters, without line breaks.";
        };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel };
        cancel.SetBounds(304, 102, 84, 28);
        Controls.AddRange(new Control[] { prompt, nameInput, validation, confirm, cancel });
        AcceptButton = confirm; CancelButton = cancel;
        Shown += (sender, e) => { nameInput.Focus(); nameInput.SelectAll(); };
    }
}
