using System.ComponentModel;

namespace MediocreSoundDesigner;

public sealed class TimelineRuler : Control
{
    private int framesPerPixel = 1;
    private long playheadFrame;
    private long durationFrames;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int FramesPerPixel
    {
        get => framesPerPixel;
        set { if (framesPerPixel != Math.Max(1, value)) { framesPerPixel = Math.Max(1, value); Invalidate(); } }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public long PlayheadFrame
    {
        get => playheadFrame;
        set { if (playheadFrame != value) { playheadFrame = value; Invalidate(); } }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public long DurationFrames
    {
        get => durationFrames;
        set { if (durationFrames != value) { durationFrames = value; Invalidate(); } }
    }

    public TimelineRuler()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = false;
        Cursor = Cursors.SizeWE;
        AccessibleName = "Timeline ruler in minutes, seconds, and milliseconds";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        double endPixel = TimelineScale.PixelAtFrame(durationFrames, framesPerPixel);
        if (endPixel < ClientSize.Width)
        {
            using Brush beyondEnd = new SolidBrush(Color.FromArgb(32, ForeColor));
            e.Graphics.FillRectangle(beyondEnd, (float)endPixel, 0, ClientSize.Width - (float)endPixel, Height);
        }
        using Pen ticksPen = new(Color.FromArgb(170, ForeColor));
        int labelWidth = TextRenderer.MeasureText(
            TimelineScale.FormatTimestamp((long)ClientSize.Width * framesPerPixel), Font).Width;
        var ticks = TimelineScale.MajorTicks(ClientSize.Width, framesPerPixel, Math.Max(100, labelWidth + 16));
        double step = ticks.Count >= 2 ? ticks[1].Pixel : 0;
        foreach (TimelineTick tick in ticks)
        {
            float x = (float)tick.Pixel;
            e.Graphics.DrawLine(ticksPen, x, Height - 11, x, Height - 1);
            string label = TimelineScale.FormatTimestamp(tick.Frame);
            if (x + TextRenderer.MeasureText(label, Font).Width + 4 <= ClientSize.Width)
                TextRenderer.DrawText(e.Graphics, label, Font, new Point((int)x + 4, 2), ForeColor,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            if (step > 0)
                for (int minor = 1; minor < 5; minor++)
                {
                    float minorX = x + (float)(step * minor / 5);
                    if (minorX < ClientSize.Width)
                        e.Graphics.DrawLine(ticksPen, minorX, Height - 5, minorX, Height - 1);
                }
        }
        e.Graphics.DrawLine(ticksPen, 0, Height - 1, Width, Height - 1);
        double playheadX = TimelineScale.PixelAtFrame(playheadFrame, framesPerPixel);
        if (playheadX < ClientSize.Width)
        {
            float x = (float)playheadX;
            using Brush head = new SolidBrush(Settings.Color.Playline);
            using Pen line = new(Settings.Color.Playline, 2);
            e.Graphics.FillPolygon(head, new[] { new PointF(x - 6, Height - 15), new PointF(x + 6, Height - 15), new PointF(x, Height - 7) });
            e.Graphics.DrawLine(line, x, Height - 7, x, Height);
        }
    }
}
