using System.Reflection;
using MediocreSoundDesigner;
using NAudio.Wave;

internal static partial class Program
{
    private static void RunTimelineChecks()
    {
        Run("Timeline coordinates clamp and retain a shared sample grid", TimelineCoordinates);
        Run("Ruler marks round times without accumulating sample drift", RulerTicks);
        Run("Ruler renders ticks and its playhead on a generated canvas", RulerPainting);
        Run("Right-clicking a waveform selects an absolute project position", WaveformPosition);
        Run("Right-dragging updates and clamps until the pointer is released", WaveformDragging);
        Run("Lost capture and missing buttons cancel timeline dragging", CancelTimelineDrag);
        Run("The ruler and playhead line both support direct dragging", RulerAndLineDragging);
        Run("Waveform seeking preserves playing and paused states", WaveformPlaybackStates);
        Run("Ruler, bottom slider, and time readout share the same cursor", SharedTimelineControls);
        Run("Zoom and resize preserve audio timing and ruler alignment", TimelineResizeZoom);
        Run("Vertical track scrolling leaves the ruler fixed and aligned", TimelineVerticalScroll);
        Run("Repeated seeking does not copy the entire mixed audio", ScrubAllocations);
        Run("Removing a row cancels its active timeline drag", RemovedDragSource);
        Run("Empty timeline clicks stay at zero without opening a device", EmptyTimelineInput);
    }

    private static void TimelineCoordinates()
    {
        foreach (int scale in new[] { 0, 1, 10, 500, 999 })
        {
            foreach (long frame in new long[] { 0, 1, 4410, 11025, 44100 })
            {
                double pixel = TimelineScale.PixelAtFrame(frame, scale);
                long snapped = TimelineScale.FrameAtPixel((int)pixel, scale, 44100);
                Assert(snapped <= frame && frame - snapped < Math.Max(1, scale), "Coordinate round-trip changed the sample grid.");
            }
        }
        Assert(TimelineScale.FrameAtPixel(-10, 100, 44100) == 0 && TimelineScale.FrameAtPixel(int.MaxValue, 1000, 44100) == 44100,
            "Pointer boundaries were not clamped.");
        Assert(TimelineScale.FrameAtPixel(100, 100, 0) == 0, "Empty project accepted a nonzero cursor.");
    }

    private static void RulerTicks()
    {
        var seconds = TimelineScale.MajorTicks(1000, 441, 100);
        Assert(seconds[0].Frame == 0 && seconds[1].Frame == 44100 && seconds[10].Frame == 441000,
            "One-second tick marks are incorrect.");
        var milliseconds = TimelineScale.MajorTicks(1000, 1);
        Assert(milliseconds[1].Frame == 221 && milliseconds[2].Frame == 441 && milliseconds[4].Frame == 882,
            "Millisecond tick rounding drifted.");
        Assert(TimelineScale.FormatTimestamp(milliseconds[1].Frame) == "00:00.005", "Ruler labels do not match the rounded time mark.");
        foreach (int scale in new[] { 1, 10, 500, 999, int.MaxValue })
        {
            var ticks = TimelineScale.MajorTicks(5000, scale);
            Assert(ticks.Count > 1 && ticks.Count <= 52, "Ruler tick generation is not bounded by the visible width.");
            for (int i = 1; i < ticks.Count; i++)
                Assert(ticks[i].Frame > ticks[i - 1].Frame && ticks[i].Pixel - ticks[i - 1].Pixel >= 99,
                    "Ticks overlap or run backwards.");
        }
        Assert(TimelineScale.MajorTicks(0, 100).Count == 0, "Zero-width ruler generated ticks.");
    }

    private static void RulerPainting()
    {
        using TimelineRuler ruler = new() { Size = new Size(600, 40), BackColor = Color.Black,
            ForeColor = Color.LightGray, FramesPerPixel = 100, DurationFrames = 44100, PlayheadFrame = 15000 };
        using Bitmap canvas = new(600, 40);
        using Graphics graphics = Graphics.FromImage(canvas);
        graphics.Clear(Color.Black);
        ruler.GetType().GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ruler, new object[] { new PaintEventArgs(graphics, new Rectangle(0, 0, 600, 40)) });
        Assert(canvas.GetPixel(150, 38).ToArgb() == Settings.Color.Playline.ToArgb(), "Ruler playhead was not drawn at the shared coordinate.");
        Assert(canvas.GetPixel(500, 20).ToArgb() != Color.Black.ToArgb(), "End-of-project area was not distinguished.");
    }

    private static void WithTimeline(Action<FrmMain, TrackManager, List<FakeOutput>> check, bool addTracks = true)
    {
        using FrmMain form = new(); // Never shown; synthetic pointer events do not capture the desktop mouse.
        TrackManager original = Field<TrackManager>(form, "trackManager");
        List<FakeOutput> devices = new();
        using TrackManager manager = new(null!, () => { FakeOutput d = new(); devices.Add(d); return d; });
        form.GetType().GetField("trackManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, manager);
        manager.ProjectChanged += (_, _) => InvokeTimeline(form, "RefreshTrackPanels");
        try
        {
            form.ClientSize = new Size(1000, 700);
            if (addTracks)
            {
                manager.ExecuteEdit(() =>
                {
                    Track first = new(Constant(44100));
                    Track second = new(Constant(11025)); second.MoveToFrame(11025);
                    manager.AddTrack(first); manager.AddTrack(second); manager.SelectTrack(first);
                });
            }
            manager.DrawTimescale = 100;
            manager.ClearHistory();
            check(form, manager, devices);
        }
        finally { original.Dispose(); }
    }

    private static void InvokeTimeline(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private static void Pointer(Control source, string eventName, MouseButtons button, int x)
        => typeof(Control).GetMethod(eventName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(source, new object[] { new MouseEventArgs(button, 1, x, 20, 0) });

    private static void WaveformPosition() => WithTimeline((form, manager, devices) =>
    {
        Track second = manager.Tracks[1];
        float[] before = manager.GetMixedSamples();
        Pointer(second.PanDrawSamples, "OnMouseDown", MouseButtons.Right, 200);
        Pointer(second.PanDrawSamples, "OnMouseUp", MouseButtons.Right, 200);
        Assert(manager.GetPlayheadFrame() == 20000 && manager.SelectedTrack == second && devices.Count == 0,
            "Waveform seek used track-local time or unexpectedly played audio.");
        Assert(second.StartFrame == 11025 && !manager.CanUndo, "Navigation moved audio or created an edit history entry.");
        Equal(manager.GetMixedSamples(), before);
        Pointer(second.PanDrawSamples, "OnMouseDown", MouseButtons.Left, 100);
        Assert(manager.GetPlayheadFrame() == 20000, "Ordinary left-click unexpectedly sought playback.");
        Pointer(second.PanDrawSamples, "OnMouseDoubleClick", MouseButtons.Right, 100);
        Assert(!second.IsDisabled, "Right double-click muted the track while navigating.");
    });

    private static void WaveformDragging() => WithTimeline((form, manager, _) =>
    {
        Panel wave = manager.Tracks[0].PanDrawSamples;
        Pointer(wave, "OnMouseDown", MouseButtons.Right, 50);
        Pointer(wave, "OnMouseMove", MouseButtons.Right, 120);
        Assert(manager.GetPlayheadFrame() == 12000, "Right drag did not update the cursor.");
        Pointer(wave, "OnMouseMove", MouseButtons.Right, -20);
        Assert(manager.GetPlayheadFrame() == 0, "Drag went before project zero.");
        Pointer(wave, "OnMouseMove", MouseButtons.Right, 2000);
        Assert(manager.GetPlayheadFrame() == 44100, "Drag went past the project end.");
        Pointer(wave, "OnMouseUp", MouseButtons.Right, 221);
        Pointer(wave, "OnMouseMove", MouseButtons.Right, 300);
        Assert(manager.GetPlayheadFrame() == 22100, "Release did not commit the final position or stop dragging.");
    });

    private static void CancelTimelineDrag() => WithTimeline((form, manager, _) =>
    {
        Panel wave = manager.Tracks[0].PanDrawSamples;
        Pointer(wave, "OnMouseDown", MouseButtons.Right, 40);
        InvokeTimeline(form, "Timeline_MouseCaptureChanged", wave, EventArgs.Empty);
        Pointer(wave, "OnMouseMove", MouseButtons.Right, 80);
        Assert(manager.GetPlayheadFrame() == 4000, "Lost capture left a stuck drag.");
        Pointer(wave, "OnMouseDown", MouseButtons.Right, 60);
        Pointer(wave, "OnMouseMove", MouseButtons.None, 90);
        Pointer(wave, "OnMouseMove", MouseButtons.Right, 100);
        Assert(manager.GetPlayheadFrame() == 6000, "Missing button state left a stuck drag.");
    });

    private static void RulerAndLineDragging() => WithTimeline((form, manager, _) =>
    {
        TimelineRuler ruler = Field<TimelineRuler>(form, "timelineRuler");
        Pointer(ruler, "OnMouseDown", MouseButtons.Left, 110);
        Pointer(ruler, "OnMouseMove", MouseButtons.Left, 200);
        Pointer(ruler, "OnMouseUp", MouseButtons.Left, 200);
        Assert(manager.GetPlayheadFrame() == 20000, "Ruler left-drag failed.");
        Label line = Field<Label>(form, "lblPlayhead");
        Pointer(line, "OnMouseDown", MouseButtons.Right, 0);
        Pointer(line, "OnMouseMove", MouseButtons.Right, 10);
        Assert(manager.GetPlayheadFrame() == 21000, "Playhead-line drag lost its absolute horizontal offset.");
        Pointer(line, "OnMouseUp", MouseButtons.Right, 0);
        Pointer(ruler, "OnMouseDown", MouseButtons.Right, 150);
        Pointer(ruler, "OnMouseUp", MouseButtons.Right, 150);
        Assert(manager.GetPlayheadFrame() == 15000, "Ruler right-click failed.");
    });

    private static void WaveformPlaybackStates() => WithTimeline((form, manager, devices) =>
    {
        Panel wave = manager.Tracks[0].PanDrawSamples;
        manager.PlayAll(); devices[0].AdvanceFrames(4410);
        Pointer(wave, "OnMouseDown", MouseButtons.Right, 200);
        Assert(manager.State == PlaybackState.Playing && manager.GetPlayheadFrame() == 20000 && devices[0].Disposed,
            "Playing waveform seek failed to continue at the requested position.");
        int count = devices.Count;
        Pointer(wave, "OnMouseMove", MouseButtons.Right, 200);
        Assert(devices.Count == count, "Stationary drag repeatedly restarted the audio device.");
        Pointer(wave, "OnMouseUp", MouseButtons.Right, 200);
        manager.PauseAll();
        Pointer(wave, "OnMouseDown", MouseButtons.Right, 100);
        Pointer(wave, "OnMouseMove", MouseButtons.Right, 150);
        Pointer(wave, "OnMouseUp", MouseButtons.Right, 150);
        Assert(manager.State == PlaybackState.Paused && manager.GetPlayheadFrame() == 15000 && devices.Count == count,
            "Paused waveform drag unexpectedly started playback.");
        manager.PlayAll(); Assert(manager.GetPlayheadFrame() == 15000, "Resume ignored the scrubbed position.");
    });

    private static void SharedTimelineControls() => WithTimeline((form, manager, _) =>
    {
        TrackBar bottom = Field<TrackBar>(form, "trkPlaybackPosition");
        TimelineRuler ruler = Field<TimelineRuler>(form, "timelineRuler");
        Label time = Field<Label>(form, "lblPlaybackTime");
        bottom.Value = 5000; ClickHandler(form, "trkPlaybackPosition_Scroll");
        Assert(ruler.PlayheadFrame == 22050 && time.Text == "00:00.500 / 00:01.000", "Bottom slider did not update the ruler.");
        Pointer(ruler, "OnMouseDown", MouseButtons.Right, 100);
        Pointer(ruler, "OnMouseUp", MouseButtons.Right, 100);
        Assert(ruler.PlayheadFrame == 10000 && bottom.Value == 2268 && time.Text == "00:00.226 / 00:01.000",
            "Ruler navigation did not update the bottom controls.");
        ClickHandler(form, "btnStop_Click");
        Assert(ruler.PlayheadFrame == 0 && bottom.Value == 0, "Stop did not reset the shared ruler cursor.");
    });

    private static void TimelineResizeZoom() => WithTimeline((form, manager, _) =>
    {
        float[] audio = manager.GetMixedSamples();
        manager.SeekToFrame(22050);
        TimelineRuler ruler = Field<TimelineRuler>(form, "timelineRuler");
        foreach (Size size in new[] { new Size(650, 500), new Size(1280, 720), new Size(1920, 1080) })
        foreach (int scale in new[] { 1, 100, 999 })
        {
            form.Size = size; manager.DrawTimescale = scale;
            InvokeTimeline(form, "UpdateSizes");
            Panel tracks = Field<Panel>(form, "panTracks");
            Assert(ruler.Left == tracks.Left && ruler.Width == tracks.ClientSize.Width && ruler.Bottom == tracks.Top,
                "Ruler and waveforms lost their common origin while resizing.");
            Assert(ruler.FramesPerPixel == scale && ruler.PlayheadFrame == 22050 && manager.GetPlayheadFrame() == 22050,
                "Zoom moved the shared timeline cursor.");
            Assert(manager.Tracks.All(t => t.PanDrawSamples.BorderStyle == BorderStyle.None && t.PanDrawSamples.Left == 0),
                "Track selection changed the waveform's client origin.");
            Equal(manager.GetMixedSamples(), audio);
        }
    });

    private static void TimelineVerticalScroll() => WithTimeline((form, manager, _) =>
    {
        for (int i = 0; i < 10; i++) manager.AddTrack(new(Constant(4410)));
        Panel tracks = Field<Panel>(form, "panTracks");
        TimelineRuler ruler = Field<TimelineRuler>(form, "timelineRuler");
        int rulerTop = ruler.Top;
        // AutoScrollPosition requires Control.Created, not only an HWND.
        // Initialize this hidden control tree without making the form visible.
        typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(bool) }, null)!.Invoke(tracks, new object[] { true });
        InvokeTimeline(form, "UpdateSizes");
        tracks.AutoScrollPosition = new Point(0, 160);
        int scroll = tracks.AutoScrollPosition.Y;
        Assert(scroll < 0, $"Vertical scroll test did not scroll: min={tracks.AutoScrollMinSize}, client={tracks.ClientSize}, display={tracks.DisplayRectangle}, handle={tracks.IsHandleCreated}.");
        InvokeTimeline(form, "UpdateSizes");
        Assert(tracks.AutoScrollPosition.Y == scroll && ruler.Top == rulerTop && ruler.Width == tracks.ClientSize.Width,
            "Track layout reset vertical scrolling or moved the fixed ruler.");
        Assert(!tracks.HorizontalScroll.Visible && manager.Tracks.All(t => t.PanDrawSamples.Width == ruler.Width),
            "Timeline layout created horizontal overflow or differing track scales.");
        manager.SeekToFrame(10000); ClickHandler(form, "tmrUpdatePlaybackState_Tick");
        Label line = Field<Label>(form, "lblPlayhead");
        Assert(line.Left == 100 && line.Top == scroll && line.Height >= tracks.AutoScrollMinSize.Height,
            "Playhead no longer covers the scrolled track area.");
    });

    private static void ScrubAllocations()
    {
        var (manager, _, devices) = PlaybackProject();
        using (manager)
        {
            manager.PlayAll();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 1; i <= 20; i++) manager.SeekToFrame(i * 1000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert(allocated < 44100 * 2 * sizeof(float) * 2, $"Repeated seeks copied too much audio: {allocated} bytes.");
            Assert(manager.GetPlayheadFrame() == 20000 && devices.Count == 21, "Repeated seek ended at the wrong cursor.");
        }
    }

    private static void RemovedDragSource() => WithTimeline((form, manager, _) =>
    {
        Track track = manager.Tracks[1];
        Pointer(track.PanDrawSamples, "OnMouseDown", MouseButtons.Right, 100);
        manager.RemTrack(track);
        Pointer(track.PanDrawSamples, "OnMouseMove", MouseButtons.Right, 200);
        Assert(manager.GetPlayheadFrame() == 10000, "A removed track row retained its active drag.");
    });

    private static void EmptyTimelineInput() => WithTimeline((form, manager, devices) =>
    {
        TimelineRuler ruler = Field<TimelineRuler>(form, "timelineRuler");
        Pointer(ruler, "OnMouseDown", MouseButtons.Right, 500);
        Pointer(ruler, "OnMouseMove", MouseButtons.Right, -20);
        Pointer(ruler, "OnMouseUp", MouseButtons.Right, 600);
        Assert(manager.GetPlayheadFrame() == 0 && manager.State == PlaybackState.Stopped && devices.Count == 0,
            "Empty ruler navigation created audio playback or a nonzero cursor.");
    }, addTracks: false);
}
