namespace MediocreSoundDesigner;

public readonly record struct WaveformExtrema(float Minimum, float Maximum);

/// <summary>Cached block extrema with logarithmic range queries; source edits rebuild the cache.</summary>
internal sealed class WaveformPeaks
{
    private const int Block = 64;
    private readonly float[] samples;
    private readonly int channels;
    private readonly int frames;
    private readonly int leaf;
    private readonly float[] minimum;
    private readonly float[] maximum;

    public WaveformPeaks(float[] samples, int channels)
    {
        this.samples = samples;
        this.channels = channels;
        frames = samples.Length / channels;
        int blocks = (frames + Block - 1) / Block;
        leaf = 1;
        while (leaf < blocks) leaf *= 2;
        minimum = new float[leaf * 2]; maximum = new float[leaf * 2];
        Array.Fill(minimum, float.PositiveInfinity); Array.Fill(maximum, float.NegativeInfinity);
        for (int frame = 0; frame < frames; frame++)
            for (int channel = 0; channel < channels; channel++)
            {
                float value = samples[frame * channels + channel];
                if (!float.IsFinite(value)) continue;
                int index = leaf + frame / Block;
                minimum[index] = Math.Min(minimum[index], value);
                maximum[index] = Math.Max(maximum[index], value);
            }
        for (int i = leaf - 1; i > 0; i--)
        {
            minimum[i] = Math.Min(minimum[i * 2], minimum[i * 2 + 1]);
            maximum[i] = Math.Max(maximum[i * 2], maximum[i * 2 + 1]);
        }
    }

    public WaveformExtrema Query(int first, int end)
    {
        first = Math.Clamp(first, 0, frames); end = Math.Clamp(end, 0, frames);
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        void Sample(int frame)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                float value = samples[frame * channels + channel];
                if (!float.IsFinite(value)) continue;
                low = Math.Min(low, value); high = Math.Max(high, value);
            }
        }
        while (first < end && first % Block != 0) Sample(first++);
        if (first < end)
        {
            int lastBlock = end / Block;
            int left = leaf + first / Block, right = leaf + lastBlock;
            while (left < right)
            {
                if ((left & 1) != 0) { low = Math.Min(low, minimum[left]); high = Math.Max(high, maximum[left++]); }
                if ((right & 1) != 0) { --right; low = Math.Min(low, minimum[right]); high = Math.Max(high, maximum[right]); }
                left /= 2; right /= 2;
            }
            first = Math.Max(first, lastBlock * Block);
            while (first < end) Sample(first++);
        }
        return float.IsPositiveInfinity(low) ? new(0, 0) : new(low, high);
    }
}

public partial class Track
{
    private WaveformPeaks waveformPeaks;
    private long waveformRevision;
    private long cachedWaveformRevision = -1;

    public WaveformExtrema GetWaveformExtrema(int firstFrame, int endFrame)
    {
        if (waveformPeaks is null || cachedWaveformRevision != waveformRevision)
        {
            waveformPeaks = new(samples, channels);
            cachedWaveformRevision = waveformRevision;
        }
        return waveformPeaks.Query(firstFrame, endFrame);
    }

    public void DrawSamples(Graphics graphics, double framesPerPixel, double viewStartFrame = 0)
    {
        if (PanDrawSamples is null || samples.Length == 0)
            return;
        double scale = ZoomScale.Sanitize(framesPerPixel);
        int width = PanDrawSamples.ClientSize.Width, height = PanDrawSamples.ClientSize.Height;
        double clipStart = TimelineScale.PixelAtFrame(StartFrame, scale, viewStartFrame);
        double clipEnd = TimelineScale.PixelAtFrame(EndFrame, scale, viewStartFrame);
        int left = Math.Clamp((int)Math.Floor(graphics.ClipBounds.Left), 0, width);
        int right = Math.Clamp((int)Math.Ceiling(graphics.ClipBounds.Right), 0, width);
        if (clipStart > right || clipEnd < left)
            return;
        Color color = TrackVisuals.Waveform(this);
        float center = height / 2f;
        float Y(float value) => center - Math.Clamp(float.IsFinite(value) ? value : 0, -1, 1) * Math.Max(0, center - 2);
        using Pen pen = new(color);
        if (scale > 1)
        {
            using Brush flatSamples = new SolidBrush(color);
            for (int x = left; x < right; x++)
            {
                int first = (int)Math.Clamp(Math.Ceiling(viewStartFrame + x * scale - StartFrame), 0, TimelineFrameCount);
                int end = (int)Math.Clamp(Math.Ceiling(viewStartFrame + (x + 1) * scale - StartFrame), 0, TimelineFrameCount);
                if (first >= end) continue;
                WaveformExtrema extrema = GetWaveformExtrema(first, end);
                float top = Y(extrema.Maximum * Volume), bottom = Y(extrema.Minimum * Volume);
                // GDI+ does not reliably rasterize a zero-length line. Keep a flat
                // sample column visible, including silent and zero-volume clips.
                if (top == bottom) graphics.FillRectangle(flatSamples, x, top, 1, 1);
                else graphics.DrawLine(pen, x, top, x, bottom);
            }
        }
        else
        {
            int first = (int)Math.Clamp(Math.Floor(viewStartFrame + left * scale - StartFrame) - 1, 0, TimelineFrameCount - 1);
            int end = (int)Math.Clamp(Math.Ceiling(viewStartFrame + right * scale - StartFrame) + 1, 0, TimelineFrameCount - 1);
            using Brush points = new SolidBrush(color);
            for (int channel = 0; channel < channels; channel++)
            {
                PointF? previous = null;
                for (int frame = first; frame <= end; frame++)
                {
                    PointF point = new((float)TimelineScale.PixelAtFrame(StartFrame + frame, scale, viewStartFrame), Y(samples[frame * channels + channel] * Volume));
                    if (previous is PointF prior) graphics.DrawLine(pen, prior, point);
                    if (scale <= 0.25) graphics.FillEllipse(points, point.X - 2, point.Y - 2, 4, 4);
                    else if (first == end) graphics.FillRectangle(points, point.X, point.Y, 1, 1);
                    previous = point;
                }
            }
        }

        DrawAutomation(graphics, scale, viewStartFrame, height);

        // A real clip edge remains visible even when its first/last sample is quiet.
        using Pen boundary = new(Color.FromArgb(160, color));
        if (clipStart >= 0 && clipStart < width) graphics.DrawLine(boundary, (float)Math.Floor(clipStart), 0, (float)Math.Floor(clipStart), height);
        if (clipEnd >= 0 && clipEnd <= width) graphics.DrawLine(boundary, (float)Math.Min(width - 1, Math.Floor(clipEnd)), 0, (float)Math.Min(width - 1, Math.Floor(clipEnd)), height);
    }

    private void DrawAutomation(Graphics graphics, double scale, double viewStartFrame, int height)
    {
        if (AutomationPoints.Count == 0)
            return;

        // 200% is at the top, 100% in the middle and 0% at the bottom.
        float GainY(float gain) => 8 + (2f - Math.Clamp(gain, 0f, 2f)) / 2f * Math.Max(0, height - 16);
        float FrameX(long frame) => (float)TimelineScale.PixelAtFrame(StartFrame + frame, scale, viewStartFrame);

        using Pen line = new(Color.MediumPurple, 2.5f);
        using Brush marker = new SolidBrush(Color.MediumPurple);
        using Pen markerOutline = new(Color.White, 1.5f);

        // Extend the line from the clip's beginning to its end.
        PointF previous = new(FrameX(0), GainY(AutomationGainAt(0)));
        foreach (var point in AutomationPoints)
        {
            PointF current = new(FrameX(point.Frame), GainY(point.Gain));
            graphics.DrawLine(line, previous, current);
            previous = current;
        }
        PointF end = new(FrameX(TimelineFrameCount), GainY(AutomationGainAt(TimelineFrameCount)));
        graphics.DrawLine(line, previous, end);

        // Draw the editable-looking markers over the line (dragging comes next).
        foreach (var point in AutomationPoints)
        {
            float x = FrameX(point.Frame);
            if (x < graphics.ClipBounds.Left - 6 || x > graphics.ClipBounds.Right + 6)
                continue;
            float y = GainY(point.Gain);
            graphics.FillEllipse(marker, x - 5, y - 5, 10, 10);
            graphics.DrawEllipse(markerOutline, x - 5, y - 5, 10, 10);
        }
    }
}
