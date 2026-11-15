using System.Reflection;
using MediocreSoundDesigner;
using NAudio.Wave;

internal static partial class Program
{
    private static void RunTransportChecks()
    {
        Run("Stopped seeking stores a cursor without opening a device", StoppedSeek);
        Run("Playback starts at the cursor with channel-aligned audio", PlayAtCursor);
        Run("Playing seek continues from the new cursor", PlayingSeek);
        Run("Paused seek stays paused and resumes from its new cursor", PausedSeek);
        Run("Stop resets playing and paused playback without looping", StopTransport);
        Run("Natural completion preserves the end cursor and Play restarts", NaturalCompletion);
        Run("Looping restarts only after natural completion", LoopCompletion);
        Run("Seeking to the end cancels pending automatic looping", SeekEnd);
        Run("Empty and out-of-range seeks remain safe", TransportBounds);
        Run("Edited stopped and paused seeks play current audio", SeekAfterEdits);
        Run("Undo invalidates the mix without moving the transport cursor", TransportHistory);
        Run("Transport disposal releases current and replaced devices", TransportDisposal);
        Run("A playback initialization failure never triggers looping", TransportFailure);
        Run("Transport uses absolute project frames including leading silence", OffsetTransport);
        Run("Removing all audio stops playback at the beginning", EmptyDuringPlayback);
        Run("Transport buttons, slider, and timestamps share the same state", TransportUserInterface);
        Run("Transport timestamps retain hours and millisecond precision", TimestampFormatting);
    }

    private static void StoppedSeek()
    {
        var (manager, _, devices) = PlaybackProject();
        using (manager)
        {
            manager.SeekToFrame(11025);
            Assert(devices.Count == 0 && manager.State == PlaybackState.Stopped, "Stopped seek opened an audio device.");
            Assert(manager.GetPlayheadFrame() == 11025, "Stopped cursor was not stored.");
            manager.DrawTimescale = 25;
            Assert(manager.GetPlayheadPos() == 441 && manager.GetPlayheadFrame() == 11025, "Zoom changed the transport cursor.");
            manager.UpdatePlayback();
            Assert(devices.Count == 0, "Polling unexpectedly started playback.");
        }
    }

    private static void PlayAtCursor()
    {
        List<FakeOutput> devices = new();
        using TrackManager manager = new(null!, () => { FakeOutput d = new(); devices.Add(d); return d; });
        manager.AddTrack(new(Enumerable.Range(0, 100).SelectMany(i => new[] { i / 100f, -i / 100f }).ToArray(), 44100, 2));
        manager.SeekToFrame(37); manager.PlayAll(); manager.PlayAll();
        Assert(manager.State == PlaybackState.Playing && devices.Count == 1, "Repeated Play restarted active audio.");
        byte[] frame = new byte[8]; devices[0].Provider!.Read(frame);
        Near(BitConverter.ToSingle(frame, 0), 0.37); Near(BitConverter.ToSingle(frame, 4), -0.37);
        // Reading the provider without advancing the fake device does not advance its output clock.
        Assert(manager.GetPlayheadFrame() == 37, "Play ignored the selected cursor.");
        devices[0].AdvanceFrames(10);
        Assert(manager.GetPlayheadFrame() == 47, "Playback position did not include the starting cursor.");
    }

    private static void PlayingSeek()
    {
        var (manager, _, devices) = PlaybackProject();
        using (manager)
        {
            manager.PlayAll(); devices[0].AdvanceFrames(1000);
            manager.SeekToFrame(22050);
            Assert(devices.Count == 2 && devices[0].Disposed && manager.State == PlaybackState.Playing,
                "Playing seek did not replace the previous audio stream.");
            Assert(manager.GetPlayheadFrame() == 22050, "Playing seek retained the old position.");
            devices[1].AdvanceFrames(4410);
            Assert(manager.GetPlayheadFrame() == 26460, "Playback did not advance from the sought position.");
        }
    }

    private static void PausedSeek()
    {
        var (manager, _, devices) = PlaybackProject();
        using (manager)
        {
            manager.PlayAll(); devices[0].AdvanceFrames(4410); manager.PauseAll();
            manager.SeekToFrame(33075); manager.UpdatePlayback();
            Assert(manager.State == PlaybackState.Paused && devices.Count == 1 && devices[0].Disposed,
                "Paused seeking unexpectedly started audio.");
            Assert(manager.GetPlayheadFrame() == 33075, "Paused cursor was lost.");
            manager.PlayAll();
            Assert(devices.Count == 2 && manager.GetPlayheadFrame() == 33075, "Paused seek resumed at the wrong place.");
        }
    }

    private static void StopTransport()
    {
        foreach (bool pauseFirst in new[] { false, true })
        {
            var (manager, _, devices) = PlaybackProject();
            using (manager)
            {
                manager.LoopPlayback = true; manager.PlayAll(); devices[0].AdvanceFrames(44100);
                if (pauseFirst) manager.PauseAll();
                manager.StopAll(); manager.UpdatePlayback(); manager.UpdatePlayback();
                Assert(manager.GetPlayheadFrame() == 0 && manager.State == PlaybackState.Stopped && devices.Count == 1 && devices[0].Disposed,
                    "Stop reset failed or playback automatically looped.");
                manager.PlayAll();
                Assert(manager.GetPlayheadFrame() == 0 && devices.Count == 2, "Play after Stop did not start at zero.");
            }
        }
    }

    private static void NaturalCompletion()
    {
        var (manager, _, devices) = PlaybackProject();
        using (manager)
        {
            manager.PlayAll(); devices[0].AdvanceFrames(44100); devices[0].Stop(); manager.UpdatePlayback();
            Assert(manager.GetPlayheadFrame() == 44100 && manager.State == PlaybackState.Stopped && devices.Count == 1,
                "Completion restarted audio or lost the end cursor.");
            manager.PlayAll();
            Assert(manager.GetPlayheadFrame() == 0 && manager.State == PlaybackState.Playing && devices.Count == 2,
                "Explicit Play at the end did not start another pass.");
        }
    }

    private static void LoopCompletion()
    {
        var (manager, _, devices) = PlaybackProject();
        using (manager)
        {
            manager.LoopPlayback = true; manager.PlayAll();
            devices[0].AdvanceFrames(44100); devices[0].Stop(); manager.UpdatePlayback();
            Assert(devices.Count == 2 && manager.State == PlaybackState.Playing && manager.GetPlayheadFrame() == 0,
                "Natural completion did not loop.");
            devices[1].AdvanceFrames(1000); devices[1].Stop(); manager.UpdatePlayback();
            Assert(devices.Count == 2 && manager.State == PlaybackState.Stopped && manager.GetPlayheadFrame() == 1000,
                "An early device stop incorrectly looped.");
            manager.SeekToFrame(0); manager.PlayAll(); devices[2].AdvanceFrames(1000); manager.PauseAll(); manager.UpdatePlayback();
            Assert(devices.Count == 3 && manager.State == PlaybackState.Paused, "Pause incorrectly looped.");
        }
    }

    private static void SeekEnd()
    {
        var (manager, _, devices) = PlaybackProject();
        using (manager)
        {
            manager.LoopPlayback = true; manager.PlayAll(); manager.SeekToFrame(manager.DurationFrames); manager.UpdatePlayback();
            Assert(manager.State == PlaybackState.Stopped && manager.GetPlayheadFrame() == 44100 && devices.Count == 1,
                "Seeking to the end automatically restarted playback.");
            manager.PlayAll(); manager.PauseAll(); manager.SeekToFrame(manager.DurationFrames); manager.UpdatePlayback();
            Assert(manager.State == PlaybackState.Paused && devices.Count == 2 && manager.GetPlayheadFrame() == 44100,
                "Paused seek to the end unexpectedly played.");
        }
    }

    private static void TransportBounds()
    {
        List<FakeOutput> devices = new();
        using TrackManager manager = new(null!, () => { FakeOutput d = new(); devices.Add(d); return d; });
        manager.LoopPlayback = true;
        manager.SeekToFrame(long.MaxValue); manager.PlayAll(); manager.PauseAll(); manager.StopAll(); manager.UpdatePlayback();
        Assert(manager.DurationFrames == 0 && manager.GetPlayheadFrame() == 0 && manager.State == PlaybackState.Stopped && devices.Count == 0,
            "Empty transport opened a device or retained a nonzero cursor.");
        manager.AddTrack(new(Constant(100)));
        manager.SeekToFrame(-1); Assert(manager.GetPlayheadFrame() == 0, "Negative seek was not clamped.");
        manager.SeekToFrame(long.MaxValue); Assert(manager.GetPlayheadFrame() == 100, "Past-end seek was not clamped.");
    }

    private static void SeekAfterEdits()
    {
        foreach (bool pause in new[] { false, true })
        {
            var (manager, source, devices) = PlaybackProject();
            using (manager)
            {
                manager.PlayAll(); manager.PauseAll();
                if (!pause) manager.StopAll();
                manager.SeekToFrame(22050); source.ChangeVolume(0.25f); manager.PlayAll();
                Assert(manager.GetPlayheadFrame() == 22050 && devices[0].Disposed, "Edited seek lost its position or reused a stale output.");
                byte[] frame = new byte[8]; devices[^1].Provider!.Read(frame);
                Near(BitConverter.ToSingle(frame, 0), 0.2); Near(BitConverter.ToSingle(frame, 4), 0.2);
            }
        }
    }

    private static void TransportHistory()
    {
        var (manager, source, devices) = PlaybackProject();
        using (manager)
        {
            manager.ClearHistory(); manager.SeekToFrame(11025);
            Assert(!manager.CanUndo, "Seeking created an audio edit history entry.");
            source.ChangeVolume(0.25f); manager.PlayAll(); manager.PauseAll(); manager.Undo(); manager.PlayAll();
            Assert(manager.GetPlayheadFrame() == 11025 && devices[0].Disposed, "Undo moved the cursor or reused stale playback.");
            byte[] frame = new byte[8]; devices[^1].Provider!.Read(frame); Near(BitConverter.ToSingle(frame, 0), 0.8);
            manager.PauseAll(); manager.Redo(); manager.PlayAll();
            devices[^1].Provider!.Read(frame); Near(BitConverter.ToSingle(frame, 0), 0.2);
        }
    }

    private static void TransportDisposal()
    {
        var (manager, _, devices) = PlaybackProject();
        manager.PlayAll(); manager.SeekToFrame(100); manager.PauseAll(); manager.PlayAll(); manager.Dispose();
        Assert(devices.Count == 2 && devices.All(d => d.Disposed), "A transport output device was leaked.");
    }

    private static void TransportFailure()
    {
        int attempts = 0;
        using TrackManager manager = new(null!, () => { attempts++; throw new InvalidOperationException("Expected fake device failure"); });
        manager.AddTrack(new(Constant(100))); manager.LoopPlayback = true; manager.SeekToFrame(37);
        try { manager.PlayAll(); throw new Exception("Expected initialization failure"); }
        catch (InvalidOperationException e) when (e.Message == "Expected fake device failure") { }
        manager.UpdatePlayback();
        Assert(manager.State == PlaybackState.Stopped && manager.GetPlayheadFrame() == 37 && attempts == 1,
            "Failed playback looped or lost the selected cursor.");
    }

    private static void OffsetTransport()
    {
        var (manager, source, devices) = PlaybackProject();
        using (manager)
        {
            source.MoveToFrame(44100);
            Assert(manager.DurationFrames == 88200, "Duration omitted the track's timeline offset.");
            manager.SeekToFrame(22050); manager.PlayAll();
            byte[] frame = new byte[8]; devices[^1].Provider!.Read(frame); Near(BitConverter.ToSingle(frame, 0), 0);
            manager.SeekToFrame(44100); devices[^1].Provider!.Read(frame); Near(BitConverter.ToSingle(frame, 0), 0.8);
            Assert(manager.GetPlayheadFrame() == 44100, "Offset seek was interpreted as source-local time.");
        }
    }

    private static void EmptyDuringPlayback()
    {
        var (manager, source, devices) = PlaybackProject();
        using (manager)
        {
            manager.PlayAll(); devices[0].AdvanceFrames(1000); manager.LoopPlayback = true;
            manager.RemTrack(source); manager.UpdatePlayback();
            Assert(manager.State == PlaybackState.Stopped && manager.GetPlayheadFrame() == 0 && devices[0].Disposed,
                "Removing all audio left playback active.");
        }
    }

    private static void TransportUserInterface()
    {
        using FrmMain form = new(); // Never displayed; no real audio device is used.
        TrackManager original = Field<TrackManager>(form, "trackManager");
        List<FakeOutput> devices = new();
        TrackManager manager = new(null!, () => { FakeOutput d = new(); devices.Add(d); return d; });
        form.GetType().GetField("trackManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, manager);
        Button play = Field<Button>(form, "btnPlay"), stop = Field<Button>(form, "btnStop");
        TrackBar slider = Field<TrackBar>(form, "trkPlaybackPosition");
        Label time = Field<Label>(form, "lblPlaybackTime");
        try
        {
            ClickHandler(form, "tmrUpdatePlaybackState_Tick");
            Assert(!play.Enabled && !stop.Enabled && !slider.Enabled && time.Text == "00:00.000 / 00:00.000", "Empty UI state is incorrect.");
            manager.AddTrack(new(Constant(44100)));
            form.GetType().GetMethod("RefreshTrackPanels", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            slider.Value = 2500; ClickHandler(form, "trkPlaybackPosition_Scroll");
            Assert(manager.GetPlayheadFrame() == 11025 && time.Text == "00:00.250 / 00:01.000" && devices.Count == 0,
                "Position slider did not select the shared cursor.");
            ClickHandler(form, "btnPlay_Click");
            Assert(play.AccessibleName == "Pause" && manager.GetPlayheadFrame() == 11025, "Play UI ignored selected position.");
            devices[0].AdvanceFrames(4410); ClickHandler(form, "tmrUpdatePlaybackState_Tick");
            Assert(slider.Value == 3500 && time.Text == "00:00.350 / 00:01.000", "Playback clock and slider disagree.");
            ClickHandler(form, "btnPlay_Click"); Assert(manager.State == PlaybackState.Paused && play.AccessibleName == "Play", "Pause UI did not update.");
            slider.Value = 5000; ClickHandler(form, "trkPlaybackPosition_Scroll");
            Assert(manager.State == PlaybackState.Paused && devices.Count == 1, "Paused slider unexpectedly played.");
            ClickHandler(form, "btnPlay_Click"); Assert(manager.GetPlayheadFrame() == 22050, "Resume UI lost the sought position.");
            manager.LoopPlayback = true; ClickHandler(form, "btnStop_Click"); ClickHandler(form, "tmrUpdatePlaybackState_Tick");
            Assert(slider.Value == 0 && !stop.Enabled && time.Text == "00:00.000 / 00:01.000" && manager.State == PlaybackState.Stopped,
                "Stop UI failed to reset or unexpectedly looped.");
            foreach (Size size in new[] { new Size(650, 500), new Size(1280, 720), new Size(1920, 1080) })
            {
                form.Size = size;
                form.GetType().GetMethod("UpdateSizes", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
                Panel tracks = Field<Panel>(form, "panTracks");
                Assert(stop.Left > play.Right && slider.Left > stop.Right && slider.Right <= form.ClientSize.Width &&
                    tracks.Bottom < play.Top && slider.Bottom <= play.Bottom, "Transport overlaps the waveform or leaves the window.");
            }
            manager.StopAll(); manager.SeekToFrame(44100); manager.PlayAll();
            devices[^1].AdvanceFrames(44100); devices[^1].Stop(); ClickHandler(form, "tmrUpdatePlaybackState_Tick");
            Assert(play.AccessibleName == "Pause" && manager.GetPlayheadFrame() == 0, "Loop UI did not restart from zero.");
        }
        finally { manager.Dispose(); original.Dispose(); }
    }

    private static void TimestampFormatting()
    {
        MethodInfo format = typeof(FrmMain).GetMethod("FormatTimestamp", BindingFlags.Static | BindingFlags.NonPublic)!;
        string At(long frame) => (string)format.Invoke(null, new object[] { frame })!;
        Assert(At(0) == "00:00.000" && At(11025) == "00:00.250" && At(44) == "00:00.000" && At(45) == "00:00.001",
            "Subsecond formatting is incorrect.");
        Assert(At(3600L * 44100 + 11025) == "1:00:00.250" && At(25L * 3600 * 44100) == "25:00:00.000",
            "Hour formatting wrapped or lost precision.");
    }
}
