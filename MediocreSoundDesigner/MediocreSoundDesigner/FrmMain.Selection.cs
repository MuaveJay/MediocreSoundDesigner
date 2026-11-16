
namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private void SetupTrackSelection()
    {
        trackManager.TrackSelectionChanged +=
            (sender, e) => UpdateTransportControls();

        menuEdit.DropDownItems.Add(new ToolStripSeparator());

        menuEdit.DropDownItems.Add(new ToolStripMenuItem(
            "Select All Tracks", null,
            (sender, e) => trackManager.SelectAllTracks())
        {
            ShortcutKeys = Keys.Control | Keys.A
        });

        menuEdit.DropDownItems.Add(new ToolStripMenuItem(
            "Deselect Tracks", null,
            (sender, e) => trackManager.ClearTrackSelection())
        {
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.A
        });

        menuEditSplit.ToolTipText =
            "Split the active track at the playhead. Other selected tracks are unchanged.";

        menuEditTrim.ToolTipText =
            "Keep the active track's highlighted range at its original position.";

        menuEditVolume.Text = "Active Track Volume...";
    }

    private void TrackHeader_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left ||
            sender is not Control header ||
            header.Tag is not Track track)
            return;

        SelectTrackFromPointer(track);
    }

    private bool SelectTrackFromPointer(
        Track track, bool modifiersOnly = false)
    {
        Keys modifiers = ModifierKeys;

        bool toggle = (modifiers & Keys.Control) != 0;
        bool extend = (modifiers & Keys.Shift) != 0;

        if (modifiersOnly && !toggle && !extend)
            return false;

        FinishVolumeGesture();
        EndRangeDrag();
        EndTimelineDrag();
        CancelTrackReorder();

        trackManager.SelectTrackByClick(track, toggle, extend);
        UpdateTransportControls();

        return true;
    }

    private void CreateTrackHeader(Track track)
    {
        Label header = new()
        {
            Tag = track,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(5, 0, 75, 0),
            AccessibleRole = AccessibleRole.ListItem,
            Cursor = Cursors.Hand
        };

        header.MouseDown += TrackHeader_MouseDown;

        header.Paint += (sender, e) =>
        {
            if (trackManager.IsTrackSelected(track))
            {
                using Pen pen = new(
                    track == trackManager.SelectedTrack
                        ? Color.Gold
                        : Settings.Color.Playline);

                e.Graphics.DrawRectangle(
                    pen, 0, 0,
                    Math.Max(0, header.Width - 1),
                    Math.Max(0, header.Height - 1));
            }
        };

        BindTrackIdentityHeader(track, header);
        BindTrackReorderHandle(track, header);

        Button btnMute = new()
        {
            Name = "btnTrackMute",
            Size = new Size(30, 22),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            TabStop = false,
            AccessibleName = "Mute " + track.Name
        };

        Button btnSolo = new()
        {
            Name = "btnTrackSolo",
            Size = new Size(30, 22),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            TabStop = false,
            AccessibleName = "Solo " + track.Name
        };

        btnMute.FlatAppearance.BorderSize = 1;
        btnSolo.FlatAppearance.BorderSize = 1;

        // Draw the letters ourselves to avoid text clipping.
        void DrawButtonText(
            Button button,
            PaintEventArgs e,
            string letter)
        {
            TextRenderer.DrawText(
                e.Graphics,
                letter,
                new Font("Segoe UI", 9, FontStyle.Bold),
                button.ClientRectangle,
                Color.White,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.NoPadding);
        }

        btnMute.Paint += (sender, e) =>
            DrawButtonText(btnMute, e, "M");

        btnSolo.Paint += (sender, e) =>
            DrawButtonText(btnSolo, e, "S");

        void UpdateButtons()
        {
            btnMute.BackColor = track.IsDisabled
                ? Color.IndianRed
                : Color.FromArgb(55, 60, 70);

            btnSolo.BackColor = trackManager.IsSoloed(track)
                ? Color.Goldenrod
                : Color.FromArgb(55, 60, 70);

            btnMute.Invalidate();
            btnSolo.Invalidate();
        }

        void PositionButtons()
        {
            int top = Math.Max(
                0, (header.Height - btnMute.Height) / 2);

            btnMute.Location = new Point(
                Math.Max(0, header.Width - 70), top);

            btnSolo.Location = new Point(
                Math.Max(0, header.Width - 35), top);
        }

        btnMute.Click += (sender, e) =>
        {
            track.ToggleDisabled();

            UpdateButtons();
            trackManager.RefreshSelectionAppearance();

            RunPlaybackAction(
                trackManager.RefreshPlaybackAfterEdit);
        };

        btnSolo.Click += (sender, e) =>
        {
            trackManager.ToggleSolo(track);

            UpdateButtons();

            RunPlaybackAction(
                trackManager.RefreshPlaybackAfterEdit);
        };

        header.Controls.Add(btnMute);
        header.Controls.Add(btnSolo);

        header.Resize += (sender, e) => PositionButtons();
        header.Paint += (sender, e) => UpdateButtons();

        PositionButtons();
        UpdateButtons();

        track.TrackHeader = header;
        panTracks.Controls.Add(header);

        btnMute.BringToFront();
        btnSolo.BringToFront();
    }

    private string TrackSelectionSummary()
    {
        int count = trackManager.SelectedTracks.Count;

        if (count == 0)
            return "No tracks selected. Click a track header to select.";

        return $"{count} selected | Active: {trackManager.SelectedTrack.Name} (split/trim/effects) | Ctrl/Shift-click tracks to select.";
    }
}
