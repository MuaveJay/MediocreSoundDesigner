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

    private readonly Stack<(float[] Samples, float Volume, long StartFrame)> undoHistory = new();
    private readonly Stack<(float[] Samples, float Volume, long StartFrame)> redoHistory = new();

    public bool IsDisabled { get; private set; }
    public Panel PanDrawSamples { get; private set; }
    public float Volume { get; private set; } = 1.0f;
    public int SampleRate => sampleRate;
    public int Channels => channels;
    public long Revision { get; private set; }
    public long StartFrame => startFrame;
    public double StartSeconds => startFrame / (double)AudioData.SampleRate;
    public double DurationSeconds => samples.Length / (double)(sampleRate * channels);
    public long TimelineFrameCount => samples.Length == 0 ? 0 : Math.Max(1, (long)Math.Round(
        DurationSeconds * AudioData.SampleRate, MidpointRounding.AwayFromZero));
    public long EndFrame => checked(startFrame + TimelineFrameCount);
    public PlaybackState State => outputDevice?.PlaybackState ?? PlaybackState.Stopped;

    public Track(string file)
    {
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

    public Track Duplicate()
    {
        return new Track(samples, sampleRate, channels, playbackFactory)
        {
            Volume = this.Volume,
            startFrame = this.startFrame,
            IsDisabled = this.IsDisabled
        };
    }

    public void LinkPanels(Panel panDrawSamples, Panel panContainer)
    {
        PanDrawSamples = panDrawSamples;
    }

    private void Changed()
    {
        Revision++;
        PanDrawSamples?.Invalidate();
    }

    private void SaveState()
    {
        if (HistoryOwner is not null)
            return;
        undoHistory.Push((CloneSamples(), Volume, startFrame));
        redoHistory.Clear();
    }

    public bool Undo()
    {
        if (HistoryOwner is not null)
            return HistoryOwner.Undo();
        if (undoHistory.Count == 0)
            return false;
        redoHistory.Push((CloneSamples(), Volume, startFrame));
        var previous = undoHistory.Pop();
        (samples, Volume, startFrame) = previous;
        Changed();
        return true;
    }

    public bool Redo()
    {
        if (HistoryOwner is not null)
            return HistoryOwner.Redo();
        if (redoHistory.Count == 0)
            return false;
        undoHistory.Push((CloneSamples(), Volume, startFrame));
        var next = redoHistory.Pop();
        (samples, Volume, startFrame) = next;
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
        Changed();
    }

    public void ShiftSampleOver(int pixels, int framesPerPixel)
    {
        MoveToFrame(Math.Max(0, checked(startFrame + (long)pixels * Math.Max(1, framesPerPixel))));
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
        int start = (int)Math.Round(startTime * sampleRate, MidpointRounding.AwayFromZero) * channels;
        int end = (int)Math.Round(endTime * sampleRate, MidpointRounding.AwayFromZero) * channels;
        if (start >= end || (start == 0 && end == samples.Length))
            return false;
        SaveState();
        startFrame += (long)Math.Round(start / (double)channels / sampleRate * AudioData.SampleRate,
                                      MidpointRounding.AwayFromZero);
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
        int splitIndex = (int)Math.Round(splitTime * sampleRate, MidpointRounding.AwayFromZero) * channels;
        if (splitIndex <= 0 || splitIndex >= samples.Length)
            return null;

        Track second = Duplicate();
        second.ReplaceSamples(samples[splitIndex..]);
        second.MoveToFrame(checked(startFrame + (long)Math.Round(
            splitIndex / (double)channels / sampleRate * AudioData.SampleRate,
            MidpointRounding.AwayFromZero)));

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
        Changed();
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
        if (playbackAudio is null || playbackAudioRevision != Revision)
        {
            playbackAudio = GetAdjustedSamples();
            playbackAudioRevision = Revision;
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
        Changed();
    }

    public void SaveToFile(string path)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path));
        Directory.CreateDirectory(directory);
        float[] audio = GetAdjustedSamples();
        using WaveFileWriter writer = new(path, AudioData.MixFormat);
        writer.WriteSamples(audio, 0, audio.Length);
    }

    public void DrawSamples(Graphics graphics, int framesPerPixel)
    {
        if (PanDrawSamples is null || samples.Length == 0)
            return;
        framesPerPixel = Math.Max(1, framesPerPixel);
        long firstPixel = startFrame / framesPerPixel;
        int visiblePixels = PanDrawSamples.ClientSize.Width;
        if (firstPixel >= visiblePixels)
            return;
        float center = PanDrawSamples.ClientSize.Height / 2.0f;
        using Pen pen = new(IsDisabled ? Settings.Color.TrackSamples_Disabled : Settings.Color.TrackSamples_Enabled);
        int sourceFrames = samples.Length / channels;
        for (int x = (int)firstPixel; x < visiblePixels; x++)
        {
            double projectFrame = (long)x * framesPerPixel - startFrame;
            int frame = (int)Math.Max(0, Math.Round(projectFrame * sampleRate / AudioData.SampleRate));
            if (frame >= sourceFrames)
                break;
            float minimum = 0;
            float maximum = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                float value = samples[frame * channels + channel] * Volume;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
            graphics.DrawLine(pen, x, center - Math.Clamp(maximum, -1, 1) * center,
                                   x, center - Math.Clamp(minimum, -1, 1) * center);
        }
    }

    public void Dispose()
    {
        outputDevice?.Dispose();
        outputDevice = null;
        playbackAudio = null;
    }
}
