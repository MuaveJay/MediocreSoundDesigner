using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MediocreSoundDesigner;

/// <summary>The project timeline and mixer use 44.1 kHz stereo sample frames.</summary>
public static class AudioData
{
    public const int SampleRate = 44100;
    public const int Channels = 2;
    public static WaveFormat MixFormat => WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

    public static float[] ReadAll(ISampleProvider source)
    {
        List<float> samples = new();
        float[] buffer = new float[16384];
        int count;
        while ((count = source.Read(buffer)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, count).ToArray());
        }
        return samples.ToArray();
    }

    public static float[] ToMixFormat(float[] samples, int sampleRate, int channels)
    {
        Validate(samples, sampleRate, channels);
        if (samples.Length == 0)
            return Array.Empty<float>();
        if (sampleRate == SampleRate && channels == Channels)
            return (float[])samples.Clone();

        ISampleProvider provider = new AudioArraySampleProvider(samples, sampleRate, channels);
        if (channels == 1)
            provider = new MonoToStereoSampleProvider(provider);
        if (sampleRate != SampleRate)
            provider = new WdlResamplingSampleProvider(provider, SampleRate);

        float[] converted = ReadAll(provider);
        int frames = checked((int)Math.Max(1, Math.Round(
            samples.Length / (double)channels * SampleRate / sampleRate,
            MidpointRounding.AwayFromZero)));
        // Keep duration deterministic even when the resampler rounds its final read.
        Array.Resize(ref converted, checked(frames * Channels));
        return converted;
    }

    internal static void Validate(float[] samples, int sampleRate, int channels)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels is not (1 or 2))
            throw new NotSupportedException("Only mono and stereo audio files are supported.");
        if (samples.Length % channels != 0)
            throw new ArgumentException("Audio must contain complete sample frames.", nameof(samples));
    }
}

internal sealed class AudioArraySampleProvider : ISampleProvider
{
    private readonly float[] samples;
    private int position;
    public WaveFormat WaveFormat { get; }

    public AudioArraySampleProvider(float[] samples, int sampleRate, int channels, int sampleOffset = 0)
    {
        this.samples = samples;
        position = sampleOffset;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    public int Read(Span<float> buffer)
    {
        int available = Math.Min(buffer.Length, samples.Length - position);
        samples.AsSpan(position, available).CopyTo(buffer);
        position += available;
        return available;
    }
}