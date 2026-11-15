namespace MediocreSoundDesigner;

public static class ZoomScale
{
    public const double Minimum = 0.125;
    public const double Maximum = AudioData.SampleRate * 60.0;
    public static double Sanitize(double scale) => double.IsFinite(scale) && scale > 0 ? Math.Clamp(scale, Minimum, Maximum) : 1;
    public static double FromSlider(int value, int maximum = 1000)
        => Maximum * Math.Pow(Minimum / Maximum, Math.Clamp(value / (double)Math.Max(1, maximum), 0, 1));
    public static int ToSlider(double scale, int maximum = 1000)
        => (int)Math.Clamp(Math.Round(Math.Log(Sanitize(scale) / Maximum) / Math.Log(Minimum / Maximum) * maximum), 0, maximum);
}

public partial class TrackManager
{
    public double ViewStartFrame { get; private set; }
    public long TimelineExtentFrames => Tracks.Where(track => track.TimelineFrameCount > 0)
        .Select(track => track.EndFrame).DefaultIfEmpty(0).Max();

    public void SetViewport(double startFrame, double framesPerPixel, int width)
    {
        DrawTimescale = framesPerPixel;
        double maximum = Math.Max(0, TimelineExtentFrames - Math.Max(0, width) * DrawTimescale);
        ViewStartFrame = Math.Clamp(double.IsFinite(startFrame) ? startFrame : 0, 0, maximum);
    }

    public void ZoomAtPixel(double framesPerPixel, int anchorPixel, int width)
    {
        anchorPixel = Math.Clamp(anchorPixel, 0, Math.Max(0, width));
        double frame = ViewStartFrame + anchorPixel * DrawTimescale;
        double next = ZoomScale.Sanitize(framesPerPixel);
        SetViewport(frame - anchorPixel * next, next, width);
    }
}
