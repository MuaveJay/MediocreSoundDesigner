using MediocreSoundDesigner;
using NAudio.Wave;
using System.Reflection;

internal static partial class Program
{
    private static void RunPlayheadFollowChecks()
    {
        Run("Follow easing is gradual and independent of update frequency", FollowEasing);
        Run("Follow math clamps empty, extreme zoom and boundary values", FollowBounds);
        Run("Playback starts following smoothly past the threshold and tracks continuously", FollowPlaybackMotion);
        Run("Camera following preserves samples, audio device, selection and Redo", FollowProjectState);
        Run("Pause freezes the view, resume follows and Stop returns to the beginning", FollowPauseResumeStop);
        Run("Loop wrap returns the viewport to the beginning without a backward sweep", FollowLoop);
        Run("Manual panning suspends following until playback or seeking resumes", FollowManualPan);
        Run("View option disables following and can restore an offscreen cursor", FollowOption);
        Run("Following waits for waveform seek and range gestures to finish", FollowGestures);
        Run("Zooming retains its pointer anchor before following continues", FollowZoomAnchor);
        Run("Moving views keep ruler, scrollbar and retained waveform painting synchronized", FollowRepaint);
        Run("Empty and short projects stay safe and the final cursor remains visible", FollowEndAndEmpty);
    }

    private static void WithFollow(Action<FrmMain, TrackManager, List<FakeOutput>> check)
        => WithTimeline((form, manager, devices) =>
        {
            form.ClientSize = new Size(900, 650);
            Track source = new(ReportedNoise(12 * AudioData.SampleRate));
            Track layer = new(Constant(44100)); layer.MoveToFrame(5000);
            manager.AddTrack(source); manager.AddTrack(layer); manager.SelectAllTracks(); manager.ClearHistory();
            manager.SetViewport(0, 100, Field<Panel>(form, "panTracks").ClientSize.Width);
            InvokeTimeline(form, "UpdateSizes"); check(form, manager, devices);
        }, addTracks: false);
    private static void FollowStep(FrmMain form, double seconds = 0.02)
    {
        InvokeTimeline(form, "AdvancePlayheadFollowing", seconds);
        InvokeTimeline(form, "UpdateTransportControls");
    }
    private static void StartFollowing(FrmMain form, TrackManager manager, List<FakeOutput> devices)
    {
        ClickHandler(form, "btnPlay_Click");
        int width = Field<Panel>(form, "panTracks").ClientSize.Width;
        devices[^1].AdvanceFrames((int)(width * manager.DrawTimescale * 0.70)); FollowStep(form);
    }

    private static void FollowEasing()
    {
        double first = PlayheadFollow.NextStart(0, 70000, 100, 1000, 1000000, 0.02);
        Assert(first > 0 && first < 35000, "Follow jumps directly to the target.");
        double fast = 0, slow = 0;
        for (int i = 0; i < 10; i++) fast = PlayheadFollow.NextStart(fast, 70000, 100, 1000, 1000000, 0.02);
        for (int i = 0; i < 2; i++) slow = PlayheadFollow.NextStart(slow, 70000, 100, 1000, 1000000, 0.10);
        Near(fast, slow, 1e-8);
        Assert(fast > first && fast < 35000 && PlayheadFollow.NextStart(fast, 70000, 100, 1000, 1000000, 0) == fast,
            "Easing overshoots or moves without elapsed time.");
    }

    private static void FollowBounds()
    {
        foreach (double scale in new[] { ZoomScale.Minimum, 1, 100, ZoomScale.Maximum, double.NaN })
        foreach (long extent in new long[] { 0, 100, 1000000 })
        foreach (long frame in new long[] { 0, 1, 900000, long.MaxValue })
        {
            double start = PlayheadFollow.NextStart(double.NaN, frame, scale, 800, extent, 1);
            double maximum = Math.Max(0, extent - 800 * ZoomScale.Sanitize(scale));
            Assert(double.IsFinite(start) && start >= 0 && start <= maximum, "Following produced invalid viewport bounds.");
            if (extent > 0 && maximum > 0)
            {
                double pixel = TimelineScale.PixelAtFrame(Math.Min(frame, extent), scale, start);
                Assert(pixel >= -1e-6 && pixel <= 800 + 1e-6, "Following lost the cursor at extreme zoom.");
            }
        }
        Near(PlayheadFollow.NextStart(0, 100, 100, 0, 1000, 0.02), 0);
    }

    private static void FollowPlaybackMotion() => WithFollow((form, manager, devices) =>
    {
        int width = Field<Panel>(form, "panTracks").ClientSize.Width;
        ClickHandler(form, "btnPlay_Click"); devices[0].AdvanceFrames(width * 50); FollowStep(form);
        Near(manager.ViewStartFrame, 0);
        devices[0].AdvanceFrames(width * 20); long frame = manager.GetPlayheadFrame(); FollowStep(form);
        double target = frame - width * manager.DrawTimescale * PlayheadFollow.Anchor;
        Assert(manager.ViewStartFrame > 0 && manager.ViewStartFrame < target, "Following did not ease in before the edge.");
        for (int i = 0; i < 30; i++)
        {
            double previous = manager.ViewStartFrame; devices[0].AdvanceFrames(882); FollowStep(form);
            Assert(manager.ViewStartFrame > previous && (manager.ViewStartFrame - previous) / manager.DrawTimescale < width * 0.20,
                "Continuous following stalled or jumped by a page.");
            int x = manager.GetPlayheadPos(); Assert(x >= 0 && x < width, "Cursor left the following viewport.");
        }
    });

    private static void FollowProjectState() => WithFollow((form, manager, devices) =>
    {
        Track source = manager.Tracks[0]; source.FadeIn(); manager.Undo(); manager.SetRangeSelection(source, 100, 400);
        float[] mixed = manager.GetMixedSamples(); long start = source.StartFrame; double zoom = manager.DrawTimescale;
        StartFollowing(form, manager, devices); long frame = manager.GetPlayheadFrame(); FollowStep(form, 0.05);
        Assert(manager.CanRedo && !manager.CanUndo && manager.SelectedTracks.Count == 2 && manager.RangeSelection?.StartFrame == 100 &&
            source.StartFrame == start && manager.DrawTimescale == zoom && manager.GetPlayheadFrame() == frame && devices.Count == 1 && !devices[0].Disposed,
            "Camera motion modified project/history or restarted audio.");
        Equal(manager.GetMixedSamples(), mixed, 0);
    });

    private static void FollowPauseResumeStop() => WithFollow((form, manager, devices) =>
    {
        StartFollowing(form, manager, devices); ClickHandler(form, "btnPlay_Click");
        double frozen = manager.ViewStartFrame; long frame = manager.GetPlayheadFrame();
        for (int i = 0; i < 10; i++) FollowStep(form, 0.1);
        Assert(manager.ViewStartFrame == frozen && manager.GetPlayheadFrame() == frame && manager.State == PlaybackState.Paused, "Paused view drifted.");
        ClickHandler(form, "btnPlay_Click"); devices[^1].AdvanceFrames(20000); FollowStep(form);
        Assert(manager.ViewStartFrame > frozen && manager.State == PlaybackState.Playing, "Resume did not follow playback.");
        ClickHandler(form, "btnStop_Click"); FollowStep(form);
        Assert(manager.ViewStartFrame == 0 && manager.GetPlayheadFrame() == 0 && manager.State == PlaybackState.Stopped, "Stop left the camera away from the beginning.");
    });

    private static void FollowLoop() => WithFollow((form, manager, devices) =>
    {
        manager.LoopPlayback = true; StartFollowing(form, manager, devices);
        devices[^1].AdvanceFrames((int)manager.DurationFrames); devices[^1].Stop();
        ClickHandler(form, "tmrUpdatePlaybackState_Tick");
        Assert(manager.State == PlaybackState.Playing && manager.GetPlayheadFrame() == 0 && manager.ViewStartFrame == 0,
            "Loop wrap failed to return both audio and view to the beginning.");
    });

    private static void FollowManualPan() => WithFollow((form, manager, devices) =>
    {
        StartFollowing(form, manager, devices);
        HScrollBar scroll = Field<HScrollBar>(form, "scrTimeline");
        InvokeTimeline(form, "scrTimeline_Scroll", scroll, new ScrollEventArgs(ScrollEventType.ThumbTrack, 800000));
        double manual = manager.ViewStartFrame; devices[^1].AdvanceFrames(1000); FollowStep(form, 0.1);
        Assert(manager.ViewStartFrame == manual && (Field<ToolStripMenuItem>(form, "menuFollowPlayhead").Text ?? "").Contains("paused"), "Following fought a manual pan.");
        ClickHandler(form, "btnPlay_Click"); ClickHandler(form, "btnPlay_Click"); FollowStep(form);
        Assert(manager.ViewStartFrame < manual && !(Field<ToolStripMenuItem>(form, "menuFollowPlayhead").Text ?? "").Contains("paused"), "Resume failed to rejoin the cursor.");
        InvokeTimeline(form, "scrTimeline_Scroll", scroll, new ScrollEventArgs(ScrollEventType.ThumbTrack, 900000));
        TrackBar position = Field<TrackBar>(form, "trkPlaybackPosition"); position.Value = 3000; ClickHandler(form, "trkPlaybackPosition_Scroll"); FollowStep(form);
        Assert(!(Field<ToolStripMenuItem>(form, "menuFollowPlayhead").Text ?? "").Contains("paused"), "Seeking did not resume following.");
    });

    private static void FollowOption() => WithFollow((form, manager, devices) =>
    {
        ToolStripMenuItem follow = Field<ToolStripMenuItem>(form, "menuFollowPlayhead");
        Assert(follow.Checked && follow.CheckOnClick && Field<ToolStripMenuItem>(form, "menuView").DropDownItems.Contains(follow), "Default View option is missing.");
        follow.Checked = false; StartFollowing(form, manager, devices); Near(manager.ViewStartFrame, 0);
        manager.SetViewport(200000, 100, Field<Panel>(form, "panTracks").ClientSize.Width); ClickHandler(form, "btnStop_Click");
        Near(manager.ViewStartFrame, 200000);
        follow.Checked = true; FollowStep(form); Near(manager.ViewStartFrame, 0);
        InvokeTimeline(form, "SuspendPlayheadFollowing"); follow.Checked = false;
        Assert(follow.Text == "Follow Playhead", "Disabled option still claims following is paused.");
    });

    private static void FollowGestures() => WithFollow((form, manager, devices) =>
    {
        ClickHandler(form, "btnPlay_Click"); Panel canvas = manager.Tracks[0].PanDrawSamples;
        Pointer(canvas, "OnMouseDown", MouseButtons.Right, 600); double initial = manager.ViewStartFrame; FollowStep(form);
        Assert(manager.ViewStartFrame == initial, "Following moved coordinates during right seeking.");
        Pointer(canvas, "OnMouseUp", MouseButtons.Right, 600); FollowStep(form);
        Assert(manager.ViewStartFrame > initial, "Following did not resume when seeking ended.");
        ClickHandler(form, "btnStop_Click"); ClickHandler(form, "btnPlay_Click"); devices[^1].AdvanceFrames(60000);
        Pointer(canvas, "OnMouseDown", MouseButtons.Left, 10); Pointer(canvas, "OnMouseMove", MouseButtons.Left, 20); FollowStep(form);
        Near(manager.ViewStartFrame, 0);
        Pointer(canvas, "OnMouseUp", MouseButtons.Left, 20); FollowStep(form);
        Assert(manager.ViewStartFrame > 0 && manager.RangeSelection?.DurationFrames == 1000, "Range drag was disrupted by automatic camera motion.");
    });

    private static void FollowZoomAnchor() => WithFollow((form, manager, devices) =>
    {
        manager.SetViewport(50000, 100, Field<Panel>(form, "panTracks").ClientSize.Width); manager.SeekToFrame(100000);
        ClickHandler(form, "btnPlay_Click"); devices[^1].AdvanceFrames(10000); FollowStep(form);
        double anchor = manager.ViewStartFrame + 250 * manager.DrawTimescale; Wheel(manager.Tracks[0].PanDrawSamples, 120, 250);
        Near(manager.ViewStartFrame + 250 * manager.DrawTimescale, anchor, 1e-6);
        long frame = manager.GetPlayheadFrame(); FollowStep(form);
        Assert(manager.GetPlayheadFrame() == frame && manager.DrawTimescale == 80, "Following changed the zoom anchor's audio or scale.");
    });

    private static void FollowRepaint() => WithFollow((form, manager, devices) =>
    {
        Panel parent = Field<Panel>(form, "panTracks");
        typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(bool) }, null)!.Invoke(parent, new object[] { true });
        InvokeTimeline(form, "UpdateSizes");
        RepaintProbe[] probes = manager.Tracks.Select(track => new RepaintProbe(track.PanDrawSamples)).ToArray();
        try
        {
            StartFollowing(form, manager, devices);
            for (int i = 0; i < 12; i++)
            {
                devices[^1].AdvanceFrames(882); FollowStep(form);
                foreach (var probe in probes) probe.Verify("following " + i);
                Assert(Field<TimelineRuler>(form, "timelineRuler").ViewStartFrame == manager.ViewStartFrame, "Ruler lagged behind the camera.");
                double maximum = Math.Max(0, manager.TimelineExtentFrames - parent.ClientSize.Width * manager.DrawTimescale);
                int expected = (int)Math.Round(manager.ViewStartFrame / maximum * 1000000);
                Assert(Math.Abs(Field<HScrollBar>(form, "scrTimeline").Value - expected) <= 1, "Scrollbar lagged behind the camera.");
            }
        }
        finally { foreach (var probe in probes) probe.Dispose(); }
    });

    private static void FollowEndAndEmpty() => WithTimeline((form, manager, devices) =>
    {
        FollowStep(form); Assert(manager.ViewStartFrame == 0 && devices.Count == 0, "Empty following opened audio or moved the view.");
        Track track = new(Constant(1000)); manager.AddTrack(track); manager.SelectTrack(track);
        int width = Field<Panel>(form, "panTracks").ClientSize.Width;
        manager.SetViewport(0, 1000.0 / width, width); ClickHandler(form, "btnPlay_Click");
        devices[0].AdvanceFrames(1000); devices[0].Stop(); ClickHandler(form, "tmrUpdatePlaybackState_Tick");
        Assert(manager.State == PlaybackState.Stopped && manager.GetPlayheadFrame() == 1000 && manager.ViewStartFrame == 0, "Short-project completion moved or restarted audio.");
        Control canvas = track.PanDrawSamples;
        using Bitmap image = new(canvas.ClientSize.Width, canvas.ClientSize.Height);
        using (Graphics graphics = Graphics.FromImage(image)) PaintCanvas(canvas, graphics, canvas.ClientRectangle);
        Assert(image.GetPixel(image.Width - 1, 5).ToArgb() == Settings.Color.Playline.ToArgb(), "The final cursor disappeared at the right boundary.");
    }, addTracks: false);
}
