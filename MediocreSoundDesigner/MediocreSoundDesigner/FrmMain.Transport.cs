using MediocreSoundDesigner.Properties;
using NAudio.Wave;

namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private readonly Button btnStop = new();
    private readonly TrackBar trkPlaybackPosition = new();
    private readonly Label lblPlaybackTime = new();
    private bool draggingPlaybackPosition;

    private void SetupTransport(ToolStripMenuItem playbackMenu)
    {
        btnStop.Name = "btnStop";
        btnStop.Text = "Stop";
        btnStop.AccessibleName = "Stop playback and return to beginning";
        btnStop.UseVisualStyleBackColor = true;
        btnStop.TabIndex = 8;
        btnStop.Click += btnStop_Click;
        btnPlay.AccessibleName = "Play";
        lblDebug.Visible = false;

        trkPlaybackPosition.Name = "trkPlaybackPosition";
        trkPlaybackPosition.AccessibleName = "Playback position";
        trkPlaybackPosition.Minimum = 0;
        trkPlaybackPosition.Maximum = 10000;
        trkPlaybackPosition.SmallChange = 10;
        trkPlaybackPosition.LargeChange = 100;
        trkPlaybackPosition.TickStyle = TickStyle.None;
        trkPlaybackPosition.TabIndex = 9;
        trkPlaybackPosition.Scroll += trkPlaybackPosition_Scroll;
        trkPlaybackPosition.MouseDown += (sender, e) => draggingPlaybackPosition = true;
        trkPlaybackPosition.MouseUp += (sender, e) =>
        {
            draggingPlaybackPosition = false;
            UpdateTransportControls();
        };
        trkPlaybackPosition.MouseCaptureChanged += (sender, e) =>
        {
            if (!trkPlaybackPosition.Capture)
                draggingPlaybackPosition = false;
        };
        lblPlaybackTime.Name = "lblPlaybackTime";
        lblPlaybackTime.AutoSize = true;
        lblPlaybackTime.AccessibleName = "Current time and total duration";
        Controls.AddRange(new Control[] { btnStop, trkPlaybackPosition, lblPlaybackTime });
        playbackMenu.DropDownItems.Insert(0, new ToolStripMenuItem("Play / Pause (Space)", null, btnPlay_Click));
        playbackMenu.DropDownItems.Insert(1, new ToolStripMenuItem("Stop", null, btnStop_Click));
        playbackMenu.DropDownItems.Insert(2, new ToolStripSeparator());
    }

    private void btnStop_Click(object sender, EventArgs e)
    {
        trackManager.StopAll();
        UpdateTransportControls();
    }

    private void trkPlaybackPosition_Scroll(object sender, EventArgs e)
    {
        long frame = (long)Math.Round(trackManager.DurationFrames *
            (double)trkPlaybackPosition.Value / trkPlaybackPosition.Maximum);
        RunPlaybackAction(() => trackManager.SeekToFrame(frame));
    }

    private void RunPlaybackAction(Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            trackManager.StopAll();
            MessageBox.Show(this, $"Unable to play audio: {error.Message}", "Audio Playback",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        UpdateTransportControls();
    }

    private static string FormatTimestamp(long frame)
    {
        long seconds = frame / AudioData.SampleRate;
        long milliseconds = frame % AudioData.SampleRate * 1000 / AudioData.SampleRate;
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}.{milliseconds:000}"
            : $"{seconds / 60:00}:{seconds % 60:00}.{milliseconds:000}";
    }

    private void UpdateTransportControls()
    {
        long duration = trackManager.DurationFrames;
        long frame = trackManager.GetPlayheadFrame();
        bool playing = trackManager.State == PlaybackState.Playing;
        btnPlay.BackgroundImage = playing ? Resources.pause : Resources.play;
        btnPlay.AccessibleName = playing ? "Pause" : "Play";
        btnPlay.Enabled = duration > 0;
        btnPlay.Visible = true;
        btnStop.Enabled = frame > 0 || trackManager.State != PlaybackState.Stopped;
        trkPlaybackPosition.Enabled = duration > 0;
        if (!draggingPlaybackPosition)
            trkPlaybackPosition.Value = duration == 0 ? 0 : (int)Math.Clamp(
                Math.Round(frame * (double)trkPlaybackPosition.Maximum / duration), 0, trkPlaybackPosition.Maximum);
        lblPlaybackTime.Text = $"{FormatTimestamp(frame)} / {FormatTimestamp(duration)}";
        long x = frame / trackManager.DrawTimescale;
        lblPlayhead.Left = x < panTracks.ClientSize.Width ? (int)x : 0;
        lblPlayhead.Visible = trackManager.Tracks.Count > 0 && x < panTracks.ClientSize.Width;
    }

    private void LayoutTransportControls()
    {
        btnStop.SetBounds(btnPlay.Right + 12, btnPlay.Top, 70, btnPlay.Height);
        lblPlaybackTime.Location = new Point(btnStop.Right + 12, btnPlay.Top);
        trkPlaybackPosition.SetBounds(btnStop.Right + 8, lblPlaybackTime.Bottom + 4,
            Math.Max(100, ClientSize.Width - btnStop.Right - 23), 40);
        btnStop.BringToFront();
        trkPlaybackPosition.BringToFront();
        lblPlaybackTime.BringToFront();
    }
}
