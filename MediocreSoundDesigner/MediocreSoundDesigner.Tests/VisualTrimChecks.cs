using System.Reflection;
using MediocreSoundDesigner;
using NAudio.Wave;

internal static partial class Program
{
    private static void RunVisualTrimChecks()
    {
        Run("Visual trim preserves exact stereo samples and absolute placement", ExactVisualTrim);
        Run("Range selection clamps and sorts without adding edit history", RangeBounds);
        Run("Empty and whole-track ranges make no trim edit", EmptyRangeTrim);
        Run("Undo and Redo restore trim audio, placement, and range", VisualTrimHistory);
        Run("Split Undo restores its prior range selection", RangeSplitHistory);
        Run("Range reconciliation follows track deletion and shorter edits", RangeReconciliation);
        Run("Left dragging selects in either direction without seeking", RangePointerDirections);
        Run("A selected range survives right-button scrubbing", RangeScrubSeparation);
        Run("Trim menu, readout, and waveform respond to selection", VisualTrimUserInterface);
        Run("Range bounds and highlights stay aligned through zoom and scrolling", RangeViewAlignment);
        Run("Range capture loss, release, and Escape leave no stuck drag", RangeCancellation);
        Run("Range highlighting paints only the selected track interval", RangePainting);
        Run("Optional numeric trim can initialize from a precise range", NumericTrimRange);
        Run("Paused visual trim rebuilds playback at the current cursor", PausedVisualTrim);
        Run("Playing trim refreshes audio and stops safely beyond its new end", PlayingVisualTrim);
        Run("Large timeline offsets retain exact trim-frame precision", LargeFrameTrim);
    }

    private static void ExactVisualTrim()
    {
        using TrackManager manager = new(null!);
        float[] original = Enumerable.Range(0, 1000).SelectMany(i => new[] { i / 1000f, -i / 1000f }).ToArray();
        Track source = new(original, 44100, 2); source.MoveToFrame(44100); source.SetVolume(0.5f);
        Track other = new(Constant(100)); other.MoveToFrame(30000);
        manager.AddTrack(source); manager.AddTrack(other); manager.SelectTrack(source); manager.ClearHistory();
        manager.SetRangeSelection(source, 44237, 44911);
        Assert(manager.TrimToSelection() && source.StartFrame == 44237 && source.EndFrame == 44911,
            "Visual trim closed a gap or used rounded seconds.");
        Equal(source.CloneSamples(), original[(137 * 2)..(811 * 2)], 0);
        Assert(other.StartFrame == 30000 && other.TimelineFrameCount == 100 && manager.Tracks.Count == 2,
            "Visual trim changed an unrelated track.");
        Near(source.Volume, 0.5);
        float[] mixed = manager.GetMixedSamples();
        Near(mixed[44237 * 2 - 2], 0); Near(mixed[44237 * 2], original[137 * 2] * 0.5);
        Assert(!manager.CanTrimSelection, "The already-trimmed whole range was offered again.");
    }

    private static void RangeBounds()
    {
        using TrackManager manager = new(null!);
        Track source = new(Constant(1000)); source.MoveToFrame(1000);
        manager.AddTrack(source); manager.SelectTrack(source); manager.ClearHistory();
        source.FadeIn(); manager.Undo();
        manager.SetRangeSelection(source, 1900, 1100);
        Assert(manager.RangeSelection == new TimelineSelection(source, 1100, 1900), "Backward range was not normalized.");
        manager.SetRangeSelection(source, long.MinValue, long.MaxValue);
        Assert(manager.RangeSelection == new TimelineSelection(source, 1000, 2000), "Range did not clamp to actual track audio.");
        Assert(!manager.CanUndo && manager.CanRedo, "Selecting a range changed audio history.");
        manager.SetRangeSelection(source, 500, 800);
        Assert(manager.RangeSelection is null, "Out-of-track range became valid audio.");
        manager.SetRangeSelection(source, 1200, 1800); manager.SelectTrack(null);
        Assert(manager.RangeSelection is null && !manager.CanTrimSelection, "Changing tracks left an active old range.");
    }

    private static void EmptyRangeTrim()
    {
        using TrackManager manager = new(null!);
        Track source = new(Constant(100)); manager.AddTrack(source); manager.SelectTrack(source); manager.ClearHistory();
        foreach (var range in new[] { (0L, 0L), (0L, 100L), (100L, 200L), (-10L, -1L) })
        {
            manager.SetRangeSelection(source, range.Item1, range.Item2);
            Assert(!manager.CanTrimSelection && !manager.TrimToSelection() && !manager.CanUndo,
                "Empty/full/invalid selection made an edit.");
        }
        manager.SetRangeSelection(null!, 0, 10);
        Assert(manager.RangeSelection is null, "Null range track was accepted.");
    }

    private static void VisualTrimHistory()
    {
        using TrackManager manager = new(null!);
        Track source = new(Constant(1000)); source.MoveToFrame(1000); manager.AddTrack(source); manager.SelectTrack(source); manager.ClearHistory();
        manager.SetRangeSelection(source, 1200, 1800); TimelineSelection selection = manager.RangeSelection;
        Assert(manager.TrimToSelection() && manager.Undo() && !manager.CanUndo, "Trim was not one complete Undo step.");
        Assert(source.StartFrame == 1000 && source.TimelineFrameCount == 1000 && manager.RangeSelection == selection && manager.CanTrimSelection,
            "Undo did not restore original audio and highlighted range.");
        Assert(manager.Redo() && source.StartFrame == 1200 && source.TimelineFrameCount == 600 && manager.RangeSelection == selection,
            "Redo did not restore the retained interval.");
    }

    private static void RangeSplitHistory()
    {
        using TrackManager manager = new(null!);
        Track source = new(Constant(1000)); manager.AddTrack(source); manager.SelectTrack(source); manager.ClearHistory();
        manager.SetRangeSelection(source, 100, 900); manager.SeekToFrame(500); manager.SplitAtPlayhead();
        Assert(manager.RangeSelection is null, "Selecting a split tail retained the original track's range.");
        Assert(manager.Undo() && manager.RangeSelection == new TimelineSelection(source, 100, 900), "Split Undo lost its prior range.");
        Assert(manager.Redo() && manager.RangeSelection is null, "Split Redo restored a stale range.");
    }

    private static void RangeReconciliation()
    {
        using TrackManager manager = new(null!);
        Track source = new(Constant(1000)); manager.AddTrack(source); manager.SelectTrack(source); manager.ClearHistory();
        manager.SetRangeSelection(source, 100, 900); manager.RemTrack(source);
        Assert(manager.RangeSelection is null, "Deleting a track retained its range.");
        manager.Undo(); Assert(manager.RangeSelection == new TimelineSelection(source, 100, 900), "Delete Undo did not restore the range.");
        source.ReplaceSamples(Constant(500, 0.25f, 2));
        Assert(manager.RangeSelection == new TimelineSelection(source, 100, 500), "An audio edit left the range past the new endpoint.");
        manager.Undo(); Assert(manager.RangeSelection == new TimelineSelection(source, 100, 900), "Audio edit Undo did not restore the range.");
    }

    private static void RangePointerDirections() => WithTimeline((form, manager, devices) =>
    {
        Track track = manager.Tracks[1];
        foreach (var pair in new[] { (120, 200), (200, 120) })
        {
            Pointer(track.PanDrawSamples, "OnMouseDown", MouseButtons.Left, pair.Item1);
            Pointer(track.PanDrawSamples, "OnMouseMove", MouseButtons.Left, pair.Item2);
            Pointer(track.PanDrawSamples, "OnMouseUp", MouseButtons.Left, pair.Item2);
            Assert(manager.RangeSelection == new TimelineSelection(track, 12000, 20000), "Left drag selected the wrong absolute interval.");
            Assert(manager.GetPlayheadFrame() == 0 && devices.Count == 0 && !manager.CanUndo, "Range drag sought or edited audio.");
        }
        Pointer(track.PanDrawSamples, "OnMouseDown", MouseButtons.Left, -200);
        Pointer(track.PanDrawSamples, "OnMouseUp", MouseButtons.Left, 1000);
        Assert(manager.RangeSelection == new TimelineSelection(track, track.StartFrame, track.EndFrame), "Pointer range did not clamp to moved audio.");
    });

    private static void RangeScrubSeparation() => WithTimeline((form, manager, _) =>
    {
        Track track = manager.Tracks[0]; manager.SetRangeSelection(track, 5000, 20000);
        Pointer(track.PanDrawSamples, "OnMouseDown", MouseButtons.Right, 100);
        Pointer(track.PanDrawSamples, "OnMouseMove", MouseButtons.Right, 150);
        Pointer(track.PanDrawSamples, "OnMouseUp", MouseButtons.Right, 150);
        Assert(manager.RangeSelection == new TimelineSelection(track, 5000, 20000) && manager.GetPlayheadFrame() == 15000,
            "Right scrubbing changed the range selection.");
    });

    private static void VisualTrimUserInterface()
    {
        using FrmMain form = new(); // Use actual production event wiring; never display or play audio.
        TrackManager manager = Field<TrackManager>(form, "trackManager");
        try
        {
            Track source = new(Constant(44100)); manager.AddTrack(source);
            ToolStripMenuItem trim = Field<ToolStripMenuItem>(form, "menuEditTrim");
            ToolStripMenuItem numeric = Field<ToolStripMenuItem>(form, "menuEditTrimAtTimes");
            manager.DrawTimescale = 100;
            Pointer(source.PanDrawSamples, "OnMouseDown", MouseButtons.Left, 100);
            Pointer(source.PanDrawSamples, "OnMouseMove", MouseButtons.Left, 300);
            Pointer(source.PanDrawSamples, "OnMouseUp", MouseButtons.Left, 300);
            Assert(trim.Enabled && trim.ShortcutKeys == (Keys.Control | Keys.T) && numeric.Enabled,
                "Visual trim controls did not enable after dragging.");
            string readout = Field<Label>(form, "lblSelectionRange").Text;
            Assert(readout.Contains("00:00.226") && readout.Contains("00:00.680") && readout.Contains("00:00.453"),
                "Range readout omitted absolute start/end/duration timestamps.");
            trim.PerformClick(); Assert(source.StartFrame == 10000 && source.EndFrame == 30000 && !trim.Enabled, "Trim menu did not keep the selected interval.");
            ClickHandler(form, "menuEditUndo_Click"); Assert(trim.Enabled && source.StartFrame == 0 && source.EndFrame == 44100, "Undo did not restore trim availability and waveform placement.");
            ClickHandler(form, "menuEditRedo_Click"); Assert(!trim.Enabled && source.StartFrame == 10000, "Redo did not restore the trim.");
        }
        finally { manager.Dispose(); }
    }

    private static void RangeViewAlignment() => WithTimeline((form, manager, _) =>
    {
        Track track = manager.Tracks[0]; manager.SetRangeSelection(track, 12000, 30000);
        TimelineSelection before = manager.RangeSelection;
        foreach (int scale in new[] { 1, 100, 500, 999 })
        {
            manager.DrawTimescale = scale; form.Size = new Size(900, 650); InvokeTimeline(form, "UpdateSizes");
            Assert(manager.RangeSelection == before && track.StartFrame == 0 && track.EndFrame == 44100,
                "Zoom/resize moved the range or audio.");
            Assert(Field<Label>(form, "lblSelectionRange").Bottom < Field<Panel>(form, "panTracks").Top,
                "Range readout overlaps the tracks.");
        }
        for (int i = 0; i < 10; i++) manager.AddTrack(new(Constant(4410)));
        Panel panel = Field<Panel>(form, "panTracks");
        typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(bool) }, null)!.Invoke(panel, new object[] { true });
        panel.AutoScrollPosition = new Point(0, 160); InvokeTimeline(form, "UpdateSizes");
        Assert(panel.AutoScrollPosition.Y < 0 && manager.RangeSelection == before &&
            Field<Label>(form, "lblSelectionRange").Width == Field<TimelineRuler>(form, "timelineRuler").Width,
            "Scrolling changed the range or readout alignment.");
    });

    private static void RangeCancellation() => WithTimeline((form, manager, _) =>
    {
        Panel wave = manager.Tracks[0].PanDrawSamples;
        Pointer(wave, "OnMouseDown", MouseButtons.Left, 50); Pointer(wave, "OnMouseMove", MouseButtons.Left, 100);
        InvokeTimeline(form, "Range_MouseCaptureChanged", wave, EventArgs.Empty);
        Pointer(wave, "OnMouseMove", MouseButtons.Left, 200);
        Assert(manager.RangeSelection?.EndFrame == 10000, "Lost capture left a stuck range drag.");
        Message message = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        object[] args = { message, Keys.Escape };
        form.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
        Assert(manager.RangeSelection is null, "Escape did not clear the range.");
        Pointer(wave, "OnMouseDown", MouseButtons.Left, 50); Pointer(wave, "OnMouseUp", MouseButtons.Left, 100);
        Pointer(wave, "OnMouseMove", MouseButtons.Left, 300);
        Assert(manager.RangeSelection?.EndFrame == 10000, "Mouse release left a stuck range drag.");
    });

    private static void RangePainting() => WithTimeline((form, manager, _) =>
    {
        Track track = manager.Tracks[0]; manager.SetRangeSelection(track, 10000, 20000);
        using Bitmap canvas = new(400, 80);
        using Graphics graphics = Graphics.FromImage(canvas); graphics.Clear(Color.Black);
        InvokeTimeline(form, "PaintRangeSelection", track, graphics, new Size(400, 80));
        Assert(canvas.GetPixel(150, 40).ToArgb() != Color.Black.ToArgb() && canvas.GetPixel(50, 40).ToArgb() == Color.Black.ToArgb() &&
            canvas.GetPixel(250, 40).ToArgb() == Color.Black.ToArgb(), "Selection overlay did not respect the selected interval.");
        graphics.Clear(Color.Black); InvokeTimeline(form, "PaintRangeSelection", manager.Tracks[1], graphics, new Size(400, 80));
        Assert(canvas.GetPixel(150, 40).ToArgb() == Color.Black.ToArgb(), "Selection overlay affected another track.");
    });

    private static void NumericTrimRange()
    {
        using Track track = new(Constant(44100));
        using FrmEditTrim dialog = new(); dialog.SetSound(track, 0.123456, 0.765432);
        Near(dialog.StartSeconds, 0.123456); Near(dialog.EndSeconds, 0.765432);
        Assert(Field<NumericUpDown>(dialog, "nudStart").DecimalPlaces == 6, "Numeric trim lost displayed precision.");
        dialog.SetSound(track, double.NaN, double.PositiveInfinity);
        Near(dialog.StartSeconds, 0); Near(dialog.EndSeconds, 1);
    }

    private static void PausedVisualTrim()
    {
        var (manager, source, devices) = PlaybackProject();
        using (manager)
        {
            manager.SelectTrack(source); manager.SeekToFrame(22050); manager.PlayAll(); manager.PauseAll();
            manager.SetRangeSelection(source, 11025, 33075); manager.TrimToSelection();
            Assert(manager.State == PlaybackState.Paused && manager.GetPlayheadFrame() == 22050, "Trim moved paused playback.");
            manager.PlayAll();
            Assert(devices.Count == 2 && devices[0].Disposed && manager.GetPlayheadFrame() == 22050, "Resume reused stale untrimmed audio.");
            byte[] frame = new byte[8]; devices[^1].Provider!.Read(frame); Near(BitConverter.ToSingle(frame, 0), 0.8);
        }
    }

    private static void PlayingVisualTrim()
    {
        foreach (long cursor in new long[] { 22050, 40000 })
        {
            var (manager, source, devices) = PlaybackProject();
            using (manager)
            {
                manager.SelectTrack(source); manager.SeekToFrame(cursor); manager.PlayAll(); manager.LoopPlayback = true;
                manager.SetRangeSelection(source, 11025, 33075); manager.TrimToSelection(); manager.UpdatePlayback();
                Assert(devices[0].Disposed, "Playing trim left the old audio output active.");
                if (cursor < 33075)
                    Assert(manager.State == PlaybackState.Playing && manager.GetPlayheadFrame() == cursor && devices.Count == 2,
                        "Playing trim did not continue from the preserved cursor in its fresh mix.");
                else
                    Assert(manager.State == PlaybackState.Stopped && manager.GetPlayheadFrame() == 33075 && devices.Count == 1,
                        "Trimming past the cursor played removed audio or automatically looped.");
            }
        }
    }

    private static void LargeFrameTrim()
    {
        using TrackManager manager = new(null!);
        Track track = new(Constant(20)); long start = (1L << 53) + 1;
        track.MoveToFrame(start); manager.AddTrack(track); manager.SelectTrack(track); manager.ClearHistory();
        manager.SetRangeSelection(track, start + 3, start + 17);
        Assert(manager.TrimToSelection() && track.StartFrame == start + 3 && track.EndFrame == start + 17,
            "Visual trim lost integer precision at a large timeline offset.");
    }
}
