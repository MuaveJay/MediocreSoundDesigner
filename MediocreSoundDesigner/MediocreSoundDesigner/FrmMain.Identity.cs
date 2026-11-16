namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private void RenameTrack(Track track)
    {
        if (track is null || !trackManager.Tracks.Contains(track)) return;
        using TrackNameDialog form = new(track.Name);
        if (form.ShowDialog(this) == DialogResult.OK) track.Rename(form.TrackName);
    }

    private void ChangeTrackColor(Track track)
    {
        if (track is null || !trackManager.Tracks.Contains(track)) return;
        using ColorDialog chooser = new() { Color = track.TrackColor.IsEmpty ? Color.Cyan : track.TrackColor, FullOpen = true };
        if (chooser.ShowDialog(this) == DialogResult.OK) track.SetTrackColor(chooser.Color);
    }

    private void BindTrackIdentityHeader(Track track, Label header)
    {
        header.UseMnemonic = false;
        header.Padding = new Padding(31, 0, 0, 0);
        header.MouseDoubleClick += (sender, e) => { if (e.Button == MouseButtons.Left && (ModifierKeys & (Keys.Control | Keys.Shift)) == 0) RenameTrack(track); };
        ContextMenuStrip context = new(components);
        context.Items.Add("Rename...", null, (sender, e) => RenameTrack(track));
        context.Items.Add("Change Color...", null, (sender, e) => ChangeTrackColor(track));
        header.ContextMenuStrip = context;
        header.Paint += (sender, e) =>
        {
            using Brush color = new SolidBrush(track.TrackColor.IsEmpty ? Color.Cyan : track.TrackColor);
            e.Graphics.FillRectangle(color, 24, 2, 4, Math.Max(0, header.Height - 4));
        };
    }
}
