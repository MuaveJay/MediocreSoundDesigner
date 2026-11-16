namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private readonly TimelineRuler timelineRuler = new();
    private Control timelineDragSource;
    private MouseButtons timelineDragButton;
    private long lastScrubFrame = -1;

    // Alt + left-drag moves an existing automation point.
    private Track automationDragTrack;
    private Control automationDragSource;
    private long automationDragOriginalFrame;

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
        help.SetToolTip(panTracks, "Right-click or right-drag a waveform to move the playhead. Alt + left-drag a purple point to move it.");
    }

    private void BindTimelineInput(Control control)
    {
        // Handle automation first, so a drag does not create a range selection or seek.
        control.MouseDown += (sender, e) =>
        {
            if (TryEditAutomationPoint((Control)sender, e) ||
                TryBeginAutomationDrag((Control)sender, e))
                return;
            Timeline_MouseDown(sender, e);
            Range_MouseDown(sender, e);
        };
        control.MouseMove += (sender, e) =>
        {
            if (automationDragSource is not null)
                return;
            Timeline_MouseMove(sender, e);
            Range_MouseMove(sender, e);
        };
        control.MouseUp += (sender, e) =>
        {
            if (automationDragSource is not null)
            {
                FinishAutomationDrag((Control)sender, e);
                return;
            }
            Timeline_MouseUp(sender, e);
            Range_MouseUp(sender, e);
        };
        control.MouseCaptureChanged += (sender, e) =>
        {
            if (automationDragSource is not null)
            {
                // Losing mouse capture cancels the gesture without changing audio.
                CancelAutomationDrag();
                return;
            }
            Timeline_MouseCaptureChanged(sender, e);
            Range_MouseCaptureChanged(sender, e);
        };
    }

    // Alt + Shift + click adds a point. Alt + right-click removes a nearby point.
    // Both gestures run before normal range selection and timeline seeking.
    private bool TryEditAutomationPoint(Control source, MouseEventArgs e)
    {
        if (source.Tag is not Track track || (ModifierKeys & Keys.Alt) == 0)
            return false;

        bool adding = e.Button == MouseButtons.Left && (ModifierKeys & Keys.Shift) != 0;
        bool removing = e.Button == MouseButtons.Right;
        if (!adding && !removing)
            return false;

        double scale = ZoomScale.Sanitize(trackManager.DrawTimescale);
        double localFrame = trackManager.ViewStartFrame + e.X * scale - track.StartFrame;
        if (localFrame < 0 || localFrame > track.TimelineFrameCount)
            return true;

        if (adding)
        {
            long frame = (long)Math.Clamp(
                Math.Round(localFrame, MidpointRounding.AwayFromZero),
                0, track.TimelineFrameCount);
            float gain = Math.Clamp(
                2f * (1f - (e.Y - 8f) / Math.Max(1f, source.ClientSize.Height - 16f)),
                0f, 2f);
            gain = MathF.Round(gain * 1000f) / 1000f;
            track.SetAutomationPoint(frame / (double)track.SampleRate, gain);
        }
        else
        {
            int height = source.ClientSize.Height;
            float GainY(float gain) =>
                8 + (2f - Math.Clamp(gain, 0f, 2f)) / 2f * Math.Max(0, height - 16);

            foreach (var point in track.AutomationPoints)
            {
                float x = (float)TimelineScale.PixelAtFrame(
                    track.StartFrame + point.Frame, scale, trackManager.ViewStartFrame);
                float y = GainY(point.Gain);
                float dx = e.X - x;
                float dy = e.Y - y;
                if (dx * dx + dy * dy > 11 * 11)
                    continue;

                track.RemoveAutomationPoint(point.Frame / (double)track.SampleRate);
                break;
            }
        }

        source.Invalidate();
        RunPlaybackAction(trackManager.RefreshPlaybackAfterEdit);
        return true;
    }

    private bool TryBeginAutomationDrag(Control source, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || (ModifierKeys & Keys.Alt) == 0 ||
            source.Tag is not Track track || track.AutomationPoints.Count == 0)
            return false;

        double scale = ZoomScale.Sanitize(trackManager.DrawTimescale);
        int height = source.ClientSize.Height;
        float GainY(float gain) => 8 + (2f - Math.Clamp(gain, 0f, 2f)) / 2f * Math.Max(0, height - 16);

        // Only grab an actual purple point, within an 11-pixel radius.
        foreach (var point in track.AutomationPoints)
        {
            float x = (float)TimelineScale.PixelAtFrame(
                track.StartFrame + point.Frame, scale, trackManager.ViewStartFrame);
            float y = GainY(point.Gain);
            float dx = e.X - x;
            float dy = e.Y - y;
            if (dx * dx + dy * dy > 11 * 11)
                continue;

            EndRangeDrag();
            EndTimelineDrag();
            automationDragTrack = track;
            automationDragSource = source;
            automationDragOriginalFrame = point.Frame;
            source.Cursor = Cursors.SizeAll;
            if (source.Visible && source.IsHandleCreated)
                source.Capture = true;
            return true;
        }
        return false;
    }

    private void FinishAutomationDrag(Control source, MouseEventArgs e)
    {
        if (source != automationDragSource || e.Button != MouseButtons.Left ||
            automationDragTrack is null)
            return;

        Track track = automationDragTrack;
        long oldFrame = automationDragOriginalFrame;
        double scale = ZoomScale.Sanitize(trackManager.DrawTimescale);
        double timelineFrame = trackManager.ViewStartFrame + e.X * scale;
        long newFrame = (long)Math.Clamp(
            Math.Round(timelineFrame - track.StartFrame, MidpointRounding.AwayFromZero),
            0, track.TimelineFrameCount);
        float gain = Math.Clamp(
            2f * (1f - (e.Y - 8f) / Math.Max(1f, source.ClientSize.Height - 16f)),
            0f, 2f);
        // Snap the percentage to one decimal place, matching the numeric editor.
        gain = MathF.Round(gain * 1000f) / 1000f;

        var original = track.AutomationPoints.FirstOrDefault(p => p.Frame == oldFrame);
        CancelAutomationDrag();

        if (oldFrame == newFrame && Math.Abs(original.Gain - gain) < 0.0005f)
            return;

        trackManager.ExecuteEdit(() =>
        {
            if (newFrame != oldFrame)
                track.RemoveAutomationPoint(oldFrame / (double)track.SampleRate);
            track.SetAutomationPoint(newFrame / (double)track.SampleRate, gain);
        });
        track.PanDrawSamples?.Invalidate();
        RunPlaybackAction(trackManager.RefreshPlaybackAfterEdit);
    }

    private void CancelAutomationDrag()
    {
        Control previous = automationDragSource;
        automationDragSource = null;
        automationDragTrack = null;
        automationDragOriginalFrame = 0;
        if (previous is not null)
        {
            previous.Cursor = Cursors.Default;
            if (previous.Capture)
                previous.Capture = false;
        }
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
        long frame = TimelineScale.FrameAtPixel(x, trackManager.DrawTimescale, trackManager.DurationFrames, trackManager.ViewStartFrame);
        if (frame == lastScrubFrame)
            return;
        lastScrubFrame = frame;
        ResumePlayheadFollowing(seek: true);
        RunPlaybackAction(() => trackManager.SeekToFrame(frame));
    }

    private void LayoutTimelineRuler()
    {
        timelineRuler.SetBounds(panTracks.Left, trkDrawTimescale.Bottom + Ui(6),
            panTracks.ClientSize.Width, Math.Max(Ui(34), Font.Height + Ui(18)));
    }

    private void LayoutTimelineTracks()
    {
        Point scroll = panTracks.AutoScrollPosition;
        lblPlayhead.Visible = false;
        panTracks.AutoScrollPosition = Point.Empty;
        int count = trackManager.Tracks.Count;
        int height = count == 0 ? 0 : Math.Max(Ui(120), panTracks.ClientSize.Height / count - 1);
        int headerHeight = Math.Max(Ui(28), Font.Height + Ui(8));
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
        timelineRuler.ViewStartFrame = trackManager.ViewStartFrame;
        timelineRuler.DurationFrames = trackManager.TimelineExtentFrames;
        timelineRuler.PlayheadFrame = frame;
        int x = trackManager.GetPlayheadPos();
        bool visible = trackManager.Tracks.Count > 0 && x >= 0 && x <= panTracks.ClientSize.Width;
        lblPlayhead.Left = visible ? x : 0;
        lblPlayhead.Top = panTracks.AutoScrollPosition.Y;
        lblPlayhead.Visible = false;
        InvalidatePlayhead(visible ? x : null);
        UpdateHorizontalNavigation();
    }
}
