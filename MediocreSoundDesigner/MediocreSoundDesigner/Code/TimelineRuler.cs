using System.ComponentModel;

namespace MediocreSoundDesigner;

public sealed class TimelineRuler : Control
{
    private double framesPerPixel = 1;
    private double viewStartFrame;
    private long playheadFrame;
    private long durationFrames;
    public event EventHandler<MouseEventArgs> WheelZoom;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double FramesPerPixel
    {
        get => framesPerPixel;
        set { double next = ZoomScale.Sanitize(value); if (framesPerPixel != next) { framesPerPixel = next; Invalidate(); } }
    }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ViewStartFrame
    {
        get => viewStartFrame;
        set { if (viewStartFrame != value) { viewStartFrame = value; Invalidate(); } }
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
        DoubleBuffered = true; ResizeRedraw = true; TabStop = false; Cursor = Cursors.SizeWE;
        AccessibleName = "Timeline ruler with sample-aligned timestamps";
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        WheelZoom?.Invoke(this, e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        double endPixel = TimelineScale.PixelAtFrame(durationFrames, framesPerPixel, viewStartFrame);
        if (endPixel < ClientSize.Width)
        {
            using Brush beyondEnd = new SolidBrush(Color.FromArgb(32, ForeColor));
            float start = (float)Math.Clamp(endPixel, 0, ClientSize.Width);
            e.Graphics.FillRectangle(beyondEnd, start, 0, ClientSize.Width - start, Height);
        }
        using Pen ticksPen = new(Color.FromArgb(170, ForeColor));
        long last = TimelineScale.FrameAtPixel(ClientSize.Width, framesPerPixel, long.MaxValue, viewStartFrame);
        int labelWidth = TextRenderer.MeasureText(TimelineScale.FormatRulerTimestamp(last, framesPerPixel), Font).Width;
        var ticks = TimelineScale.MajorTicks(ClientSize.Width, framesPerPixel, Math.Max(100, labelWidth + 16), viewStartFrame);
        double step = ticks.Count >= 2 ? ticks[1].Pixel - ticks[0].Pixel : 0;
        foreach (TimelineTick tick in ticks)
        {
            float x = (float)tick.Pixel;
            e.Graphics.DrawLine(ticksPen, x, Height - 11, x, Height - 1);
            string label = TimelineScale.FormatRulerTimestamp(tick.Frame, framesPerPixel);
            if (x >= 0 && x + TextRenderer.MeasureText(label, Font).Width + 4 <= ClientSize.Width)
                TextRenderer.DrawText(e.Graphics, label, Font, new Point((int)x + 4, 2), ForeColor, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            if (step > 0)
                for (int minor = 1; minor < 5; minor++)
                {
                    float minorX = x + (float)(step * minor / 5);
                    if (minorX >= 0 && minorX < ClientSize.Width)
                        e.Graphics.DrawLine(ticksPen, minorX, Height - 5, minorX, Height - 1);
                }
        }
        e.Graphics.DrawLine(ticksPen, 0, Height - 1, Width, Height - 1);
        double playheadX = TimelineScale.PixelAtFrame(playheadFrame, framesPerPixel, viewStartFrame);
        if (playheadX >= 0 && playheadX < ClientSize.Width)
        {
            float x = (float)Math.Floor(playheadX);
            using Brush head = new SolidBrush(Settings.Color.Playline);
            using Pen line = new(Settings.Color.Playline, 2);
            e.Graphics.FillPolygon(head, new[] { new PointF(x - 6, Height - 15), new PointF(x + 6, Height - 15), new PointF(x, Height - 7) });
            e.Graphics.DrawLine(line, x, Height - 7, x, Height);
        }
    }
}
