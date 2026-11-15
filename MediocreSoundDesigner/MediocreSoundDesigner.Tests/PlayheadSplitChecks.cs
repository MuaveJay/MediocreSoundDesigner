using MediocreSoundDesigner;
using NAudio.Wave;

internal static partial class Program
{
    private static void RunPlayheadSplitChecks()
    {
        Run("Playhead splitting cuts at the exact selected stereo frame", ExactPlayheadSplit);
        Run("Moved and repeated playhead splits retain absolute placement at every zoom", MovedPlayheadSplits);
        Run("Invalid playhead split boundaries preserve Redo and audio", InvalidPlayheadSplits);
        Run("Two-frame tracks can split without empty sections", MinimalPlayheadSplit);
        Run("Playhead split preserves volume, disabled state, and channels", PlayheadSplitSettings);
        Run("One Undo restores the full split and preserves earlier fades", PlayheadSplitHistory);
        Run("Splitting preserves playing, paused, and stopped transport state", PlayheadSplitPlayback);
        Run("Optional numeric splitting rounds once to the sample grid", NumericSplitPrecision);
        Run("Split menu directly uses the cursor and restores both rows on Redo", PlayheadSplitUserInterface);
        Run("Moved split boundaries line up with the displayed playhead", SplitBoundaryAlignment);
        Run("Imported audio splits at a non-round displayed timestamp", ImportedPlayheadSplit);
        Run("Large timeline positions retain integer-frame split precision", LargeFrameSplit);
    }

    private static void ExactPlayheadSplit()
    {
        using TrackManager manager = new(null!);
        float[] original = Enumerable.Range(0, 1000).SelectMany(i => new[] { i / 1000f, -i / 1000f }).ToArray();
        Track source = new(original, 44100, 2);
        manager.AddTrack(source); manager.SelectTrack(source); manager.ClearHistory();
        manager.SeekToFrame(337);
        Track tail = manager.SplitAtPlayhead() ?? throw new InvalidOperationException("Split did not create a tail.");
        Assert(manager.Tracks.Count == 2 && manager.SelectedTrack == tail, "Split did not create and select a second track.");
        Assert(source.TimelineFrameCount == 337 && tail.StartFrame == 337 && tail.TimelineFrameCount == 663,
            "Split used a rounded timestamp instead of the exact frame.");
        Equal(source.CloneSamples(), original[..674], 0); Equal(tail.CloneSamples(), original[674..], 0);
        Equal(manager.GetMixedSamples(), original, 0);
        Assert(manager.GetPlayheadFrame() == 337, "Split moved the cursor.");
    }

    private static void MovedPlayheadSplits()
    {
        foreach (int zoom in new[] { 1, 100, 500, 999 })
        {
            using TrackManager manager = new(null!);
            Track source = new(Constant(44100)); source.MoveToFrame(44100);
            manager.AddTrack(source); manager.SelectTrack(source); manager.DrawTimescale = zoom; manager.ClearHistory();
            float[] baseline = manager.GetMixedSamples();
            manager.SeekToFrame(56445); Track middle = manager.SplitAtPlayhead();
            Assert(middle.StartFrame == 56445 && source.EndFrame == middle.StartFrame, "Moved split created a gap or overlapping boundary.");
            manager.SeekToFrame(70001); Track tail = manager.SplitAtPlayhead();
            Assert(tail.StartFrame == 70001 && middle.EndFrame == tail.StartFrame && tail.EndFrame == 88200,
                "Repeated split positions drifted.");
            Equal(manager.GetMixedSamples(), baseline, 0);
        }
    }

    private static void InvalidPlayheadSplits()
    {
        using TrackManager manager = new(null!);
        Track source = new(Constant(100)); source.MoveToFrame(100);
        manager.AddTrack(source); manager.SelectTrack(source); manager.ClearHistory();
        source.FadeIn(); manager.Undo();
        float[] before = manager.GetMixedSamples();
        foreach (long frame in new long[] { -1, 0, 99, 100, 200, 201, long.MaxValue })
        {
            Assert(!manager.CanSplitTrackAtFrame(source, frame) && manager.SplitTrackAtFrame(source, frame) is null,
                "Invalid or empty section was accepted.");
            Assert(manager.CanRedo && !manager.CanUndo && manager.Tracks.Count == 1, "Invalid split changed history.");
        }
        manager.SeekToFrame(100);
        Assert(!manager.CanSplitAtPlayhead && manager.SplitAtPlayhead() is null, "Playhead at track start was accepted.");
        manager.SelectTrack(null);
        Assert(!manager.CanSplitAtPlayhead && manager.SplitAtPlayhead() is null, "Split accepted no selected track.");
        using Track outsider = new(Constant(100));
        Assert(manager.SplitTrackAtFrame(outsider, 50) is null, "Split modified a track outside the project.");
        Equal(manager.GetMixedSamples(), before, 0);
    }

    private static void MinimalPlayheadSplit()
    {
        using TrackManager manager = new(null!);
        Track source = new(new[] { 0.1f, -0.1f, 0.2f, -0.2f }, 44100, 2);
        manager.AddTrack(source); manager.SelectTrack(source); manager.ClearHistory(); manager.SeekToFrame(1);
        Track tail = manager.SplitAtPlayhead();
        Assert(source.TimelineFrameCount == 1 && tail.TimelineFrameCount == 1 && tail.StartFrame == 1, "Minimal split produced an empty section.");
        Equal(tail.CloneSamples(), new[] { 0.2f, -0.2f });
        Assert(!manager.CanSplitAtPlayhead, "One-frame tail was offered as splittable.");
    }

    private static void PlayheadSplitSettings()
    {
        using TrackManager manager = new(null!);
        Track source = new(Enumerable.Range(0, 1000).SelectMany(i => new[] { i / 1000f, -i / 1000f }).ToArray(), 44100, 2);
        source.MoveToFrame(100); source.SetVolume(0.35f); source.ToggleDisabled();
        manager.AddTrack(source); manager.AddTrack(new(Constant(2000))); manager.SelectTrack(source); manager.ClearHistory();
        manager.SeekToFrame(437); Track tail = manager.SplitAtPlayhead();
        Assert(tail.IsDisabled && source.IsDisabled && tail.Channels == 2 && tail.SampleRate == 44100 && tail.StartFrame == 437,
            "Split lost track settings or channel alignment.");
        Near(tail.Volume, 0.35); Near(source.Volume, 0.35);
        Near(tail.CloneSamples()[0], 0.337); Near(tail.CloneSamples()[1], -0.337);
    }

    private static void PlayheadSplitHistory()
    {
        using TrackManager manager = new(null!);
        Track source = new(Constant(4410)); manager.AddTrack(source); manager.SelectTrack(source); manager.ClearHistory();
        float[] original = source.CloneSamples(); source.FadeIn(); float[] faded = source.CloneSamples();
        manager.SeekToFrame(1337); Track tail = manager.SplitAtPlayhead();
        Assert(manager.Undo() && manager.Tracks.Count == 1 && manager.SelectedTrack == source, "Split Undo did not restore the original track and selection.");
        Equal(source.CloneSamples(), faded, 0);
        Assert(manager.GetPlayheadFrame() == 1337 && manager.Undo() && !manager.CanUndo, "Split moved the cursor or erased earlier history.");
        Equal(source.CloneSamples(), original, 0);
        Assert(manager.Redo() && manager.Redo() && manager.Tracks[1] == tail && tail.StartFrame == 1337,
            "Complete split Redo failed.");
    }

    private static void PlayheadSplitPlayback()
    {
        foreach (PlaybackState state in new[] { PlaybackState.Stopped, PlaybackState.Playing, PlaybackState.Paused })
        {
            var (manager, source, devices) = PlaybackProject();
            using (manager)
            {
                manager.SelectTrack(source); manager.SeekToFrame(11025);
                if (state != PlaybackState.Stopped) manager.PlayAll();
                if (state == PlaybackState.Paused) manager.PauseAll();
                int count = devices.Count;
                Track tail = manager.SplitAtPlayhead();
                Assert(tail.StartFrame == 11025 && manager.GetPlayheadFrame() == 11025 && manager.State == state && devices.Count == count,
                    "Split changed transport state, cursor, or active device.");
                if (state == PlaybackState.Playing) manager.PauseAll();
                manager.PlayAll();
                Assert(manager.GetPlayheadFrame() == 11025, "Play after split lost the selected position.");
                byte[] frame = new byte[8]; devices[^1].Provider!.Read(frame); Near(BitConverter.ToSingle(frame, 0), 0.8);
            }
        }
    }

    private static void NumericSplitPrecision()
    {
        using TrackManager manager = new(null!);
        Track source = new(Constant(44100)); source.MoveToFrame(44100); manager.AddTrack(source); manager.SelectTrack(source);
        using FrmEditSplit dialog = new(); dialog.SetSound(source, 0.123456);
        NumericUpDown input = Field<NumericUpDown>(dialog, "nudSplit");
        Assert(input.DecimalPlaces == 6 && input.Increment == 0.001m, "Optional numeric input lost precision.");
        Near(dialog.SplitSeconds, 0.123456);
        Track tail = manager.SplitTrack(source, dialog.SplitSeconds);
        Assert(tail.StartFrame == 44100 + (long)Math.Round(0.123456 * 44100, MidpointRounding.AwayFromZero),
            "Numeric split did not round exactly once to the sample grid.");
        using FrmEditSplit fallback = new(); fallback.SetSound(tail, double.NaN);
        Near(fallback.SplitSeconds, tail.DurationSeconds / 2);
    }

    private static void PlayheadSplitUserInterface() => WithTimeline((form, manager, _) =>
    {
        Track source = manager.Tracks[0];
        ToolStripMenuItem direct = Field<ToolStripMenuItem>(form, "menuEditSplit");
        ToolStripMenuItem numeric = Field<ToolStripMenuItem>(form, "menuEditSplitAtTime");
        Assert(direct.Text == "Split at Playhead" && direct.ShortcutKeys == (Keys.Control | Keys.K) &&
            numeric.ShortcutKeys == (Keys.Control | Keys.Shift | Keys.K), "Split menu commands or shortcuts are incorrect.");
        Assert(!direct.Enabled && numeric.Enabled, "Boundary split should be disabled while numeric entry remains available.");
        Pointer(source.PanDrawSamples, "OnMouseDown", MouseButtons.Right, 200);
        Pointer(source.PanDrawSamples, "OnMouseUp", MouseButtons.Right, 200);
        Assert(direct.Enabled, "Valid cursor did not enable Split at Playhead.");
        direct.PerformClick();
        Track tail = manager.SelectedTrack;
        Assert(manager.Tracks.Count == 3 && tail.StartFrame == 20000 && tail.PanDrawSamples.Parent == source.PanDrawSamples.Parent,
            "Menu did not split directly at the cursor or show both tracks.");
        Assert(!direct.Enabled, "Split should be disabled at the new tail's start.");
        ClickHandler(form, "menuEditUndo_Click");
        Assert(manager.Tracks.Count == 2 && direct.Enabled && manager.SelectedTrack == source, "Undo did not restore rows and split availability.");
        ClickHandler(form, "menuEditRedo_Click");
        Assert(manager.Tracks.Count == 3 && tail.PanDrawSamples.Parent is not null, "Redo did not restore the tail row.");
        manager.SelectTrack(null); ClickHandler(form, "tmrUpdatePlaybackState_Tick");
        Assert(!direct.Enabled && !numeric.Enabled, "Split commands stayed enabled without a selected track.");
    });

    private static void SplitBoundaryAlignment() => WithTimeline((form, manager, _) =>
    {
        Track source = manager.Tracks[1]; manager.SelectTrack(source);
        float[] original = manager.GetMixedSamples();
        foreach (int scale in new[] { 1, 100, 500, 999 })
        {
            manager.DrawTimescale = scale; manager.SeekToFrame(15001); ClickHandler(form, "tmrUpdatePlaybackState_Tick");
            Track tail = manager.SplitAtPlayhead();
            Label line = Field<Label>(form, "lblPlayhead");
            Assert(tail.StartFrame == 15001 && source.EndFrame == tail.StartFrame &&
                tail.StartFrame / scale == manager.GetPlayheadPos(), "Split boundary used a different grid from the playhead.");
            if (manager.GetPlayheadPos() < Field<Panel>(form, "panTracks").ClientSize.Width - line.Width)
                Assert(line.Left == tail.StartFrame / scale, "Displayed playhead and new track start differ.");
            Equal(manager.GetMixedSamples(), original, 0);
            Assert(manager.Undo(), "Boundary test could not undo its split.");
        }
    });

    private static void ImportedPlayheadSplit()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        try
        {
            using (WaveFileWriter writer = new(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)))
            {
                float[] audio = Enumerable.Range(0, 96000).SelectMany(i => new[] {
                    (float)(0.5 * Math.Sin(i * 0.05)), (float)(0.3 * Math.Cos(i * 0.07)) }).ToArray();
                writer.WriteSamples(audio, 0, audio.Length);
            }
            using TrackManager manager = new(null!);
            Track source = new(path); manager.AddTrack(source); manager.SelectTrack(source); manager.ClearHistory();
            float[] original = manager.GetMixedSamples();
            manager.SeekToFrame(58962); Track tail = manager.SplitAtPlayhead();
            Assert(TimelineScale.FormatTimestamp(tail.StartFrame) == "00:01.337" && tail.StartFrame == 58962,
                "Imported split did not match the non-round displayed timestamp.");
            Equal(tail.CloneSamples(), original[(58962 * 2)..], 0);
            Equal(manager.GetMixedSamples(), original, 0);
        }
        finally { File.Delete(path); }
    }

    private static void LargeFrameSplit()
    {
        using TrackManager manager = new(null!);
        Track source = new(Constant(20));
        long start = (1L << 53) + 1;
        source.MoveToFrame(start); manager.AddTrack(source); manager.SelectTrack(source); manager.ClearHistory();
        manager.SeekToFrame(start + 7); Track tail = manager.SplitAtPlayhead();
        Assert(tail.StartFrame == start + 7 && source.TimelineFrameCount == 7 && tail.TimelineFrameCount == 13,
            "Project split position lost integer precision through seconds conversion.");
    }
}
