namespace MediocreSoundDesigner;

public sealed class WaveformCanvas : Panel
{
    public event EventHandler<MouseEventArgs> WheelZoom;
    public WaveformCanvas()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        ResizeRedraw = true;
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using Brush background = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(background, e.ClipRectangle);
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        WheelZoom?.Invoke(this, e);
    }
}

public sealed class ZoomTrackBar : TrackBar
{
    public event EventHandler<MouseEventArgs> WheelZoom;
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        WheelZoom?.Invoke(this, e);
    }
}
