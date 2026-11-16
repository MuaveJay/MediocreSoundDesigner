namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private Control reorderSource;
    private Track reorderTrack;
    private Point reorderPress;
    private bool reorderActive;
    private int reorderDropIndex = -1;
    private int reorderPointerY;
    private readonly Label reorderMarker = new() { BackColor = Color.Gold, Height = 3, Visible = false };
    private System.Windows.Forms.Timer reorderScrollTimer;

    private void SetupTrackReordering()
    {
        reorderMarker.Name = "reorderMarker";
        reorderMarker.AccessibleName = "Track drop position";
        panTracks.Controls.Add(reorderMarker);
        reorderScrollTimer = new(components) { Interval = 50 };
        reorderScrollTimer.Tick += (sender, e) => ScrollTrackReorder();
    }

    private void BindTrackReorderHandle(Track track, Label header)
    {
        Label handle = new()
        {
            Name = "trackReorderHandle", Text = "::", TextAlign = ContentAlignment.MiddleCenter,
            Width = 20, Cursor = Cursors.SizeNS, Tag = track,
            AccessibleName = "Drag to reorder " + track.Name, AccessibleRole = AccessibleRole.Grip
        };
        header.Controls.Add(handle);
        header.SizeChanged += (sender, e) => handle.SetBounds(2, 2, 20, Math.Max(1, header.Height - 4));
        ToolTip help = new(components);
        help.SetToolTip(handle, "Drag vertically to reorder this track. Escape cancels.");
        handle.MouseDown += Reorder_MouseDown;
        handle.MouseMove += Reorder_MouseMove;
        handle.MouseUp += Reorder_MouseUp;
        handle.MouseCaptureChanged += (sender, e) =>
        {
            if (sender == reorderSource && !reorderSource.Capture) CancelTrackReorder();
        };
    }

    private int ReorderPanelY(Control handle, int y) => handle.Parent.Top + handle.Top + y;

    private void Reorder_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not Control handle ||
            handle.Tag is not Track track || !trackManager.Tracks.Contains(track)) return;
        if (SelectTrackFromPointer(track, modifiersOnly: true)) return;
        FinishVolumeGesture();
        EndRangeDrag();
        EndTimelineDrag();
        CancelTrackReorder();
        reorderSource = handle;
        reorderTrack = track;
        reorderPress = e.Location;
        reorderPointerY = ReorderPanelY(handle, e.Y);
        if (handle.Visible && handle.IsHandleCreated) handle.Capture = true;
    }

    private void Reorder_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender != reorderSource) return;
        if ((e.Button & MouseButtons.Left) == 0) { CancelTrackReorder(); return; }
        if (!reorderActive && Math.Abs(e.X - reorderPress.X) < SystemInformation.DragSize.Width / 2 &&
            Math.Abs(e.Y - reorderPress.Y) < SystemInformation.DragSize.Height / 2) return;
        reorderActive = true;
        reorderPointerY = ReorderPanelY(reorderSource, e.Y);
        UpdateTrackDropPosition();
        // Only the visible application starts a timer; hidden-form checks drive it explicitly.
        if (reorderSource.Visible) reorderScrollTimer.Start();
    }

    private void UpdateTrackDropPosition()
    {
        if (!reorderActive || reorderTrack is null || !trackManager.Tracks.Contains(reorderTrack))
        { CancelTrackReorder(); return; }
        int slot = 0;
        foreach (Track row in trackManager.Tracks)
        {
            int middle = (row.TrackHeader.Top + row.PanDrawSamples.Bottom) / 2;
            if (reorderPointerY < middle) break;
            slot++;
        }
        int sourceIndex = trackManager.Tracks.ToList().IndexOf(reorderTrack);
        reorderDropIndex = slot > sourceIndex ? slot - 1 : slot;
        int y = slot == trackManager.Tracks.Count ? trackManager.Tracks[^1].PanDrawSamples.Bottom : trackManager.Tracks[slot].TrackHeader.Top;
        reorderMarker.SetBounds(0, Math.Clamp(y, 0, Math.Max(0, panTracks.ClientSize.Height - 3)), panTracks.ClientSize.Width, 3);
        reorderMarker.Visible = true;
        reorderMarker.BringToFront();
    }

    private void ScrollTrackReorder()
    {
        if (!reorderActive) return;
        int direction = reorderPointerY < 28 ? -1 : reorderPointerY > panTracks.ClientSize.Height - 28 ? 1 : 0;
        if (direction != 0)
        {
            int maximum = Math.Max(0, panTracks.AutoScrollMinSize.Height - panTracks.ClientSize.Height);
            int next = Math.Clamp(-panTracks.AutoScrollPosition.Y + direction * 24, 0, maximum);
            panTracks.AutoScrollPosition = new Point(0, next);
        }
        UpdateTrackDropPosition();
    }

    private void Reorder_MouseUp(object sender, MouseEventArgs e)
    {
        if (sender != reorderSource || e.Button != MouseButtons.Left) return;
        if (reorderActive)
        {
            reorderPointerY = ReorderPanelY(reorderSource, e.Y);
            UpdateTrackDropPosition();
        }
        Track track = reorderTrack;
        int destination = reorderDropIndex;
        bool commit = reorderActive && track is not null && trackManager.Tracks.Contains(track) && destination >= 0;
        CancelTrackReorder();
        if (commit) trackManager.MoveTrack(track, destination);
    }

    private void CancelTrackReorder()
    {
        Control old = reorderSource;
        reorderSource = null;
        reorderTrack = null;
        reorderActive = false;
        reorderDropIndex = -1;
        reorderScrollTimer?.Stop();
        reorderMarker.Visible = false;
        if (old is not null && old.Capture) old.Capture = false;
    }
}
