namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private readonly TimelineRuler timelineRuler = new();
    private Control timelineDragSource;
    private MouseButtons timelineDragButton;
    private long lastScrubFrame = -1;

    private void SetupTimeline()
    {
        timelineRuler.Name = "timelineRuler";
        Controls.Add(timelineRuler);
        // Client origins must agree; selection is painted without changing borders.
        panTracks.BorderStyle = BorderStyle.None;
        lblPlayhead.Width = 2;
        BindTimelineInput(timelineRuler);
        BindTimelineInput(panTracks);
        BindTimelineInput(lblPlayhead);
        panTracks.Scroll += (sender, e) => UpdateTimelineView();
        panTracks.ClientSizeChanged += (sender, e) => { LayoutTimelineRuler(); LayoutRangeReadout(); };
        ToolTip help = new(components);
        help.SetToolTip(timelineRuler, "Click or right-drag to move the playhead.");
        help.SetToolTip(panTracks, "Right-click or right-drag a waveform to move the playhead.");
    }

    private void BindTimelineInput(Control control)
    {
        control.MouseDown += Timeline_MouseDown;
        control.MouseMove += Timeline_MouseMove;
        control.MouseUp += Timeline_MouseUp;
        control.MouseCaptureChanged += Timeline_MouseCaptureChanged;
        control.MouseDown += Range_MouseDown;
        control.MouseMove += Range_MouseMove;
        control.MouseUp += Range_MouseUp;
        control.MouseCaptureChanged += Range_MouseCaptureChanged;
    }

    private void Timeline_MouseDown(object sender, MouseEventArgs e)
    {
        Control source = (Control)sender;
        bool handle = e.Button == MouseButtons.Right ||
            (e.Button == MouseButtons.Left && (source == timelineRuler || source == lblPlayhead));
        if (!handle)
            return;
        EndRangeDrag();
        EndTimelineDrag();
        if (source.Tag is Track track && trackManager.SelectedTracks.Count <= 1)
            trackManager.SelectTrack(track);
        timelineDragSource = source;
        timelineDragButton = e.Button;
        lastScrubFrame = -1;
        // The real application captures its pointer while dragging; hidden-form
        // tests never take desktop mouse capture or show a window.
        if (source.Visible && source.IsHandleCreated)
            source.Capture = true;
        SeekTimelinePointer(source, e.X);
    }

    private void Timeline_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender != timelineDragSource)
            return;
        if ((e.Button & timelineDragButton) == 0)
        {
            EndTimelineDrag();
            return;
        }
        SeekTimelinePointer((Control)sender, e.X);
    }

    private void Timeline_MouseUp(object sender, MouseEventArgs e)
    {
        if (sender == timelineDragSource && e.Button == timelineDragButton)
        {
            SeekTimelinePointer((Control)sender, e.X);
            EndTimelineDrag();
        }
    }

    private void Timeline_MouseCaptureChanged(object sender, EventArgs e)
    {
        if (sender == timelineDragSource && !timelineDragSource.Capture)
            EndTimelineDrag();
    }

    private void EndTimelineDrag()
    {
        Control previous = timelineDragSource;
        timelineDragSource = null;
        timelineDragButton = MouseButtons.None;
        lastScrubFrame = -1;
        if (previous is not null && previous.Capture)
            previous.Capture = false;
    }

    private void SeekTimelinePointer(Control source, int x)
    {
        if (source != timelineRuler)
        {
            if (source != panTracks)
                x += source.Left;
            x -= panTracks.AutoScrollPosition.X;
        }
        long frame = TimelineScale.FrameAtPixel(x, trackManager.DrawTimescale, trackManager.DurationFrames);
        if (frame == lastScrubFrame)
            return;
        lastScrubFrame = frame;
        RunPlaybackAction(() => trackManager.SeekToFrame(frame));
    }

    private void LayoutTimelineRuler()
    {
        timelineRuler.SetBounds(panTracks.Left, trkDrawTimescale.Bottom + 8,
            panTracks.ClientSize.Width, Math.Max(34, Font.Height + 18));
    }

    private void LayoutTimelineTracks()
    {
        Point scroll = panTracks.AutoScrollPosition;
        lblPlayhead.Visible = false;
        panTracks.AutoScrollPosition = Point.Empty;
        int count = trackManager.Tracks.Count;
        int height = count == 0 ? 0 : Math.Max(104, panTracks.ClientSize.Height / count - 1);
        int headerHeight = Math.Max(24, Font.Height + 6);
        int contentHeight = checked(count * height);
        foreach (Track track in trackManager.Tracks)
        {
            track.PanDrawSamples.Width = 1;
            track.TrackHeader.Width = 1;
        }
        panTracks.AutoScrollMinSize = new Size(0, contentHeight);
        panTracks.PerformLayout();
        int top = 0;
        foreach (Track track in trackManager.Tracks)
        {
            track.TrackHeader.SetBounds(0, top, panTracks.ClientSize.Width, headerHeight);
            track.PanDrawSamples.SetBounds(0, top + headerHeight, panTracks.ClientSize.Width, height - headerHeight);
            track.PanDrawSamples.Invalidate();
            top += height;
        }
        lblPlayhead.SetBounds(0, 0, 2, Math.Max(panTracks.ClientSize.Height, contentHeight));
        panTracks.AutoScrollPosition = new Point(0, -scroll.Y);
        lblPlayhead.BringToFront();
        LayoutTimelineRuler();
        LayoutRangeReadout();
    }

    private void UpdateTimelineView()
    {
        long frame = trackManager.GetPlayheadFrame();
        timelineRuler.FramesPerPixel = trackManager.DrawTimescale;
        timelineRuler.DurationFrames = trackManager.DurationFrames;
        timelineRuler.PlayheadFrame = frame;
        long x = frame / trackManager.DrawTimescale;
        bool visible = trackManager.Tracks.Count > 0 && x <= panTracks.ClientSize.Width - lblPlayhead.Width;
        lblPlayhead.Left = visible ? (int)x : 0;
        lblPlayhead.Top = panTracks.AutoScrollPosition.Y;
        lblPlayhead.Visible = visible;
    }
}
