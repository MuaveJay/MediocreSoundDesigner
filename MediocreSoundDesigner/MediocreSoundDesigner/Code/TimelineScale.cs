namespace MediocreSoundDesigner;

public readonly record struct TimelineTick(long Frame, double Pixel);

/// <summary>Shared sample-frame coordinates for the ruler and waveform pointer.</summary>
public static class TimelineScale
{
    public static long FrameAtPixel(int pixel, int framesPerPixel, long durationFrames)
        => Math.Clamp((long)Math.Max(0, pixel) * Math.Max(1, framesPerPixel), 0, Math.Max(0, durationFrames));

    public static double PixelAtFrame(long frame, int framesPerPixel)
        => Math.Max(0, frame) / (double)Math.Max(1, framesPerPixel);

    public static IReadOnlyList<TimelineTick> MajorTicks(int width, int framesPerPixel, int minimumSpacing = 100)
    {
        if (width <= 0)
            return Array.Empty<TimelineTick>();
        framesPerPixel = Math.Max(1, framesPerPixel);
        minimumSpacing = Math.Max(40, minimumSpacing);
        double minimumSeconds = minimumSpacing * (double)framesPerPixel / AudioData.SampleRate;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(minimumSeconds)));
        double scaled = minimumSeconds / magnitude;
        double stepSeconds = (scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10) * magnitude;
        double stepFrames = stepSeconds * AudioData.SampleRate;
        List<TimelineTick> ticks = new();
        for (int index = 0; index <= width / minimumSpacing + 1; index++)
        {
            // Choose the first sample at or after each round time mark. Derive
            // each mark independently so rounding never accumulates along the ruler.
            long frame = (long)Math.Ceiling(index * stepFrames - 1e-7);
            double pixel = PixelAtFrame(frame, framesPerPixel);
            if (pixel > width)
                break;
            ticks.Add(new(frame, pixel));
        }
        return ticks;
    }

    public static string FormatTimestamp(long frame)
    {
        frame = Math.Max(0, frame);
        long seconds = frame / AudioData.SampleRate;
        long milliseconds = frame % AudioData.SampleRate * 1000 / AudioData.SampleRate;
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}.{milliseconds:000}"
            : $"{seconds / 60:00}:{seconds % 60:00}.{milliseconds:000}";
    }
}
