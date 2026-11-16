using System.Reflection;
using MediocreSoundDesigner;

internal static partial class Program
{
    private static void RunWaveformViewChecks()
    {
        Run("Overview extrema include impulses between old sampled columns", OverviewPeaks);
        Run("Cached peak queries match stereo samples across block boundaries", PeakQueries);
        Run("Waveform cache follows audio edits and Undo", PeakCacheEdits);
        Run("Metadata changes reuse waveform peak data without full copies", PeakMetadataAllocations);
        Run("High zoom draws connected positive and negative sample values", SampleDetailRendering);
        Run("Overview rendering preserves hidden positive and negative peaks", OverviewRendering);
        Run("Quiet clip boundaries remain visible at their actual time", QuietBoundaryRendering);
        Run("Flat positive, negative and silent audio stays visible at overview zoom", FlatOverviewRendering);
        Run("The full exponential zoom range remains finite and useful", ZoomRange);
        Run("Zooming preserves the time under an interior pointer", ZoomAnchor);
        Run("Horizontal navigation reaches the end without changing audio", HorizontalView);
        Run("Ruler coordinates include viewport origin and sample detail", ViewportCoordinates);
        Run("Wheel zoom is routed once over waveforms and zoom control", WheelZoomInterface);
        Run("Fit and the horizontal scrollbar update the shared view", ViewNavigationInterface);
        Run("Cursor-strip repaint restores the complete underlying waveform", CursorRepaint);
        Run("Zooming while right-dragging keeps audio and selection unchanged", ZoomDuringScrub);
        Run("Panned ranges, split boundaries, and trim share one frame transform", PannedEditAlignment);
        Run("A minute-long waveform retains known peaks and playback time while panning", LongWaveformView);
        Run("Actual zoom-slider endpoints remain safe during right-button scrubbing", FullSliderDuringScrub);
    }

    private static void OverviewPeaks()
    {
        float[] samples = new float[2000]; samples[137] = 0.9f; samples[499] = -0.8f;
        using Track track = new(samples);
        var extrema = track.GetWaveformExtrema(0, 500); Near(extrema.Maximum, 0.9); Near(extrema.Minimum, -0.8);
        Assert(track.GetWaveformExtrema(500, 1000) == new WaveformExtrema(0, 0), "Peak query leaked across its interval.");
    }

    private static void PeakQueries()
    {
        Random random = new(403);
        float[] samples = Enumerable.Range(0, 10002).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
        using Track track = new(samples, 44100, 2);
        foreach (var range in new[] { (0, 1), (0, 64), (3, 77), (64, 129), (63, 4500), (200, 5001), (5001, 5001) })
        {
            var expected = samples[(range.Item1 * 2)..(range.Item2 * 2)];
            var extrema = track.GetWaveformExtrema(range.Item1, range.Item2);
            Near(extrema.Minimum, expected.Length == 0 ? 0 : expected.Min()); Near(extrema.Maximum, expected.Length == 0 ? 0 : expected.Max());
        }
        using Track dc = new(Constant(1000, 0.3f)); Near(dc.GetWaveformExtrema(0, 1000).Minimum, 0.3);
    }

    private static void PeakCacheEdits()
    {
        using Track track = new(Constant(1000, 1));
        Near(track.GetWaveformExtrema(0, 1).Maximum, 1);
        track.FadeIn(); Near(track.GetWaveformExtrema(0, 1).Maximum, 0);
        track.Reverse(); Near(track.GetWaveformExtrema(0, 1).Maximum, 1);
        track.Flip(); Near(track.GetWaveformExtrema(0, 1).Minimum, -1);
        track.Undo(); Near(track.GetWaveformExtrema(0, 1).Maximum, 1);
        track.ReplaceSamples(Constant(100, -0.5f, 2)); Near(track.GetWaveformExtrema(0, 100).Maximum, -0.5);
    }

    private static void PeakMetadataAllocations()
    {
        using Track track = new(Constant(100000)); track.GetWaveformExtrema(0, 100000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 30; i++)
        {
            track.SetVolume(i / 20f); track.MoveToFrame(i * 10); track.ToggleDisabled();
            Near(track.GetWaveformExtrema(0, 100000).Maximum, 0.25);
        }
        long allocation = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(allocation < 100000, $"Metadata edits rebuilt waveform peaks: {allocation} allocated bytes.");
    }

    private static Bitmap RenderTrack(Track track, double scale, double origin = 0, int width = 400, int height = 80)
    {
        using Panel panel = new() { Size = new Size(width, height), BackColor = Color.Black }; track.LinkPanels(panel, panel);
        Bitmap bitmap = new(width, height);
        using Graphics graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.Black);
        track.DrawSamples(graphics, scale, origin);
        return bitmap;
    }

    private static void SampleDetailRendering()
    {
        using Track track = new(new[] { 0f, 1f, 0f, -1f, 0f });
        using Bitmap bitmap = RenderTrack(track, 0.125, 0, 80, 80);
        Color wave = Settings.Color.TrackSamples_Enabled;
        Assert(bitmap.GetPixel(8, 2).ToArgb() == wave.ToArgb() && bitmap.GetPixel(24, 78).ToArgb() == wave.ToArgb(),
            "Individual positive/negative sample peaks were not drawn.");
        Assert(bitmap.GetPixel(12, 20).ToArgb() != Color.Black.ToArgb(), "Samples were not connected at high zoom.");
    }

    private static void OverviewRendering()
    {
        float[] samples = new float[2000]; samples[137] = 1; samples[499] = -1;
        using Track track = new(samples);
        using Bitmap bitmap = RenderTrack(track, 500, 0, 80, 80);
        Assert(bitmap.GetPixel(0, 2).ToArgb() != Color.Black.ToArgb() && bitmap.GetPixel(0, 78).ToArgb() != Color.Black.ToArgb(),
            "Overview skipped impulses between sampled columns.");
    }

    private static void FlatOverviewRendering()
    {
        foreach (float value in new[] { 0f, 0.5f, -0.5f })
        {
            using Track track = new(Constant(1000, value));
            using Bitmap bitmap = RenderTrack(track, 10, 0, 160, 80);
            int y = (int)(40 - value * 38);
            Assert(bitmap.GetPixel(40, y).ToArgb() != Color.Black.ToArgb(),
                $"Flat audio at {value} was invisible at overview scale.");
            Assert(bitmap.GetPixel(40, y + 3).ToArgb() == Color.Black.ToArgb() &&
                bitmap.GetPixel(110, y).ToArgb() == Color.Black.ToArgb(), "Flat audio was smeared outside its amplitude or clip.");
        }
    }

    private static void QuietBoundaryRendering()
    {
        using Track track = new(new float[100]); track.MoveToFrame(500);
        using Bitmap bitmap = RenderTrack(track, 10, 0, 100, 80);
        Assert(bitmap.GetPixel(50, 10).ToArgb() != Color.Black.ToArgb() && bitmap.GetPixel(60, 10).ToArgb() != Color.Black.ToArgb(),
            "A quiet clip's true start/end were not marked.");
        Assert(bitmap.GetPixel(49, 10).ToArgb() == Color.Black.ToArgb(), "Clip boundary was drawn before its timeline start.");
    }

    private static void ZoomRange()
    {
        double previous = double.PositiveInfinity;
        for (int value = 0; value <= 1000; value++)
        {
            double scale = ZoomScale.FromSlider(value);
            Assert(double.IsFinite(scale) && scale >= ZoomScale.Minimum && scale <= ZoomScale.Maximum && scale <= previous,
                "Zoom scale is invalid or jumps in the wrong direction.");
            Assert(Math.Abs(ZoomScale.ToSlider(scale) - value) <= 1, "Zoom slider does not round-trip.");
            previous = scale;
        }
        foreach (double value in new[] { 0, -1, double.NaN, double.PositiveInfinity }) Near(ZoomScale.Sanitize(value), 1);
        Near(ZoomScale.FromSlider(1000), 0.125);
    }

    private static void ZoomAnchor()
    {
        using TrackManager manager = new(null!); Track track = new(Constant(200000)); manager.AddTrack(track); manager.ClearHistory();
        manager.SetViewport(50000, 100, 400);
        double anchor = manager.ViewStartFrame + 173 * manager.DrawTimescale;
        foreach (double scale in new[] { 80.0, 10.0, 1.0, 0.125, 20.0, 100.0 })
        {
            manager.ZoomAtPixel(scale, 173, 400);
            Near(manager.ViewStartFrame + 173 * manager.DrawTimescale, anchor, 1e-6);
            Assert(track.StartFrame == 0 && !manager.CanUndo, "View zoom edited audio or history.");
        }
    }

    private static void HorizontalView()
    {
        using TrackManager manager = new(null!); Track track = new(Constant(100000)); manager.AddTrack(track); manager.ClearHistory();
        float[] before = manager.GetMixedSamples(); manager.SeekToFrame(20000);
        manager.SetViewport(double.MaxValue, 0.125, 400);
        Near(manager.ViewStartFrame, 99950); Near(manager.ViewStartFrame + 400 * manager.DrawTimescale, 100000);
        Assert(manager.GetPlayheadFrame() == 20000 && track.StartFrame == 0 && !manager.CanUndo, "Panning changed playback/audio/history.");
        Equal(manager.GetMixedSamples(), before, 0);
        manager.SetViewport(-100, double.NaN, 400); Near(manager.ViewStartFrame, 0); Near(manager.DrawTimescale, 1);
    }

    private static void ViewportCoordinates()
    {
        Near(TimelineScale.PixelAtFrame(50040, 0.125, 50000), 320);
        Assert(TimelineScale.FrameAtPixel(320, 0.125, 100000, 50000) == 50040, "Viewport pointer mapping lost its origin.");
        var ticks = TimelineScale.MajorTicks(1000, 100, 100, 50000);
        Assert(ticks.Any(t => t.Pixel >= 0 && t.Frame >= 50000), "Panned ruler omitted its absolute timestamps.");
        Assert(TimelineScale.FormatRulerTimestamp(23, 0.125) == "00:00.000521", "High-zoom ruler omitted sub-millisecond detail.");
    }

    private static void Wheel(Control control, int delta, int x)
        => typeof(Control).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, new object[] { new HandledMouseEventArgs(MouseButtons.None, 0, x, 20, delta) });

    private static void WheelZoomInterface() => WithTimeline((form, manager, _) =>
    {
        manager.SetViewport(5000, 10, Field<Panel>(form, "panTracks").ClientSize.Width); InvokeTimeline(form, "UpdateSizes");
        double frame = manager.ViewStartFrame + 150 * manager.DrawTimescale;
        Wheel(manager.Tracks[0].PanDrawSamples, 120, 150);
        Near(manager.DrawTimescale, 8); Near(manager.ViewStartFrame + 150 * manager.DrawTimescale, frame);
        Wheel(Field<TrackBar>(form, "trkDrawTimescale"), -120, 20); Near(manager.DrawTimescale, 10);
        Assert(manager.Tracks[0].StartFrame == 0, "Wheel zoom moved audio.");
    });

    private static void ViewNavigationInterface() => WithTimeline((form, manager, _) =>
    {
        manager.SetViewport(0, 1, Field<Panel>(form, "panTracks").ClientSize.Width); InvokeTimeline(form, "UpdateSizes");
        HScrollBar scroll = Field<HScrollBar>(form, "scrTimeline");
        InvokeTimeline(form, "scrTimeline_Scroll", scroll, new ScrollEventArgs(ScrollEventType.ThumbTrack, 1000000));
        Near(manager.ViewStartFrame + Field<Panel>(form, "panTracks").ClientSize.Width * manager.DrawTimescale, manager.TimelineExtentFrames);
        Field<Button>(form, "btnFitWaveform").PerformClick();
        // PerformClick on a hidden form may be suppressed; invoke its registered click path explicitly.
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Field<Button>(form, "btnFitWaveform"), new object[] { EventArgs.Empty });
        Near(manager.ViewStartFrame, 0);
        Assert(manager.TimelineExtentFrames <= Field<Panel>(form, "panTracks").ClientSize.Width * manager.DrawTimescale + 1,
            "Fit did not show the complete project.");
    });

    private static void PaintCanvas(Control canvas, Graphics graphics, Rectangle clip)
    {
        var state = graphics.Save(); graphics.SetClip(clip);
        try
        {
            using PaintEventArgs args = new(graphics, clip);
            typeof(Control).GetMethod("OnPaintBackground", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, new object[] { args });
            typeof(Control).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, new object[] { args });
        }
        finally { graphics.Restore(state); }
    }

    private static void CursorRepaint() => WithTimeline((form, manager, _) =>
    {
        Control canvas = manager.Tracks[0].PanDrawSamples; canvas.Size = new Size(400, 80);
        manager.SetViewport(0, 100, 400); manager.SeekToFrame(10000);
        using Bitmap partial = new(400, 80), fresh = new(400, 80);
        using Graphics a = Graphics.FromImage(partial), b = Graphics.FromImage(fresh);
        PaintCanvas(canvas, a, new Rectangle(0, 0, 400, 80));
        manager.SeekToFrame(20000);
        PaintCanvas(canvas, a, new Rectangle(97, 0, 7, 80)); PaintCanvas(canvas, a, new Rectangle(197, 0, 7, 80));
        PaintCanvas(canvas, b, new Rectangle(0, 0, 400, 80));
        for (int y = 0; y < 80; y++)
        for (int x = 0; x < 400; x++)
            Assert(partial.GetPixel(x, y).ToArgb() == fresh.GetPixel(x, y).ToArgb(), $"Cursor repaint left a stale pixel at {x},{y}.");
        Assert(!Field<Label>(form, "lblPlayhead").Visible && Field<Label>(form, "lblPlayhead").Parent is null,
            "A native overlay label still covers the waveform.");
    });

    private static void FullSliderDuringScrub() => WithTimeline((form, manager, _) =>
    {
        manager.SelectAllTracks(); float[] audio = manager.GetMixedSamples();
        Panel canvas = manager.Tracks[0].PanDrawSamples; canvas.Size = new Size(400, 80);
        TrackBar slider = Field<TrackBar>(form, "trkDrawTimescale");
        using Bitmap bitmap = new(400, 80); using Graphics graphics = Graphics.FromImage(bitmap);
        Pointer(canvas, "OnMouseDown", MouseButtons.Right, 20);
        foreach (int value in new[] { 0, 1, 250, 500, 750, 999, 1000, 0 })
        {
            slider.Value = value; ClickHandler(form, "trkDrawTimescale_Scroll");
            Pointer(canvas, "OnMouseMove", MouseButtons.Right, 20);
            PaintCanvas(canvas, graphics, new Rectangle(0, 0, 400, 80));
            Assert(double.IsFinite(manager.DrawTimescale) && manager.DrawTimescale >= 0.125 && manager.SelectedTracks.Count == 2,
                "Zoom-slider endpoint produced invalid view state while scrubbing.");
        }
        Pointer(canvas, "OnMouseUp", MouseButtons.Right, 20);
        Equal(manager.GetMixedSamples(), audio, 0);
    });

    private static void LongWaveformView()
    {
        float[] samples = new float[60 * AudioData.SampleRate];
        int origin = 45 * AudioData.SampleRate;
        samples[origin + 137] = 0.9f; samples[origin + 499] = -0.8f;
        using TrackManager manager = new(null!); Track track = new(samples); manager.AddTrack(track); manager.ClearHistory();
        long revision = track.Revision; manager.SeekToFrame(origin + 10000);
        var peaks = track.GetWaveformExtrema(origin, origin + 1000); Near(peaks.Maximum, 0.9); Near(peaks.Minimum, -0.8);
        foreach (double scale in new[] { 500.0, 20.0, 1.0, 0.125 })
        {
            manager.SetViewport(origin, scale, 400);
            using Bitmap bitmap = RenderTrack(track, scale, manager.ViewStartFrame);
            Assert(track.Revision == revision && manager.GetPlayheadFrame() == origin + 10000 && track.StartFrame == 0,
                "Long waveform navigation changed audio or playback time.");
        }
        Assert(TimelineScale.FormatTimestamp(manager.GetPlayheadFrame()) == "00:45.226" && !manager.CanUndo,
            "Long waveform timestamps/history changed while panning.");
    }

    private static void PannedEditAlignment() => WithTimeline((form, manager, _) =>
    {
        Track source = manager.Tracks[0];
        manager.SetViewport(5000, 10, Field<Panel>(form, "panTracks").ClientSize.Width);
        InvokeTimeline(form, "UpdateSizes");
        Pointer(source.PanDrawSamples, "OnMouseDown", MouseButtons.Left, 200);
        Pointer(source.PanDrawSamples, "OnMouseUp", MouseButtons.Left, 300);
        Assert(manager.RangeSelection == new TimelineSelection(source, 7000, 8000), "Panned range input lost the viewport origin.");
        manager.SeekToFrame(6750); Track tail = manager.SplitAtPlayhead();
        Near(TimelineScale.PixelAtFrame(tail.StartFrame, manager.DrawTimescale, manager.ViewStartFrame), 175);
        Assert(manager.GetPlayheadPos() == 175, "Panned split boundary differs from the visible playhead.");
        manager.Undo(); Assert(manager.RangeSelection == new TimelineSelection(source, 7000, 8000), "Panned split Undo lost the range.");
        manager.TrimToSelection();
        Assert(source.StartFrame == 7000 && source.EndFrame == 8000 && manager.ViewStartFrame == 5000,
            "Panned trim moved retained audio or the viewport.");
        using Bitmap canvas = new(400, 80);
        using Graphics graphics = Graphics.FromImage(canvas); graphics.Clear(Color.Black);
        InvokeTimeline(form, "PaintRangeSelection", source, graphics, new Size(400, 80));
        Assert(canvas.GetPixel(250, 40).ToArgb() != Color.Black.ToArgb() && canvas.GetPixel(150, 40).ToArgb() == Color.Black.ToArgb(),
            "Panned range painting lost the shared view origin.");
        Assert(Field<TimelineRuler>(form, "timelineRuler").ViewStartFrame == manager.ViewStartFrame, "Panned ruler uses a different origin.");
    });

    private static void ZoomDuringScrub() => WithTimeline((form, manager, _) =>
    {
        float[] before = manager.GetMixedSamples(); manager.SelectAllTracks();
        manager.SetViewport(5000, 10, Field<Panel>(form, "panTracks").ClientSize.Width); InvokeTimeline(form, "UpdateSizes");
        Panel canvas = manager.Tracks[0].PanDrawSamples;
        Pointer(canvas, "OnMouseDown", MouseButtons.Right, 150); long cursor = manager.GetPlayheadFrame();
        Wheel(canvas, 120, 150); Pointer(canvas, "OnMouseMove", MouseButtons.Right, 150); Pointer(canvas, "OnMouseUp", MouseButtons.Right, 150);
        Assert(manager.GetPlayheadFrame() == cursor && manager.SelectedTracks.Count == 2, "Zooming while scrubbing moved the anchored cursor or cleared selection.");
        Equal(manager.GetMixedSamples(), before, 0);
    });
}
