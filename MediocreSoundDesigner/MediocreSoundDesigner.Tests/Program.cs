using MediocreSoundDesigner;
using NAudio.Wave;

internal static partial class Program
{
    private static int failures;
    private static int checks;

    [STAThread]
    private static int Main()
    {
        Run("Zoom never changes the mixed audio or track position", ZoomDoesNotMoveAudio);
        Run("Mono generation has the correct stereo output duration", MonoDuration);
        Run("Different source rates and stereo channels mix at the right time", MixedFormats);
        Run("Resampling retains tone frequency and stereo separation", ResampledTones);
        Run("Imported mono and stereo files retain correct duration", FileImport);
        Run("Split uses a timeline offset instead of silence padding", SplitPlacement);
        Run("Repeated splits retain their absolute positions", RepeatedSplit);
        Run("Resampled audio splits without duration drift or changed samples", ResampledSplit);
        Run("Trim and Undo/Redo restore audio and timeline position", TrimHistory);
        Run("Duplicate samples and settings are independent", DuplicateIndependence);
        Run("Stereo fades and reverse keep channel frames aligned", StereoEdits);
        Run("Silence elimination uses complete stereo frames", StereoSilence);
        Run("All silence-removal modes preserve frame alignment", SilenceModes);
        Run("Normal pause/resume keeps the same player and position", UnchangedResume);
        Run("Paused edits rebuild the mix and retain playback position", EditedResume);
        Run("Changing the output format still gives the correct playhead", OutputPosition);
        Run("Export preserves offsets, duration, stereo, and playback", Export);
        Run("Empty and disabled tracks do not create invalid playback", EmptyTracks);
        Run("Invalid ranges and malformed sample buffers are rejected", InvalidInputs);
        RunHistoryChecks();
        RunTransportChecks();
        RunTimelineChecks();
        RunPlayheadSplitChecks();
        RunVisualTrimChecks();
        RunTrackSelectionChecks();
        RunSelectedVolumeChecks();
        RunWaveformViewChecks();
        RunTrackIdentityChecks();
        RunTrackReorderChecks();
        RunWindowModeChecks();
        RunEditorLayoutChecks();
        RunModifierSelectionChecks();
        RunShortcutHelpChecks();
        RunReportedRenderingChecks();
        RunPlayheadFollowChecks();
        RunProjectFileChecks();
        Console.WriteLine(failures == 0 ? $"All {checks} regression checks passed." : $"{failures} of {checks} regression checks failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action check)
    {
        checks++;
        try { check(); Console.WriteLine("PASS: " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL: " + name + " -- " + error.Message); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Near(double actual, double expected, double tolerance = 1e-5)
        => Assert(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}.");

    private static float[] Constant(int frames, float value = 0.25f, int channels = 1)
        => Enumerable.Repeat(value, frames * channels).ToArray();

    private static void Equal(float[] actual, float[] expected, double tolerance = 1e-5)
    {
        Assert(actual.Length == expected.Length, $"Lengths differ: {actual.Length} vs {expected.Length}.");
        for (int i = 0; i < actual.Length; i++)
            if (Math.Abs(actual[i] - expected[i]) > tolerance)
                throw new InvalidOperationException($"Sample {i}: {actual[i]} vs {expected[i]}.");
    }

    private static void ZoomDoesNotMoveAudio()
    {
        using Track track = new(Constant(100));
        using TrackManager manager = new(null!);
        manager.AddTrack(track);
        track.ShiftSampleOver(5, 500);
        Assert(track.StartFrame == 2500, "Keyboard movement did not convert pixels to timeline frames.");
        float[] baseline = manager.GetMixedSamples();
        long revision = track.Revision;
        using Panel panel = new() { Width = 120, Height = 80 };
        track.LinkPanels(panel, panel);
        using Bitmap bitmap = new(120, 80);
        using Graphics graphics = Graphics.FromImage(bitmap);
        foreach (int zoom in new[] { 1, 10, 500, 999, 0 })
        {
            manager.DrawTimescale = zoom;
            track.DrawSamples(graphics, manager.DrawTimescale);
            Equal(manager.GetMixedSamples(), baseline);
            Assert(track.StartFrame == 2500 && track.Revision == revision, "Drawing changed the audio model.");
        }
        track.ShiftSampleOver(-10000, 500);
        Assert(track.StartFrame == 0, "Movement went before project zero.");
    }

    private static void MonoDuration()
    {
        using Track track = new(Constant(44100));
        Near(track.DurationSeconds, 1);
        float[] mixed = track.GetAdjustedSamples();
        Assert(mixed.Length == 88200, "Mono audio was not expanded into stereo frames.");
        Near(mixed[0], 0.25); Near(mixed[1], 0.25);
    }

    private static void MixedFormats()
    {
        using Track mono = new(Constant(48000, 0.25f), 48000);
        using Track stereo = new(Enumerable.Range(0, 22050).SelectMany(_ => new[] { 0.1f, -0.2f }).ToArray(), 22050, 2);
        stereo.MoveToFrame(22050);
        using TrackManager manager = new(null!);
        manager.AddTrack(mono); manager.AddTrack(stereo);
        float[] mixed = manager.GetMixedSamples();
        Assert(mixed.Length == 132300, "Mixed duration must be 1.5 seconds of stereo frames.");
        Near(mixed[10000], 0.25, 0.002);
        Near(mixed[60000], 0.35, 0.002); Near(mixed[60001], 0.05, 0.002);
        Near(mixed[110000], 0.1, 0.002); Near(mixed[110001], -0.2, 0.002);
    }

    private static void FileImport()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        try
        {
            foreach (var format in new[] { (Rate: 48000, Channels: 1), (Rate: 22050, Channels: 2) })
            {
                using (WaveFileWriter writer = new(path, WaveFormat.CreateIeeeFloatWaveFormat(format.Rate, format.Channels)))
                {
                    float[] audio = Constant(format.Rate, 0.2f, format.Channels);
                    writer.WriteSamples(audio, 0, audio.Length);
                }
                using Track imported = new(path);
                Near(imported.DurationSeconds, 1);
                Assert(imported.GetAdjustedSamples().Length == 88200, "Imported audio duration changed during conversion.");
                // The importer must release its file handle after reading.
                using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
        }
        finally { File.Delete(path); }
    }

    private static void ResampledTones()
    {
        float[] source = Enumerable.Range(0, 48000).SelectMany(frame => new[] {
            (float)(0.5 * Math.Sin(2 * Math.PI * 440 * frame / 48000)),
            (float)(0.5 * Math.Sin(2 * Math.PI * 880 * frame / 48000)) }).ToArray();
        using Track track = new(source, 48000, 2);
        float[] audio = track.CloneSamples();
        Assert(audio.Length == 88200 && track.SampleRate == 44100 && track.Channels == 2,
               "Import did not establish the common editing format.");
        for (int channel = 0; channel < 2; channel++)
        {
            int crossings = 0;
            for (int frame = 101; frame < 44000; frame++)
                if (audio[(frame - 1) * 2 + channel] <= 0 && audio[frame * 2 + channel] > 0)
                    crossings++;
            double frequency = crossings / ((44000 - 101) / 44100.0);
            Near(frequency, channel == 0 ? 440 : 880, 2);
        }
    }

    private static void SplitPlacement()
    {
        float[] original = Enumerable.Range(0, 44100).SelectMany(i => new[] { i / 44100f, -i / 44100f }).ToArray();
        using Track track = new(original, 44100, 2);
        track.MoveToFrame(4410);
        float[] before = track.GetAdjustedSamples();
        using Track second = track.Split(0.25);
        Assert(second is not null && second.StartFrame == 15435, "Second section is not at the split timestamp.");
        Near(track.DurationSeconds, 0.25); Near(second!.DurationSeconds, 0.75);
        Near(second.CloneSamples()[0], 0.25);
        using TrackManager manager = new(null!);
        manager.AddTrack(track); manager.AddTrack(second);
        Equal(manager.GetMixedSamples(), before);
    }

    private static void RepeatedSplit()
    {
        using Track first = new(Constant(44100));
        using Track second = first.Split(0.25);
        using Track third = second.Split(0.25);
        Assert(first.StartFrame == 0 && second.StartFrame == 11025 && third.StartFrame == 22050,
               "Repeated split positions drifted.");
        using TrackManager manager = new(null!);
        manager.AddTrack(first); manager.AddTrack(second); manager.AddTrack(third);
        Equal(manager.GetMixedSamples(), Constant(44100, 0.25f, 2));
    }

    private static void TrimHistory()
    {
        using Track track = new(Constant(48000), 48000);
        track.MoveToFrame(88200);
        Assert(track.Trim(0.25, 0.75), "Trim failed.");
        Near(track.DurationSeconds, 0.5); Near(track.StartSeconds, 2.25);
        Assert(track.Undo(), "Trim undo failed.");
        Near(track.DurationSeconds, 1); Near(track.StartSeconds, 2);
        Assert(track.Redo(), "Trim redo failed.");
        Near(track.DurationSeconds, 0.5); Near(track.StartSeconds, 2.25);
    }

    private static void ResampledSplit()
    {
        using Track first = new(Enumerable.Range(0, 48001).Select(i => (float)Math.Sin(i * 0.1)).ToArray(), 48000);
        float[] baseline = first.GetAdjustedSamples();
        using Track second = first.Split(0.12345);
        using Track third = second.Split(0.23456);
        using TrackManager manager = new(null!);
        manager.AddTrack(first); manager.AddTrack(second); manager.AddTrack(third);
        Equal(manager.GetMixedSamples(), baseline, 0);
    }

    private static void DuplicateIndependence()
    {
        using Track source = new(Constant(40, 0.4f, 2), 48000, 2);
        source.MoveToFrame(2000); source.ChangeVolume(0.5f);
        using Track copy = source.Duplicate();
        Assert(copy.SampleRate == 44100 && copy.Channels == 2 && copy.StartFrame == 2000, "Duplicate metadata was lost.");
        Near(copy.Volume, 0.5);
        float[] original = source.CloneSamples();
        copy.FadeOut(); Equal(source.CloneSamples(), original);
        Assert(copy.Undo(), "Duplicate fade undo failed.");
        Equal(copy.CloneSamples(), source.CloneSamples());
    }

    private static void StereoEdits()
    {
        using Track track = new(new[] { 0.1f, -0.1f, 0.2f, -0.2f, 0.3f, -0.3f }, 44100, 2);
        track.Reverse();
        Equal(track.CloneSamples(), new[] { 0.3f, -0.3f, 0.2f, -0.2f, 0.1f, -0.1f });
        track.Undo(); track.FadeIn();
        Equal(track.CloneSamples(), new[] { 0f, 0f, 0.1f, -0.1f, 0.3f, -0.3f });
        track.Undo(); track.FadeOut();
        Equal(track.CloneSamples(), new[] { 0.1f, -0.1f, 0.1f, -0.1f, 0f, 0f });
    }

    private static void StereoSilence()
    {
        using Track track = new(new[] { 0f, 0f, 0f, 0f, 0f, 0.5f, 0.25f, -0.25f, 0f, 0f }, 44100, 2);
        track.EliminateSilence(SampleRegion.BEGINNING_AND_END, 1f / 44100, 0.01f);
        Equal(track.CloneSamples(), new[] { 0f, 0.5f, 0.25f, -0.25f });
        Assert(track.Undo(), "Silence removal undo failed.");
        Assert(track.CloneSamples().Length == 10, "Stereo frames were not restored.");
    }

    private static void SilenceModes()
    {
        foreach (SampleRegion mode in Enum.GetValues<SampleRegion>())
        {
            using Track track = new(new[] { 0f, 0f, 1f, -1f, 0f, 0f, 0.5f, -0.5f, 0f, 0f }, 44100, 2);
            track.EliminateSilence(mode, 0, 0.01f);
            int expectedFrames = mode == SampleRegion.ANYWHERE ? 2 : mode == SampleRegion.BEGINNING_AND_END ? 3 : 4;
            Assert(track.CloneSamples().Length == expectedFrames * 2, "Incorrect retained frame count for " + mode);
        }
    }

    private static (TrackManager Manager, Track Source, List<FakeOutput> Devices) PlaybackProject()
    {
        List<FakeOutput> devices = new();
        TrackManager manager = new(null!, () => { var device = new FakeOutput(); devices.Add(device); return device; });
        Track source = new(Constant(44100, 0.8f));
        manager.AddTrack(source);
        return (manager, source, devices);
    }

    private static void UnchangedResume()
    {
        var (manager, _, devices) = PlaybackProject();
        using (manager)
        {
            manager.PlayAll(); devices[0].AdvanceFrames(4410); manager.PauseAll();
            manager.DrawTimescale = 1; manager.PlayAll();
            Assert(devices.Count == 1 && !devices[0].Disposed, "Zoom rebuilt unchanged paused audio.");
            Assert(manager.GetPlayheadFrame() == 4410 && manager.State == PlaybackState.Playing, "Resume lost its position.");
        }
    }

    private static void EditedResume()
    {
        var (manager, source, devices) = PlaybackProject();
        using (manager)
        {
            manager.PlayAll(); devices[0].AdvanceFrames(4410); manager.PauseAll();
            source.ChangeVolume(0.25f); manager.PlayAll();
            Assert(devices.Count == 2 && devices[0].Disposed, "Changed audio reused the stale player.");
            Assert(manager.GetPlayheadFrame() == 4410, "Rebuilt playback restarted at zero.");
            byte[] frame = new byte[8]; devices[1].Provider!.Read(frame);
            Near(BitConverter.ToSingle(frame, 0), 0.2); Near(BitConverter.ToSingle(frame, 4), 0.2);
        }
    }

    private static void OutputPosition()
    {
        var (manager, _, devices) = PlaybackProject();
        using (manager)
        {
            manager.PlayAll();
            devices[0].ReportedFormat = new WaveFormat(48000, 16, 2);
            devices[0].ReportedBytes = 4800 * 4;
            Assert(manager.GetPlayheadFrame() == 4410, "Output bytes were treated as source samples.");
        }
    }

    private static void Export()
    {
        var (manager, source, devices) = PlaybackProject();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        try
        {
            using (manager)
            {
                source.MoveToFrame(4410); manager.PlayAll(); devices[0].AdvanceFrames(1000);
                manager.SaveToFile(path);
                Assert(!devices[0].Disposed && manager.State == PlaybackState.Playing, "Saving interrupted playback.");
                using AudioFileReader reader = new(path);
                Near(reader.TotalTime.TotalSeconds, 1.1, 1e-4);
                Assert(reader.WaveFormat.Channels == 2 && reader.WaveFormat.SampleRate == 44100, "Incorrect exported format.");
                using Track imported = new(path);
                Equal(imported.CloneSamples(), manager.GetMixedSamples());
            }
        }
        finally { File.Delete(path); }
    }

    private static void EmptyTracks()
    {
        var (manager, source, devices) = PlaybackProject();
        using (manager)
        {
            source.ToggleDisabled(); manager.PlayAll();
            Assert(devices.Count == 0 && manager.State == PlaybackState.Stopped, "Empty mix tried to open an audio device.");
            using Track empty = new(Array.Empty<float>()); empty.MoveToFrame(44100); manager.AddTrack(empty);
            Assert(manager.GetMixedSamples().Length == 0, "Empty moved track added a silent project tail.");
            manager.RemTrack(source); manager.RemTrack(empty);
            Assert(manager.SelectedTrack is null, "Removed track left a stale selection.");
        }
    }

    private static void InvalidInputs()
    {
        using Track track = new(Constant(10));
        Assert(track.Split(double.NaN) is null && track.Split(double.PositiveInfinity) is null, "Invalid split accepted.");
        Assert(track.Split(0) is null && track.Split(track.DurationSeconds) is null, "Empty split section accepted.");
        Assert(!track.Trim(double.NaN, 1) && !track.Trim(1, 0), "Invalid trim accepted.");
        try { using Track malformed = new(new[] { 1f }, 44100, 2); throw new Exception("Malformed stereo accepted."); }
        catch (ArgumentException) { }
        try { using Track surround = new(new float[6], 44100, 6); throw new Exception("Unsupported surround accepted."); }
        catch (NotSupportedException) { }
    }
}

internal sealed class FakeOutput : IWavePlayer, IWavePosition
{
    public IWaveProvider? Provider { get; private set; }
    public bool Disposed { get; private set; }
    public PlaybackState PlaybackState { get; private set; }
    public float Volume { get; set; } = 1;
    public WaveFormat? ReportedFormat { get; set; }
    public long ReportedBytes { get; set; }
    public WaveFormat OutputWaveFormat => ReportedFormat ?? Provider?.WaveFormat ?? AudioData.MixFormat;
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;
    public void Init(IWaveProvider waveProvider) => Provider = waveProvider;
    public void Play() => PlaybackState = PlaybackState.Playing;
    public void Pause() => PlaybackState = PlaybackState.Paused;
    public void Stop() { PlaybackState = PlaybackState.Stopped; PlaybackStopped?.Invoke(this, new StoppedEventArgs(null)); }
    public long GetPosition() => ReportedBytes;
    public void Dispose() { Disposed = true; PlaybackState = PlaybackState.Stopped; }
    public void AdvanceFrames(int frames)
    {
        byte[] buffer = new byte[frames * OutputWaveFormat.BlockAlign];
        ReportedBytes += Provider!.Read(buffer);
    }
}
