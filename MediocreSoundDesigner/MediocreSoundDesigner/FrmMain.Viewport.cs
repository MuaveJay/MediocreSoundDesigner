namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private readonly Button btnFitWaveform = new();
    private readonly HScrollBar scrTimeline = new();
    private int? paintedPlayheadPixel;
    private const int ScrollResolution = 1000000;

    private void SetupWaveformView()
    {
        // The cursor is painted with each waveform; the old designer label is
        // retained only for existing coordinate helpers, not as an overlay window.
        lblPlayhead.Visible = false;
        panTracks.Controls.Remove(lblPlayhead);
        components.Add(lblPlayhead);
        trkDrawTimescale.Minimum = 0;
        trkDrawTimescale.Maximum = 1000;
        trkDrawTimescale.SmallChange = 1;
        trkDrawTimescale.LargeChange = 25;
        trkDrawTimescale.TickStyle = TickStyle.None;
        ((ZoomTrackBar)trkDrawTimescale).WheelZoom += ZoomControl_Wheel;
        timelineRuler.WheelZoom += Waveform_Wheel;
        btnFitWaveform.Name = "btnFitWaveform";
        btnFitWaveform.Text = "Fit";
        btnFitWaveform.AccessibleName = "Fit all tracks in the waveform view";
        btnFitWaveform.UseVisualStyleBackColor = true;
        btnFitWaveform.Click += (sender, e) =>
            ApplyViewport(0, Math.Max(ZoomScale.Minimum, trackManager.TimelineExtentFrames / (double)Math.Max(1, panTracks.ClientSize.Width)));
        scrTimeline.Name = "scrTimeline";
        scrTimeline.AccessibleName = "Horizontal timeline position";
        scrTimeline.Minimum = 0;
        scrTimeline.LargeChange = 10000;
        scrTimeline.SmallChange = 1000;
        scrTimeline.Maximum = ScrollResolution + scrTimeline.LargeChange - 1;
        scrTimeline.Scroll += scrTimeline_Scroll;
        Controls.AddRange(new Control[] { btnFitWaveform, scrTimeline });
    }

    private int ZoomAnchorPixel()
    {
        double x = TimelineScale.PixelAtFrame(trackManager.GetPlayheadFrame(), trackManager.DrawTimescale, trackManager.ViewStartFrame);
        return x >= 0 && x < panTracks.ClientSize.Width ? (int)Math.Round(x) : panTracks.ClientSize.Width / 2;
    }

    private void ApplyViewport(double start, double scale)
    {
        ResetFollowingAfterZoom();
        trackManager.SetViewport(start, scale, panTracks.ClientSize.Width);
        trkDrawTimescale.Value = ZoomScale.ToSlider(trackManager.DrawTimescale);
        trackManager.RedrawAll();
        UpdateTransportControls();
    }

    private void ZoomToScale(double scale, int anchor)
    {
        trackManager.ZoomAtPixel(scale, anchor, panTracks.ClientSize.Width);
        ApplyViewport(trackManager.ViewStartFrame, trackManager.DrawTimescale);
    }

    private void Waveform_Wheel(object sender, MouseEventArgs e)
    {
        double next = trackManager.DrawTimescale / Math.Pow(1.25, e.Delta / 120.0);
        ZoomToScale(next, Math.Clamp(e.X, 0, panTracks.ClientSize.Width));
    }

    private void ZoomControl_Wheel(object sender, MouseEventArgs e)
        => ZoomToScale(trackManager.DrawTimescale / Math.Pow(1.25, e.Delta / 120.0), ZoomAnchorPixel());

    private void scrTimeline_Scroll(object sender, ScrollEventArgs e)
    {
        SuspendPlayheadFollowing();
        double maximum = Math.Max(0, trackManager.TimelineExtentFrames - panTracks.ClientSize.Width * trackManager.DrawTimescale);
        ApplyViewport(maximum * e.NewValue / ScrollResolution, trackManager.DrawTimescale);
    }

    private void UpdateHorizontalNavigation()
    {
        double maximum = Math.Max(0, trackManager.TimelineExtentFrames - panTracks.ClientSize.Width * trackManager.DrawTimescale);
        scrTimeline.Enabled = maximum > 0;
        scrTimeline.Value = maximum == 0 ? 0 : (int)Math.Clamp(Math.Round(trackManager.ViewStartFrame / maximum * ScrollResolution), 0, ScrollResolution);
        btnFitWaveform.Enabled = trackManager.TimelineExtentFrames > 0;
    }

    private void LayoutWaveformNavigation()
    {
        scrTimeline.SetBounds(panTracks.Left, panTracks.Bottom + 4, panTracks.ClientSize.Width, SystemInformation.HorizontalScrollBarHeight);
        trackManager.SetViewport(trackManager.ViewStartFrame, trackManager.DrawTimescale, panTracks.ClientSize.Width);
        btnFitWaveform.BringToFront();
        scrTimeline.BringToFront();
        UpdateHorizontalNavigation();
    }

    private void PaintPlayhead(Graphics graphics, Size size)
    {
        double x = TimelineScale.PixelAtFrame(trackManager.GetPlayheadFrame(), trackManager.DrawTimescale, trackManager.ViewStartFrame);
        if (x < 0 || x > size.Width + 1e-7) return;
        x = Math.Min(x, size.Width);
        using Pen pen = new(Settings.Color.Playline, 2);
        graphics.DrawLine(pen, (float)Math.Floor(x), 0, (float)Math.Floor(x), size.Height);
    }

    private void InvalidatePlayhead(int? next)
    {
        if (next == paintedPlayheadPixel) return;
        foreach (Track track in trackManager.Tracks)
        {
            if (paintedPlayheadPixel is int old) track.PanDrawSamples?.Invalidate(new Rectangle(old - 3, 0, 7, track.PanDrawSamples.Height));
            if (next is int current) track.PanDrawSamples?.Invalidate(new Rectangle(current - 3, 0, 7, track.PanDrawSamples.Height));
        }
        paintedPlayheadPixel = next;
    }
}
