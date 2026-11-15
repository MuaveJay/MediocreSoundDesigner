namespace MediocreSoundDesigner;

public readonly record struct TimelineTick(long Frame, double Pixel);

public static class TimelineScale
{
    public static long FrameAtPixel(int pixel, double framesPerPixel, long durationFrames, double viewStartFrame = 0)
    {
        double frame = (double.IsFinite(viewStartFrame) ? viewStartFrame : 0) + pixel * ZoomScale.Sanitize(framesPerPixel);
        if (frame <= 0 || durationFrames <= 0) return 0;
        if (frame >= durationFrames) return durationFrames;
        return (long)Math.Round(frame, MidpointRounding.AwayFromZero);
    }

    public static double PixelAtFrame(long frame, double framesPerPixel, double viewStartFrame = 0)
        => (Math.Max(0, frame) - viewStartFrame) / ZoomScale.Sanitize(framesPerPixel);

    public static IReadOnlyList<TimelineTick> MajorTicks(int width, double framesPerPixel, int minimumSpacing = 100, double viewStartFrame = 0)
    {
        if (width <= 0) return Array.Empty<TimelineTick>();
        framesPerPixel = ZoomScale.Sanitize(framesPerPixel);
        minimumSpacing = Math.Max(40, minimumSpacing);
        double minimumSeconds = minimumSpacing * framesPerPixel / AudioData.SampleRate;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(minimumSeconds)));
        double scaled = minimumSeconds / magnitude;
        double stepFrames = (scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10) * magnitude * AudioData.SampleRate;
        double firstIndex = Math.Floor(Math.Max(0, viewStartFrame) / stepFrames);
        List<TimelineTick> ticks = new();
        for (int i = 0; i <= width / minimumSpacing + 2; i++)
        {
            long frame = (long)Math.Ceiling((firstIndex + i) * stepFrames - 1e-7);
            double pixel = PixelAtFrame(frame, framesPerPixel, viewStartFrame);
            if (pixel > width) break;
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

    public static string FormatRulerTimestamp(long frame, double scale)
    {
        if (scale >= 1) return FormatTimestamp(frame);
        long seconds = Math.Max(0, frame) / AudioData.SampleRate;
        long micros = Math.Max(0, frame) % AudioData.SampleRate * 1000000 / AudioData.SampleRate;
        return $"{seconds / 60:00}:{seconds % 60:00}.{micros:000000}";
    }
}
