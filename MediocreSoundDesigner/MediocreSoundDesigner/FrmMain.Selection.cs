namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private void SetupTrackSelection()
    {
        trackManager.TrackSelectionChanged += (sender, e) => UpdateTransportControls();
        menuEdit.DropDownItems.Add(new ToolStripSeparator());
        menuEdit.DropDownItems.Add(new ToolStripMenuItem("Select All Tracks", null,
            (sender, e) => trackManager.SelectAllTracks()) { ShortcutKeys = Keys.Control | Keys.A });
        menuEdit.DropDownItems.Add(new ToolStripMenuItem("Deselect Tracks", null,
            (sender, e) => trackManager.ClearTrackSelection()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.A });
        menuEditSplit.ToolTipText = "Split the active track at the playhead. Other selected tracks are unchanged.";
        menuEditTrim.ToolTipText = "Keep the active track's highlighted range at its original position.";
        menuEditVolume.Text = "Active Track Volume...";
    }

    private void TrackHeader_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not Control header || header.Tag is not Track track)
            return;
        EndRangeDrag();
        EndTimelineDrag();
        trackManager.SelectTrackByClick(track, (ModifierKeys & Keys.Control) != 0, (ModifierKeys & Keys.Shift) != 0);
        UpdateTransportControls();
    }

    private void CreateTrackHeader(Track track)
    {
        Label header = new()
        {
            Tag = track, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(5, 0, 0, 0), AccessibleRole = AccessibleRole.ListItem,
            Cursor = Cursors.Hand
        };
        header.MouseDown += TrackHeader_MouseDown;
        header.Paint += (sender, e) =>
        {
            if (trackManager.IsTrackSelected(track))
            {
                using Pen pen = new(track == trackManager.SelectedTrack ? Color.Gold : Settings.Color.Playline);
                e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, header.Width - 1), Math.Max(0, header.Height - 1));
            }
        };
        BindTrackIdentityHeader(track, header);
        BindTrackReorderHandle(track, header);
        track.TrackHeader = header;
        panTracks.Controls.Add(header);
    }

    private string TrackSelectionSummary()
    {
        int count = trackManager.SelectedTracks.Count;
        if (count == 0)
            return "No tracks selected. Click a track header to select.";
        return $"{count} selected | Active: {trackManager.SelectedTrack.Name} (split/trim/effects) | Ctrl/Shift-click headers to select.";
    }
}
