namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private readonly TrackBar trkSelectedVolume = new();
    private readonly Label lblSelectedVolume = new();
    private bool volumeGesture;

    private void SetupSelectedVolume()
    {
        trkSelectedVolume.Name = "trkSelectedVolume";
        trkSelectedVolume.AccessibleName = "Volume of selected tracks";
        trkSelectedVolume.Minimum = 0;
        trkSelectedVolume.Maximum = 200;
        trkSelectedVolume.Value = 100;
        trkSelectedVolume.TickStyle = TickStyle.None;
        trkSelectedVolume.SmallChange = 1;
        trkSelectedVolume.LargeChange = 10;
        trkSelectedVolume.TabIndex = 10;
        trkSelectedVolume.Scroll += trkSelectedVolume_Scroll;
        trkSelectedVolume.MouseDown += (sender, e) =>
        {
            if (e.Button == MouseButtons.Left)
                BeginVolumeGesture();
        };
        trkSelectedVolume.MouseUp += (sender, e) => FinishVolumeGesture();
        trkSelectedVolume.MouseCaptureChanged += (sender, e) =>
        {
            if (!trkSelectedVolume.Capture)
                FinishVolumeGesture();
        };
        lblSelectedVolume.Name = "lblSelectedVolume";
        lblSelectedVolume.AccessibleName = "Selected track volume percentage or mixed values";
        lblSelectedVolume.AutoEllipsis = true;
        Controls.AddRange(new Control[] { trkSelectedVolume, lblSelectedVolume });
        ToolTip help = new(components);
        help.SetToolTip(trkSelectedVolume, "Sets every selected track to this percentage. Mixed means their current levels differ.");
        trackManager.TrackSelectionChanged += (sender, e) => FinishVolumeGesture();
    }

    private void BeginVolumeGesture()
    {
        if (volumeGesture || trackManager.SelectedTracks.Count == 0)
            return;
        trackManager.BeginEditGroup();
        volumeGesture = true;
    }

    private void FinishVolumeGesture()
    {
        if (!volumeGesture)
            return;
        volumeGesture = false;
        trackManager.EndEditGroup();
        UpdateSelectedVolumeControls();
    }

    private void CancelVolumeGesture()
    {
        if (!volumeGesture)
            return;
        volumeGesture = false;
        trackManager.CancelEditGroup();
        RunPlaybackAction(trackManager.RefreshPlaybackAfterEdit);
    }

    private void trkSelectedVolume_Scroll(object sender, EventArgs e)
    {
        RunPlaybackAction(() =>
        {
            try
            {
                trackManager.SetSelectedVolume(trkSelectedVolume.Value / 100f);
                trackManager.RedrawAll();
                trackManager.RefreshPlaybackAfterEdit();
            }
            catch
            {
                if (volumeGesture)
                {
                    volumeGesture = false;
                    trackManager.CancelEditGroup();
                }
                throw;
            }
        });
    }

    private void UpdateSelectedVolumeControls()
    {
        var chosen = trackManager.SelectedTracks;
        trkSelectedVolume.Enabled = chosen.Count > 0;
        if (chosen.Count == 0)
        {
            lblSelectedVolume.Text = "Volume: no selection";
            return;
        }
        float first = chosen[0].Volume;
        bool mixed = chosen.Any(track => track.Volume != first);
        lblSelectedVolume.Text = mixed ? $"Volume: Mixed ({chosen.Count} tracks)"
            : $"Volume: {first * 100:0.##}% ({chosen.Count} selected)";
        if (!volumeGesture)
            trkSelectedVolume.Value = (int)Math.Clamp(Math.Round(first * 100), 0, 200);
    }

    private void LayoutSelectedVolume()
    {
        int width = Math.Min(Ui(240), Math.Max(Ui(180), ClientSize.Width / 4));
        lblSelectedVolume.SetBounds(ClientSize.Width - width - Ui(12), btnPlay.Top, width, Font.Height + Ui(8));
        trkSelectedVolume.SetBounds(lblSelectedVolume.Left - Ui(4), lblSelectedVolume.Bottom + Ui(2), width + Ui(4), Ui(30));
    }
}
