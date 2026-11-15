namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private readonly ToolStripMenuItem menuEditSplitAtTime = new();

    private void SetupSplitControls()
    {
        menuEditSplit.Text = "Split at Playhead";
        menuEditSplit.ShortcutKeys = Keys.Control | Keys.K;
        menuEditSplit.ToolTipText = "Split the selected track at the current playhead.";
        menuEditSplitAtTime.Name = "menuEditSplitAtTime";
        menuEditSplitAtTime.Text = "Split at Time...";
        menuEditSplitAtTime.ShortcutKeys = Keys.Control | Keys.Shift | Keys.K;
        menuEditSplitAtTime.ToolTipText = "Enter a split time measured from the start of the selected track.";
        menuEditSplitAtTime.Click += menuEditSplitAtTime_Click;
        menuEdit.DropDownItems.Insert(menuEdit.DropDownItems.IndexOf(menuEditSplit) + 1, menuEditSplitAtTime);
        menuEdit.DropDownOpening += (sender, e) => UpdateSplitControls();
    }

    private void UpdateSplitControls()
    {
        menuEditSplit.Enabled = trackManager.CanSplitAtPlayhead;
        Track track = trackManager.SelectedTrack;
        menuEditSplitAtTime.Enabled = track is not null && trackManager.Tracks.Contains(track) && track.TimelineFrameCount >= 2;
    }

    private void menuEditSplit_Click(object sender, EventArgs e)
    {
        // The manager captures the cursor once and validates it again at execution.
        trackManager.SplitAtPlayhead();
        UpdateTransportControls();
    }

    private void menuEditSplitAtTime_Click(object sender, EventArgs e)
    {
        Track track = trackManager.SelectedTrack;
        if (track is null || !trackManager.Tracks.Contains(track) || track.TimelineFrameCount < 2)
            return;
        double localSeconds = (trackManager.GetPlayheadFrame() - track.StartFrame) / (double)AudioData.SampleRate;
        using FrmEditSplit form = new();
        form.SetSound(track, localSeconds);
        if (form.ShowDialog(this) == DialogResult.OK && trackManager.SplitTrack(track, form.SplitSeconds) is null)
            MessageBox.Show(this, "Choose a time strictly inside the selected track.", "Split Audio",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        UpdateTransportControls();
    }
}
