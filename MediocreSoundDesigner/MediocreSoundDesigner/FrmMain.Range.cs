namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private readonly ToolStripMenuItem menuEditTrimAtTimes = new();
    private readonly Label lblSelectionRange = new();
    private Control rangeDragSource;
    private Track rangeDragTrack;
    private long rangeAnchorFrame;
    private long rangeDragRevision;

    private void SetupTrimControls()
    {
        menuEditTrim.Text = "Trim to Selection";
        menuEditTrim.ShortcutKeys = Keys.Control | Keys.T;
        menuEditTrim.ToolTipText = "Keep the highlighted audio at its original timeline position.";
        menuEditTrimAtTimes.Name = "menuEditTrimAtTimes";
        menuEditTrimAtTimes.Text = "Trim at Times...";
        menuEditTrimAtTimes.ShortcutKeys = Keys.Control | Keys.Shift | Keys.T;
        menuEditTrimAtTimes.Click += menuEditTrimAtTimes_Click;
        menuEdit.DropDownItems.Insert(menuEdit.DropDownItems.IndexOf(menuEditTrim) + 1, menuEditTrimAtTimes);
        menuEdit.DropDownOpening += (sender, e) => UpdateTrimControls();
        lblSelectionRange.Name = "lblSelectionRange";
        lblSelectionRange.AutoSize = false;
        lblSelectionRange.AutoEllipsis = true;
        lblSelectionRange.AccessibleName = "Selection start, end, and duration";
        Controls.Add(lblSelectionRange);
        trackManager.RangeSelectionChanged += (sender, e) =>
        {
            trackManager.RedrawAll();
            UpdateTrimControls();
        };
    }

    private void UpdateTrimControls()
    {
        menuEditTrim.Enabled = trackManager.CanTrimSelection;
        Track track = trackManager.SelectedTrack;
        menuEditTrimAtTimes.Enabled = track is not null && trackManager.Tracks.Contains(track) && track.TimelineFrameCount > 0;
        TimelineSelection range = trackManager.RangeSelection;
        lblSelectionRange.Text = range is null ? TrackSelectionSummary()
            : $"Keep {FormatTimestamp(range.StartFrame)} to {FormatTimestamp(range.EndFrame)}   |   Duration {FormatTimestamp(range.DurationFrames)}";
    }

    private void LayoutRangeReadout()
    {
        lblSelectionRange.SetBounds(panTracks.Left, timelineRuler.Bottom + 3,
            panTracks.ClientSize.Width, Font.Height + 5);
    }

    private void Range_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not Control source || source.Tag is not Track track)
            return;
        EndRangeDrag();
        EndTimelineDrag();
        trackManager.SelectTrack(track);
        rangeDragSource = source;
        rangeDragTrack = track;
        rangeDragRevision = track.Revision;
        rangeAnchorFrame = RangeFrameAtPointer(source, e.X, track);
        trackManager.SetRangeSelection(track, rangeAnchorFrame, rangeAnchorFrame);
        if (source.Visible && source.IsHandleCreated)
            source.Capture = true;
        UpdateTrimControls();
    }

    private long RangeFrameAtPointer(Control source, int x, Track track)
    {
        long frame = TimelineScale.FrameAtPixel(x + source.Left - panTracks.AutoScrollPosition.X,
            trackManager.DrawTimescale, long.MaxValue);
        return Math.Clamp(frame, track.StartFrame, track.EndFrame);
    }

    private void Range_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender != rangeDragSource)
            return;
        if ((e.Button & MouseButtons.Left) == 0)
        {
            EndRangeDrag();
            return;
        }
        UpdateRangePointer(e.X);
    }

    private void Range_MouseUp(object sender, MouseEventArgs e)
    {
        if (sender == rangeDragSource && e.Button == MouseButtons.Left)
        {
            UpdateRangePointer(e.X);
            EndRangeDrag();
        }
    }

    private void UpdateRangePointer(int x)
    {
        if (!trackManager.Tracks.Contains(rangeDragTrack) || rangeDragTrack.Revision != rangeDragRevision)
        {
            EndRangeDrag();
            return;
        }
        trackManager.SetRangeSelection(rangeDragTrack, rangeAnchorFrame,
            RangeFrameAtPointer(rangeDragSource, x, rangeDragTrack));
    }

    private void Range_MouseCaptureChanged(object sender, EventArgs e)
    {
        if (sender == rangeDragSource && !rangeDragSource.Capture)
            EndRangeDrag();
    }

    private void EndRangeDrag()
    {
        Control previous = rangeDragSource;
        rangeDragSource = null;
        rangeDragTrack = null;
        if (previous is not null && previous.Capture)
            previous.Capture = false;
    }

    private void PaintRangeSelection(Track track, Graphics graphics, Size size)
    {
        TimelineSelection range = trackManager.RangeSelection;
        if (range is null || range.Track != track)
            return;
        double left = TimelineScale.PixelAtFrame(range.StartFrame, trackManager.DrawTimescale);
        double right = TimelineScale.PixelAtFrame(range.EndFrame, trackManager.DrawTimescale);
        if (left >= size.Width || right <= 0)
            return;
        float x1 = (float)Math.Clamp(left, 0, size.Width);
        float x2 = (float)Math.Clamp(right, 0, size.Width);
        using Brush fill = new SolidBrush(Color.FromArgb(64, Color.DodgerBlue));
        using Pen edge = new(Color.LightSkyBlue);
        graphics.FillRectangle(fill, x1, 0, Math.Max(1, x2 - x1), size.Height);
        graphics.DrawLine(edge, x1, 0, x1, size.Height);
        if (right < size.Width)
            graphics.DrawLine(edge, (float)right, 0, (float)right, size.Height);
    }

    private void menuEditTrim_Click(object sender, EventArgs e)
    {
        EndRangeDrag();
        RunPlaybackAction(() => { trackManager.TrimToSelection(); });
    }

    private void menuEditTrimAtTimes_Click(object sender, EventArgs e)
    {
        Track track = trackManager.SelectedTrack;
        if (track is null || track.TimelineFrameCount == 0)
            return;
        TimelineSelection range = trackManager.RangeSelection;
        using FrmEditTrim form = new();
        form.SetSound(track, range is null ? null : (range.StartFrame - track.StartFrame) / (double)AudioData.SampleRate,
            range is null ? null : (range.EndFrame - track.StartFrame) / (double)AudioData.SampleRate);
        if (form.ShowDialog(this) == DialogResult.OK)
            RunPlaybackAction(() =>
            {
                if (track.Trim(form.StartSeconds, form.EndSeconds) && trackManager.State == NAudio.Wave.PlaybackState.Playing)
                    trackManager.SeekToFrame(trackManager.GetPlayheadFrame());
            });
        UpdateTransportControls();
    }
}
