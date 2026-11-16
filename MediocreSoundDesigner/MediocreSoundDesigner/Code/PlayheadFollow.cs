namespace MediocreSoundDesigner;

/// <summary>Elapsed-time easing for the view only; audio positions stay on their sample grid.</summary>
public static class PlayheadFollow
{
    public const double FollowThreshold = 0.60;
    public const double Anchor = 0.35;
    public static double NextStart(double current, long playhead, double scale, int width, long extent, double seconds)
    {
        scale = ZoomScale.Sanitize(scale);
        double span = Math.Max(0, width) * scale;
        double maximum = Math.Max(0, extent - span);
        current = Math.Clamp(double.IsFinite(current) ? current : 0, 0, maximum);
        if (width <= 0 || extent <= 0 || !double.IsFinite(seconds) || seconds <= 0) return current;
        double frame = Math.Clamp(playhead, 0, extent);
        double target = Math.Clamp(frame - span * Anchor, 0, maximum);
        double blend = 1 - Math.Exp(-Math.Min(seconds, 0.1) / 0.14);
        double next = current + (target - current) * blend;
        // At extreme zoom or after an explicit seek, a single audio update may
        // span several screens. Keep the cursor inside the available view.
        double minimumVisible = Math.Min(maximum, Math.Max(0, frame - span * 0.90));
        double maximumVisible = Math.Min(maximum, Math.Max(0, frame - span * 0.10));
        return Math.Clamp(next, minimumVisible, maximumVisible);
    }
}
