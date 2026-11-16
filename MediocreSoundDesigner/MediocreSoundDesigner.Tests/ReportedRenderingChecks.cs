using MediocreSoundDesigner;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

internal static partial class Program
{
    private static void RunReportedRenderingChecks()
    {
        Run("The reported gradient-noise workflow cuts exactly at 1.337 seconds", ReportedGradientSplit);
        Run("Repository WAV files retain exact samples and visible split boundaries", ReportedImportedSplits);
        Run("Quiet fractional-pixel clip boundaries share the ruler and playhead column", ReportedQuietBoundary);
        Run("Actual invalidation rectangles leave no trails while right-dragging split noise", ReportedScrubRepaint);
        Run("Sample-detail right dragging repaints identically to a fresh frame", ReportedSampleRepaint);
        Run("Zoom-slider and wheel changes during scrubbing leave every row clean", ReportedZoomRepaint);
        Run("Bottom playback slider right-button gestures keep split rows clean", ReportedPositionSliderRepaint);
        Run("Playing and paused split-track seeking retains clean rendering and audio", ReportedPlaybackRepaint);
        Run("Split Undo/Redo, moved rows and scrolling retain visible boundary alignment", ReportedBoundaryHistory);
    }

    private static float[] ReportedNoise(int frames = 250000)
    {
        Random random = new(403); float value = 0; int direction = 1;
        float[] samples = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            value += 0.005f * direction;
            if (random.NextSingle() < 0.5f) direction *= -1;
            if (value > 0.9f) direction = -1;
            if (value < -0.9f) direction = 1;
            samples[i] = value;
        }
        return samples;
    }

    private static void ReportedGradientSplit() => WithTimeline((form, manager, _) =>
    {
        ClickHandler(form, "menuGenGradientNoise_Click");
        Track source = manager.Tracks.Single(); float[] original = source.CloneSamples();
        Assert(source.TimelineFrameCount == 250000 && TimelineScale.FormatTimestamp(manager.DurationFrames) == "00:05.668", "Report fixture does not match the generator duration.");
        manager.SeekToFrame(58962); ClickHandler(form, "menuEditSplit_Click");
        Track tail = manager.Tracks[1];
        Assert(source.EndFrame == 58962 && tail.StartFrame == 58962 && manager.GetPlayheadFrame() == 58962 &&
            TimelineScale.FormatTimestamp(manager.GetPlayheadFrame()) == "00:01.337", "Split did not use the screenshot's exact sample frame.");
        Equal(manager.GetMixedSamples(), original, 0);
        manager.Undo(); Equal(source.CloneSamples(), original, 0); manager.Redo(); Equal(manager.GetMixedSamples(), original, 0);
    }, addTracks: false);

    private static void ReportedImportedSplits()
    {
        foreach (string name in new[] { "beat-it.wav", "silence.wav", "So-Tired-forward.wav" })
        {
            using TrackManager manager = new(null!); Track source = new(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
            source.MoveToFrame(321); manager.AddTrack(source); manager.SelectTrack(source); manager.ClearHistory();
            float[] original = manager.GetMixedSamples(); manager.SeekToFrame(58962); Track tail = manager.SplitAtPlayhead();
            Assert(tail.StartFrame == 58962 && source.EndFrame == tail.StartFrame, "Imported split placement differs from the cursor.");
            Equal(manager.GetMixedSamples(), original, 0);
            foreach (double scale in new[] { 500.0, 83.7, 1.0, 0.25, 0.125 })
            {
                double origin = scale >= 500 ? 0 : 58962 - scale * 100.375;
                using Bitmap image = RenderTrack(tail, scale, origin, 400, 80);
                int x = (int)Math.Floor(TimelineScale.PixelAtFrame(tail.StartFrame, scale, origin));
                Assert(image.GetPixel(x, 5).ToArgb() != Color.Black.ToArgb(), name + " lacks a visible boundary at the true frame.");
            }
            manager.Undo(); manager.Redo(); Equal(manager.GetMixedSamples(), original, 0);
        }
    }

    private static void ReportedQuietBoundary() => WithTimeline((form, manager, _) =>
    {
        Track source = new(new float[250000]); manager.AddTrack(source); manager.SelectTrack(source);
        manager.SeekToFrame(58962); Track tail = manager.SplitAtPlayhead();
        foreach (double scale in new[] { 500.0, 83.7, 37.25, 1.0, 0.5, 0.25, 0.125 })
        {
            double origin = scale >= 500 ? 0 : 58962 - scale * 100.375;
            manager.SetViewport(origin, scale, 400);
            InvokeTimeline(form, "UpdateTransportControls");
            TimelineRuler ruler = Field<TimelineRuler>(form, "timelineRuler");
            ruler.Size = new Size(400, 40);
            using Bitmap rulerImage = new(400, 40);
            using (Graphics rulerGraphics = Graphics.FromImage(rulerImage)) PaintCanvas(ruler, rulerGraphics, ruler.ClientRectangle);
            using Bitmap bitmap = RenderTrack(tail, scale, manager.ViewStartFrame, 400, 80);
            int x = manager.GetPlayheadPos();
            Assert(bitmap.GetPixel(x, 5).ToArgb() != Color.Black.ToArgb(), "Quiet boundary is not on the playhead's column.");
            using Graphics graphics = Graphics.FromImage(bitmap);
            InvokeTimeline(form, "PaintPlayhead", graphics, new Size(400, 80));
            Assert(bitmap.GetPixel(x, 5).ToArgb() == Settings.Color.Playline.ToArgb(), "Playhead and true clip edge differ.");
            Assert(rulerImage.GetPixel(x, 38).ToArgb() == Settings.Color.Playline.ToArgb(), "Ruler indicator and clip boundary differ.");
        }
    }, addTracks: false);

    private sealed class RepaintProbe : IDisposable
    {
        private readonly Control canvas;
        private readonly List<Rectangle> dirty = new();
        private readonly Bitmap image;
        private readonly InvalidateEventHandler handler;
        public RepaintProbe(Control canvas)
        {
            this.canvas = canvas;
            image = new(canvas.ClientSize.Width, canvas.ClientSize.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(image)) PaintCanvas(canvas, graphics, canvas.ClientRectangle);
            handler = (sender, args) => dirty.Add(args.InvalidRect);
            canvas.Invalidated += handler;
        }
        public void Verify(string step)
        {
            Assert(canvas.ClientSize == image.Size, "Probe resized unexpectedly.");
            using (Graphics graphics = Graphics.FromImage(image))
                foreach (Rectangle rectangle in dirty)
                {
                    Rectangle clip = Rectangle.Intersect(rectangle, canvas.ClientRectangle);
                    if (!clip.IsEmpty) PaintCanvas(canvas, graphics, clip);
                }
            using Bitmap fresh = new(image.Width, image.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(fresh)) PaintCanvas(canvas, graphics, canvas.ClientRectangle);
            byte[] previous = Pixels(image), expected = Pixels(fresh);
            if (!previous.AsSpan().SequenceEqual(expected))
            {
                image.Save(Path.Combine(AppContext.BaseDirectory, "repaint-actual.png"));
                fresh.Save(Path.Combine(AppContext.BaseDirectory, "repaint-expected.png"));
                throw new InvalidOperationException("Partial repaint left a stale pixel during " + step + "; dirty rectangles=" + dirty.Count);
            }
            dirty.Clear();
        }
        private static byte[] Pixels(Bitmap bitmap)
        {
            BitmapData locked = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] bytes = new byte[locked.Stride * locked.Height]; Marshal.Copy(locked.Scan0, bytes, 0, bytes.Length); return bytes;
            }
            finally { bitmap.UnlockBits(locked); }
        }
        public void Dispose() { canvas.Invalidated -= handler; image.Dispose(); }
    }

    private static void WithReportedRows(Action<FrmMain, TrackManager, Track[]> check)
        => WithTimeline((form, manager, _) =>
        {
            form.ClientSize = new Size(800, 650);
            Track source = new(ReportedNoise()); manager.AddTrack(source); manager.SelectTrack(source);
            manager.SeekToFrame(58962); manager.SplitAtPlayhead(); manager.SeekToFrame(140000); manager.SplitAtPlayhead();
            manager.SelectAllTracks(); manager.ClearHistory();
            Panel parent = Field<Panel>(form, "panTracks");
            typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(bool) }, null)!.Invoke(parent, new object[] { true });
            manager.SetViewport(0, 500, parent.ClientSize.Width); InvokeTimeline(form, "UpdateSizes");
            check(form, manager, manager.Tracks.ToArray());
        }, addTracks: false);

    private static void ReportedScrubRepaint() => WithReportedRows((form, manager, rows) =>
    {
        RepaintProbe[] probes = rows.Select(row => new RepaintProbe(row.PanDrawSamples)).ToArray();
        float[] original = manager.GetMixedSamples();
        try
        {
            foreach (int row in new[] { 0, 1, 2 })
            {
                Pointer(rows[row].PanDrawSamples, "OnMouseDown", MouseButtons.Right, 117);
                foreach (int x in new[] { 118, 120, 195, 267, 281, 399, 500, 300, 118, 30, 0 })
                {
                    Pointer(rows[row].PanDrawSamples, "OnMouseMove", MouseButtons.Right, x);
                    foreach (var probe in probes) probe.Verify("right-drag " + row + ":" + x);
                }
                Pointer(rows[row].PanDrawSamples, "OnMouseUp", MouseButtons.Right, 117);
                foreach (var probe in probes) probe.Verify("right release");
            }
            Equal(manager.GetMixedSamples(), original, 0);
            Assert(manager.SelectedTracks.Count == 3 && !manager.CanUndo, "Scrubbing changed tracks or history.");
        }
        finally { foreach (var probe in probes) probe.Dispose(); }
    });

    private static void ReportedSampleRepaint() => WithReportedRows((form, manager, rows) =>
    {
        foreach (double scale in new[] { 1.0, 0.5, 0.25, 0.125 })
        {
            manager.SetViewport(58962 - 100.375 * scale, scale, Field<Panel>(form, "panTracks").ClientSize.Width);
            InvokeTimeline(form, "UpdateTransportControls");
            RepaintProbe[] probes = rows.Select(row => new RepaintProbe(row.PanDrawSamples)).ToArray();
            try
            {
                Pointer(rows[1].PanDrawSamples, "OnMouseDown", MouseButtons.Right, 100);
                foreach (int x in new[] { 101, 102, 103, 108, 115, 137, 240, 500, 130, 105, 100 })
                {
                    Pointer(rows[1].PanDrawSamples, "OnMouseMove", MouseButtons.Right, x);
                    foreach (var probe in probes) probe.Verify("sample scale " + scale + ":" + x);
                }
                Pointer(rows[1].PanDrawSamples, "OnMouseUp", MouseButtons.Right, 100);
            }
            finally { foreach (var probe in probes) probe.Dispose(); }
        }
    });

    private static void ReportedZoomRepaint() => WithReportedRows((form, manager, rows) =>
    {
        RepaintProbe[] probes = rows.Select(row => new RepaintProbe(row.PanDrawSamples)).ToArray();
        TrackBar zoom = Field<TrackBar>(form, "trkDrawTimescale");
        try
        {
            Pointer(rows[1].PanDrawSamples, "OnMouseDown", MouseButtons.Right, 117);
            foreach (int value in new[] { 0, 250, 500, 750, 999, 1000, 999, 600, 300, 0 })
            {
                zoom.Value = value; ClickHandler(form, "trkDrawTimescale_Scroll");
                Wheel(rows[1].PanDrawSamples, 120, 117);
                Pointer(rows[1].PanDrawSamples, "OnMouseMove", MouseButtons.Right, 117);
                foreach (var probe in probes) probe.Verify("zoom slider " + value);
            }
            Pointer(rows[1].PanDrawSamples, "OnMouseUp", MouseButtons.Right, 117);
        }
        finally { foreach (var probe in probes) probe.Dispose(); }
    });

    private static void ReportedPositionSliderRepaint() => WithReportedRows((form, manager, rows) =>
    {
        RepaintProbe[] probes = rows.Select(row => new RepaintProbe(row.PanDrawSamples)).ToArray();
        TrackBar position = Field<TrackBar>(form, "trkPlaybackPosition"); float[] audio = manager.GetMixedSamples();
        try
        {
            Pointer(position, "OnMouseDown", MouseButtons.Right, 10);
            foreach (int value in new[] { 0, 1, 2360, 5340, 9000, 10000, 5340, 2360, 0 })
            {
                position.Value = value; ClickHandler(form, "trkPlaybackPosition_Scroll");
                foreach (var probe in probes) probe.Verify("position slider " + value);
            }
            Pointer(position, "OnMouseUp", MouseButtons.Right, 10);
            foreach (var probe in probes) probe.Verify("position slider release");
            Equal(manager.GetMixedSamples(), audio, 0);
            Assert(manager.SelectedTracks.Count == 3 && !manager.CanUndo, "Position-slider gesture edited audio/selection/history.");
        }
        finally { foreach (var probe in probes) probe.Dispose(); }
    });

    private static void ReportedPlaybackRepaint() => WithReportedRows((form, manager, rows) =>
    {
        manager.PlayAll(); InvokeTimeline(form, "UpdateTransportControls");
        RepaintProbe[] probes = rows.Select(row => new RepaintProbe(row.PanDrawSamples)).ToArray(); float[] audio = manager.GetMixedSamples();
        try
        {
            foreach (bool paused in new[] { false, true })
            {
                if (paused) manager.PauseAll();
                foreach (int x in new[] { 30, 117, 118, 237, 280, 390, 120 })
                {
                    Pointer(rows[1].PanDrawSamples, "OnMouseDown", MouseButtons.Right, x);
                    Pointer(rows[1].PanDrawSamples, "OnMouseUp", MouseButtons.Right, x);
                    foreach (var probe in probes) probe.Verify("playback seek " + paused + ":" + x);
                    Assert(manager.GetPlayheadFrame() == x * 500 && manager.State == (paused ? NAudio.Wave.PlaybackState.Paused : NAudio.Wave.PlaybackState.Playing),
                        "Scrubbing changed transport state or timeline position.");
                }
            }
            Equal(manager.GetMixedSamples(), audio, 0);
        }
        finally { foreach (var probe in probes) probe.Dispose(); }
    });

    private static void ReportedBoundaryHistory() => WithReportedRows((form, manager, rows) =>
    {
        Track middle = rows[1]; manager.SelectTrack(middle); manager.SeekToFrame(90001);
        Track tail = manager.SplitAtPlayhead(); float[] expected = manager.GetMixedSamples();
        manager.MoveTrack(tail, 0); manager.Undo(); manager.Undo(); manager.Redo(); manager.Redo();
        Assert(tail.StartFrame == 90001 && middle.EndFrame == 90001 && manager.Tracks[0] == tail, "Undo/Redo changed the visible cut or row identity.");
        Equal(manager.GetMixedSamples(), expected, 0);
        Panel parent = Field<Panel>(form, "panTracks"); parent.AutoScrollPosition = new Point(0, 100);
        manager.SetViewport(85000.25, 37.25, parent.ClientSize.Width); InvokeTimeline(form, "UpdateSizes");
        using Bitmap marker = RenderTrack(tail, manager.DrawTimescale, manager.ViewStartFrame, parent.ClientSize.Width, 80);
        int x = manager.GetPlayheadPos(); Assert(marker.GetPixel(x, 5).ToArgb() != Color.Black.ToArgb(), "Scrolled/reordered boundary no longer shares the playhead column.");
    });
}
