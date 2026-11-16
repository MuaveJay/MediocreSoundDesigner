using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MediocreSoundDesigner;

public enum SampleRegion
{
    BEGINNING_ONLY,
    END_ONLY,
    BEGINNING_AND_END,
    ANYWHERE,
}

public partial class Track : IDisposable
{
    private float[] samples;
    private readonly int sampleRate = AudioData.SampleRate;
    private readonly int channels = AudioData.Channels;
    private readonly Func<IWavePlayer> playbackFactory;
    private IWavePlayer outputDevice;
    private long playbackStartFrame;
    private long playbackLengthFrames;
    private float[] playbackAudio;
    private long playbackAudioRevision = -1;
    private long startFrame;

    private readonly Stack<(float[] Samples, float Volume, long StartFrame, string Name, Color TrackColor)> undoHistory = new();
    private readonly Stack<(float[] Samples, float Volume, long StartFrame, string Name, Color TrackColor)> redoHistory = new();

    public bool IsDisabled { get; private set; }
    public Panel PanDrawSamples { get; private set; }
    public Control TrackHeader { get; internal set; }
    public float Volume { get; private set; } = 1.0f;
    public int SampleRate => sampleRate;
    public int Channels => channels;
    public long Revision { get; private set; }
    public long MixRevision { get; private set; }
    public long StartFrame => startFrame;
    public double StartSeconds => startFrame / (double)AudioData.SampleRate;
    public double DurationSeconds => samples.Length / (double)(sampleRate * channels);
    public long TimelineFrameCount => samples.Length == 0 ? 0 : Math.Max(1, (long)Math.Round(
        DurationSeconds * AudioData.SampleRate, MidpointRounding.AwayFromZero));
    public long EndFrame => checked(startFrame + TimelineFrameCount);
    public PlaybackState State => outputDevice?.PlaybackState ?? PlaybackState.Stopped;

    public Track(string file)
    {
        SetInitialName(Path.GetFileNameWithoutExtension(file));
        using AudioFileReader reader = new(file);
        AudioData.Validate(Array.Empty<float>(), reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
        samples = AudioData.ToMixFormat(AudioData.ReadAll(reader),
            reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
        playbackFactory = () => new WaveOut();
    }

    public Track(float[] samples, int sampleRate = AudioData.SampleRate, int channels = 1,
                 Func<IWavePlayer> playbackFactory = null)
    {
        // Convert once so every edit uses the same frame grid as the output mixer.
        this.samples = AudioData.ToMixFormat(samples, sampleRate, channels);
        this.playbackFactory = playbackFactory ?? (() => new WaveOut());
    }

    public Track Duplicate(bool preserveIdentity = false)
    {
        return new Track(samples, sampleRate, channels, playbackFactory)
        {
            Volume = this.Volume,
            startFrame = this.startFrame,
            IsDisabled = this.IsDisabled,
            Name = preserveIdentity ? Name : NameWithSuffix(Name, " Copy"),
            TrackColor = TrackColor,
            needsDefaultName = false
        };
    }

    public void LinkPanels(Panel panDrawSamples, Panel panContainer)
    {
        PanDrawSamples = panDrawSamples;
    }

    private void Changed(bool audioChanged = true, bool mixChanged = true)
    {
        Revision++;
        if (mixChanged) MixRevision++;
        if (audioChanged) waveformRevision++;
        PanDrawSamples?.Invalidate();
    }

    private void SaveState()
    {
        if (HistoryOwner is not null)
            return;
        undoHistory.Push((CloneSamples(), Volume, startFrame, Name, TrackColor));
        redoHistory.Clear();
    }

    public bool Undo()
    {
        if (HistoryOwner is not null)
            return HistoryOwner.Undo();
        if (undoHistory.Count == 0)
            return false;
        redoHistory.Push((CloneSamples(), Volume, startFrame, Name, TrackColor));
        var previous = undoHistory.Pop();
        (samples, Volume, startFrame, Name, TrackColor) = previous;
        Changed();
        return true;
    }

    public bool Redo()
    {
        if (HistoryOwner is not null)
            return HistoryOwner.Redo();
        if (redoHistory.Count == 0)
            return false;
        undoHistory.Push((CloneSamples(), Volume, startFrame, Name, TrackColor));
        var next = redoHistory.Pop();
        (samples, Volume, startFrame, Name, TrackColor) = next;
        Changed();
        return true;
    }

    public float[] CloneSamples() => (float[])samples.Clone();

    public void ReplaceSamples(float[] replacement)
    {
        if (NeedsProjectEdit)
        {
            HistoryOwner.ExecuteEdit(() => ReplaceSamples(replacement));
            return;
        }
        AudioData.Validate(replacement, sampleRate, channels);
        samples = (float[])replacement.Clone();
        Changed();
    }

    public void MoveToFrame(long frame)
    {
        if (NeedsProjectEdit)
        {
            HistoryOwner.ExecuteEdit(() => MoveToFrame(frame));
            return;
        }
        if (frame < 0)
            throw new ArgumentOutOfRangeException(nameof(frame));
        if (startFrame == frame)
            return;
        startFrame = frame;
        Changed(false);
    }

    public void ShiftSampleOver(int pixels, double framesPerPixel)
    {
        long delta = (long)Math.Round(pixels * ZoomScale.Sanitize(framesPerPixel), MidpointRounding.AwayFromZero);
        if (pixels != 0 && delta == 0) delta = Math.Sign(pixels);
        MoveToFrame(Math.Max(0, checked(startFrame + delta)));
    }

    public void Normalize()
    {
        if (NeedsProjectEdit)
        {
            HistoryOwner.ExecuteEdit(() => Normalize());
            return;
        }
        if (samples.Length == 0 || Volume == 0)
            return;
        float peak = 0;
        foreach (float sample in samples)
            peak = Math.Max(peak, Math.Abs(sample * Volume));
        if (peak == 0)
            return;
        SaveState();
        float gain = 0.95f / peak;
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= gain;
        Changed();
    }

    public void Reverse()
    {
        if (NeedsProjectEdit)
        {
            HistoryOwner.ExecuteEdit(() => Reverse());
            return;
        }
        SaveState();
        int frames = samples.Length / channels;
        for (int frame = 0; frame < frames / 2; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                int left = frame * channels + channel;
                int right = (frames - 1 - frame) * channels + channel;
                (samples[left], samples[right]) = (samples[right], samples[left]);
            }
        }
        Changed();
    }

    public void Flip()
    {
        if (NeedsProjectEdit)
        {
            HistoryOwner.ExecuteEdit(() => Flip());
            return;
        }
        SaveState();
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= -1;
        Changed();
    }

    public void FadeIn() => ApplyFade(true);
    public void FadeOut() => ApplyFade(false);

    private void ApplyFade(bool fadeIn)
    {
        if (NeedsProjectEdit)
        {
            HistoryOwner.ExecuteEdit(() => ApplyFade(fadeIn));
            return;
        }
        int frameCount = samples.Length / channels;
        if (frameCount < 2)
            return;
        SaveState();
        for (int frame = 0; frame < frameCount; frame++)
        {
            float progress = (float)frame / (frameCount - 1);
            float gain = fadeIn ? progress : 1.0f - progress;
            for (int channel = 0; channel < channels; channel++)
                samples[frame * channels + channel] *= gain;
        }
        Changed();
    }

    public bool Trim(double startTime, double endTime)
    {
        if (NeedsProjectEdit)
            return HistoryOwner.ExecuteEdit(() => Trim(startTime, endTime));
        if (!double.IsFinite(startTime) || !double.IsFinite(endTime) ||
            startTime < 0 || startTime >= endTime || endTime > DurationSeconds)
            return false;
        return TrimFrames((long)Math.Round(startTime * sampleRate, MidpointRounding.AwayFromZero),
            (long)Math.Round(endTime * sampleRate, MidpointRounding.AwayFromZero));
    }

    internal bool TrimFrames(long firstFrame, long endFrame)
    {
        if (NeedsProjectEdit)
            return HistoryOwner.ExecuteEdit(() => TrimFrames(firstFrame, endFrame));
        if (firstFrame < 0 || endFrame > TimelineFrameCount || firstFrame >= endFrame ||
            (firstFrame == 0 && endFrame == TimelineFrameCount))
            return false;
        int start = checked((int)(firstFrame * channels));
        int end = checked((int)(endFrame * channels));
        SaveState();
        startFrame = checked(startFrame + firstFrame);
        samples = samples[start..end];
        Changed();
        return true;
    }

    public Track Split(double splitTime)
    {
        if (NeedsProjectEdit)
            return HistoryOwner.SplitTrack(this, splitTime);
        if (!double.IsFinite(splitTime) || splitTime <= 0 || splitTime >= DurationSeconds)
            return null;
        return SplitAtFrame((long)Math.Round(splitTime * sampleRate, MidpointRounding.AwayFromZero));
    }

    internal Track SplitAtFrame(long localFrame)
    {
        if (localFrame <= 0 || localFrame >= TimelineFrameCount)
            return null;
        if (NeedsProjectEdit)
            return HistoryOwner.SplitTrackAtFrame(this, checked(startFrame + localFrame));
        int splitIndex = checked((int)(localFrame * channels));
        Track second = Duplicate(preserveIdentity: true);
        second.Name = NameWithSuffix(Name, " Split");
        second.ReplaceSamples(samples[splitIndex..]);
        second.MoveToFrame(checked(startFrame + localFrame));
        SaveState();
        samples = samples[..splitIndex];
        Changed();
        return second;
    }

    public void AlterTimeScale(float scale) { }

    public void SetVolume(float newVolume)
    {
        if (NeedsProjectEdit)
        {
            HistoryOwner.ExecuteEdit(() => SetVolume(newVolume));
            return;
        }
        if (Volume == newVolume)
            return;
        Volume = newVolume;
        Changed(false);
    }

    public void ChangeVolume(float newVolume)
    {
        if (NeedsProjectEdit)
        {
            HistoryOwner.ExecuteEdit(() => ChangeVolume(newVolume));
            return;
        }
        if (Volume == newVolume)
            return;
        SaveState();
        SetVolume(newVolume);
    }

    public void EliminateSilence(SampleRegion region, float minDuration, float threshold)
    {
        if (NeedsProjectEdit)
        {
            HistoryOwner.ExecuteEdit(() => EliminateSilence(region, minDuration, threshold));
            return;
        }
        if (!float.IsFinite(minDuration) || !float.IsFinite(threshold) || minDuration < 0 || threshold < 0)
            return;
        int frames = samples.Length / channels;
        long minimumFrames = Math.Max(1, (long)Math.Ceiling((double)minDuration * sampleRate));
        bool Silent(int frame)
        {
            for (int channel = 0; channel < channels; channel++)
                if (Math.Abs(samples[frame * channels + channel]) > threshold)
                    return false;
            return true;
        }

        float[] result;
        if (region == SampleRegion.ANYWHERE)
        {
            List<float> retained = new();
            int frame = 0;
            while (frame < frames)
            {
                int start = frame;
                if (Silent(frame))
                {
                    while (frame < frames && Silent(frame))
                        frame++;
                    if (frame - start >= minimumFrames)
                        continue;
                }
                else
                    frame++;
                retained.AddRange(samples.AsSpan(start * channels, (frame - start) * channels).ToArray());
            }
            result = retained.ToArray();
        }
        else
        {
            int start = 0;
            int end = frames;
            if (region is SampleRegion.BEGINNING_ONLY or SampleRegion.BEGINNING_AND_END)
            {
                int silent = 0;
                while (silent < end && Silent(silent)) silent++;
                if (silent >= minimumFrames) start = silent;
            }
            if (region is SampleRegion.END_ONLY or SampleRegion.BEGINNING_AND_END)
            {
                int silent = 0;
                while (end - silent > start && Silent(end - silent - 1)) silent++;
                if (silent >= minimumFrames) end -= silent;
            }
            result = samples[(start * channels)..(end * channels)];
        }
        if (result.Length == samples.Length)
            return;
        SaveState();
        samples = result;
        Changed();
    }

    internal float[] GetMixSamples() => CloneSamples();

    public float[] GetAdjustedSamples()
    {
        float[] normalized = GetMixSamples();
        int offset = checked((int)(startFrame * AudioData.Channels));
        float[] result = new float[checked(offset + normalized.Length)];
        for (int i = 0; i < normalized.Length; i++)
            result[offset + i] = normalized[i] * Volume;
        return result;
    }

    public void Play()
    {
        if (State == PlaybackState.Paused)
        {
            outputDevice.Play();
            return;
        }
        PlayFromFrame(0);
    }

    internal void PlayFromFrame(long frame)
    {
        outputDevice?.Dispose();
        outputDevice = null;
        // Scrubbing opens a new stream at another offset in the same immutable
        // snapshot, without copying the entire mix for every mouse movement.
        if (playbackAudio is null || playbackAudioRevision != MixRevision)
        {
            playbackAudio = GetAdjustedSamples();
            playbackAudioRevision = MixRevision;
        }
        float[] audio = playbackAudio;
        playbackLengthFrames = audio.Length / AudioData.Channels;
        playbackStartFrame = Math.Clamp(frame, 0, playbackLengthFrames);
        if (playbackStartFrame == playbackLengthFrames)
            return;
        var provider = new AudioArraySampleProvider(audio, AudioData.SampleRate, AudioData.Channels,
            checked((int)(playbackStartFrame * AudioData.Channels)));
        outputDevice = playbackFactory();
        try
        {
            outputDevice.Init(new SampleToWaveProvider(provider));
            outputDevice.Play();
        }
        catch
        {
            outputDevice.Dispose();
            outputDevice = null;
            throw;
        }
    }

    public void Pause() => outputDevice?.Pause();
    public void Reset()
    {
        outputDevice?.Dispose();
        outputDevice = null;
        playbackStartFrame = 0;
    }

    public long GetPlayheadFrame()
    {
        if (outputDevice is not IWavePosition position)
            return playbackStartFrame;
        WaveFormat format = position.OutputWaveFormat;
        long outputFrames = position.GetPosition() / format.BlockAlign;
        long timelineFrames = (long)(outputFrames * (double)AudioData.SampleRate / format.SampleRate);
        return Math.Min(playbackLengthFrames, playbackStartFrame + timelineFrames);
    }

    public int GetPlayheadPos() => (int)Math.Min(int.MaxValue, GetPlayheadFrame());

    public void ToggleDisabled()
    {
        if (NeedsProjectEdit)
        {
            HistoryOwner.ExecuteEdit(() => ToggleDisabled());
            return;
        }
        IsDisabled = !IsDisabled;
        Changed(false);
    }

    public void SaveToFile(string path)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path));
        Directory.CreateDirectory(directory);
        float[] audio = GetAdjustedSamples();
        using WaveFileWriter writer = new(path, AudioData.MixFormat);
        writer.WriteSamples(audio, 0, audio.Length);
    }

    public void Dispose()
    {
        outputDevice?.Dispose();
        outputDevice = null;
        playbackAudio = null;
        waveformPeaks = null;
        samples = Array.Empty<float>();
        ClearLocalHistory();
    }
}
