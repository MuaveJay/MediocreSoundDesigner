using MediocreSoundDesigner.Properties;
using System.Text.Json;

namespace MediocreSoundDesigner;

public partial class FrmMain : Form
{
    // fields
    private TrackManager trackManager;
    private Track copiedTrack;
    // ctor
    public FrmMain()
    {
        InitializeComponent();
        Setup();
    }

    // setup
    private void Setup()
    {
        DoubleBuffered = true;
        trackManager = new(panTracks);
        trackManager.ProjectChanged += (sender, e) => RefreshTrackPanels();
        menuEditUndo.Enabled = false;
        menuEditRedo.Enabled = false;
        btnPlay.Parent = this;
        MinimumSize = new Size(650, 500);
        menuEdit.DropDownItems.Add(
    new ToolStripMenuItem("Copy Track", null, (sender, e) =>
    {
        if (trackManager.SelectedTrack is not null)
        {
            copiedTrack = trackManager.SelectedTrack.Duplicate();
        }
    })
    {
        ShortcutKeys = Keys.Control | Keys.C
    });

        menuEdit.DropDownItems.Add(
            new ToolStripMenuItem("Paste Track", null, (sender, e) =>
            {
                if (copiedTrack is null)
                {
                    return;
                }

                trackManager.PasteTrack(copiedTrack);
            })
            {
                ShortcutKeys = Keys.Control | Keys.V
            });
        menuEdit.DropDownItems.Add("Fade In", null, (sender, e) =>
    {
        trackManager.SelectedTrack?.FadeIn();
    });

        menuEdit.DropDownItems.Add("Fade Out", null, (sender, e) =>
        {
            trackManager.SelectedTrack?.FadeOut();
        });

        ToolStripMenuItem playbackMenu = new("Playback");

        ToolStripMenuItem loopItem = new("Loop Playback")
        {
            CheckOnClick = true
        };

        loopItem.CheckedChanged += (sender, e) =>
        {
            trackManager.LoopPlayback = loopItem.Checked;
        };

        playbackMenu.DropDownItems.Add(loopItem);
        menu.Items.Add(playbackMenu);
        SetupTransport(playbackMenu);
        SetupTimeline();
        SetupSplitControls();
        SetupTrimControls();

        btnPlay.BackgroundImage = Resources.play;
        trackManager.DrawTimescale = trkDrawTimescale.Value;
        UpdateSizes();
        ApplySettings();
    }
    private void ApplySettings()
    {
        BackColor = Settings.Color.WinBg;
        panTracks.BackColor = Settings.Color.TrackBg_Unselected;
        lblPlayhead.BackColor = Settings.Color.Playline;
        lblPlaybackTime.ForeColor = Settings.Color.WinFg;
        trkPlaybackPosition.BackColor = Settings.Color.WinBg;
        timelineRuler.BackColor = Settings.Color.WinBg;
        timelineRuler.ForeColor = Settings.Color.WinFg;
        lblSelectionRange.ForeColor = Settings.Color.WinFg;
        timelineRuler.Invalidate();
        trackManager.ApplySettings();
    }

    // GUI controls
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            EndRangeDrag();
            trackManager.ClearRangeSelection();
            UpdateTrimControls();
            return true;
        }
        if (keyData == Keys.Space)
        {
            btnPlay_Click(this, EventArgs.Empty);
            return true;
        }
        if (trackManager.SelectedTrack is not null)
        {
            if (keyData == Keys.Left)
            {
                trackManager.SelectedTrack.ShiftSampleOver(-5, trackManager.DrawTimescale);
                trackManager.SelectedTrack.PanDrawSamples.Invalidate();
                return true;
            }
            else if (keyData == Keys.Right)
            {
                trackManager.SelectedTrack.ShiftSampleOver(+5, trackManager.DrawTimescale);
                trackManager.SelectedTrack.PanDrawSamples.Invalidate();
                return true;
            }
            else if (keyData == Keys.Delete)
            {
                if (trackManager.SelectedTrack is not null)
                {
                    if (MessageBox.Show("Delete the selected track? You can undo this action.", "Are You Sure?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation) == DialogResult.Yes)
                    {
                        trackManager.RemTrack(trackManager.SelectedTrack);
                        return true;
                    }
                }
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        EndRangeDrag();
        EndTimelineDrag();
        trackManager?.Dispose();
        base.OnFormClosed(e);
    }

    private void FrmMain_SizeChanged(object sender, EventArgs e)
    {
        if (trackManager is null)
            return;
        UpdateSizes();
    }

    private void btnPlay_Click(object sender, EventArgs e)
    {
        RunPlaybackAction(() =>
        {
            if (trackManager.State == NAudio.Wave.PlaybackState.Playing)
                trackManager.PauseAll();
            else
                trackManager.PlayAll();
        });
    }

    private void menuFileOpen_Click(object sender, EventArgs e)
    {
        ofdLoadFile.DefaultExt = "wav";
        if (ofdLoadFile.ShowDialog() == DialogResult.OK)
        {
            try
            {
                Track newTrack = new(ofdLoadFile.FileName);
                AddTrackAndSelect(newTrack);
            }
            catch (Exception error)
            {
                MessageBox.Show($"Unable to open audio: {error.Message}", "Open Audio",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    private void menuFileSave_Click(object sender, EventArgs e)
    {
        sfdSaveFile.DefaultExt = "wav";
        if (sfdSaveFile.ShowDialog() == DialogResult.OK)
        {
            trackManager.SaveToFile(sfdSaveFile.FileName);
        }
    }
    private void menuFileSettings_Click(object sender, EventArgs e)
    {
        FrmEditSettings frm = new();
        frm.SetSound(trackManager.SelectedTrack);
        if (frm.ShowDialog() == DialogResult.OK)
        {
            ApplySettings();
        }
    }

    private void menuEditUndo_Click(object sender, EventArgs e)
    {
        trackManager.Undo();
    }

    private void menuEditRedo_Click(object sender, EventArgs e)
    {
        trackManager.Redo();
    }


    private void menuEditFlip_Click(object sender, EventArgs e)
    {
        trackManager.SelectedTrack?.Flip();
        trackManager.SelectedTrack?.PanDrawSamples?.Invalidate();
    }

    private void menuEditReverse_Click(object sender, EventArgs e)
    {
        trackManager.SelectedTrack?.Reverse();
        trackManager.SelectedTrack?.PanDrawSamples?.Invalidate();
    }

    private void menuEditNormalize_Click(object sender, EventArgs e)
    {
        trackManager.SelectedTrack?.Normalize();
    }

    private void menuEditVolume_Click(object sender, EventArgs e)
    {
        if (trackManager.SelectedTrack is not null)
        {
            FrmEditVolume frm = new();
            frm.SetSound(trackManager.SelectedTrack);
            frm.EditExecutor = action => { trackManager.ExecuteEdit(action); };
            frm.ShowDialog();
        }
    }
    private void menuEditEliminateSilence_Click(object sender, EventArgs e)
    {
        if (trackManager.SelectedTrack is not null)
        {
            FrmEditEliminateSilence frm = new();
            frm.SetSound(trackManager.SelectedTrack);
            frm.EditExecutor = action => { trackManager.ExecuteEdit(action); };
            frm.ShowDialog();
        }
    }

    private void menuGenRecord_Click(object sender, EventArgs e)
    {
        Track newTrack = new(Array.Empty<float>());
        using FrmGenRecord frm = new();
        frm.SetSound(newTrack);
        if (frm.ShowDialog() == DialogResult.OK)
            AddTrackAndSelect(newTrack);
        else
            newTrack.Dispose();
    }

    private void menuGenNoise_Click(object sender, EventArgs e)
    {
        const int NUM_SAMPLES = 100_000;
        float[] samples = new float[NUM_SAMPLES];
        Random rand = new();
        for (int i = 0; i < NUM_SAMPLES; i++)
        {
            samples[i] = rand.NextSingle() * 2.0f - 1.0f;
        }
        Track newTrack = new(samples);
        AddTrackAndSelect(newTrack);
    }
    private void menuGenGradientNoise_Click(object sender, EventArgs e)
    {
        const int NUM_SAMPLES = 250_000;
        float[] samples = new float[NUM_SAMPLES];
        Random rand = new();
        float cur = 0;
        float inc = 0.005f;
        int dir = +1;
        for (int i = 0; i < NUM_SAMPLES; i++)
        {
            cur += (inc * dir);
            if (rand.NextSingle() < 0.5f)
            {
                dir *= -1;
            }
            if (cur > 0.9f)
            {
                dir = -1;
            }
            if (cur < -0.9f)
            {
                dir = +1;
            }
            samples[i] = cur;
        }
        Track newTrack = new(samples);
        AddTrackAndSelect(newTrack);
    }

    private void tmrUpdatePlaybackState_Tick(object sender, EventArgs e)
    {
        RunPlaybackAction(trackManager.UpdatePlayback);
    }

    private void trkDrawTimescale_Scroll(object sender, EventArgs e)
    {
        trackManager.DrawTimescale = trkDrawTimescale.Maximum - trkDrawTimescale.Value;
        trackManager.RedrawAll();
        UpdateTransportControls();
    }

    // Helper methods
    private void AddTrackAndSelect(Track track)
    {
        trackManager.ExecuteEdit(() =>
        {
            trackManager.AddTrack(track);
            trackManager.SelectTrack(track);
        });
    }

    private void RefreshTrackPanels()
    {
        if (rangeDragTrack is not null && (!trackManager.Tracks.Contains(rangeDragTrack) || rangeDragTrack.Revision != rangeDragRevision))
            EndRangeDrag();
        if (timelineDragSource?.Tag is Track dragged && !trackManager.Tracks.Contains(dragged))
            EndTimelineDrag();
        foreach (Panel panel in panTracks.Controls.OfType<Panel>().ToArray())
        {
            if (panel.Tag is Track track && !trackManager.Tracks.Contains(track))
                panTracks.Controls.Remove(panel);
        }
        foreach (Track track in trackManager.Tracks)
        {
            if (track.PanDrawSamples is null || track.PanDrawSamples.IsDisposed)
                CreateTrackPanel(track);
            else if (track.PanDrawSamples.Parent != panTracks)
                panTracks.Controls.Add(track.PanDrawSamples);
        }
        trackManager.SelectTrack(trackManager.SelectedTrack);
        menuEditUndo.Enabled = trackManager.CanUndo;
        menuEditRedo.Enabled = trackManager.CanRedo;
        UpdateSizes();
    }

    private Panel CreateTrackPanel(Track track)
    {
        Panel pan = new()
        {
            BackColor = Settings.Color.TrackBg_Unselected,
            Width = panTracks.Width,
            Left = 0,
            Top = 0,
        };
        pan.Click += (sender, e) =>
        {
            trackManager.SelectTrack(track);
            UpdateSplitControls();
            UpdateTrimControls();
        };
        pan.Tag = track;
        BindTimelineInput(pan);
        pan.MouseDoubleClick += (sender, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;
            track.ToggleDisabled();
            track.PanDrawSamples.Invalidate();
        };
        pan.Paint += (sender, e) =>
        {
            track.DrawSamples(e.Graphics, trackManager.DrawTimescale);
            PaintRangeSelection(track, e.Graphics, pan.ClientSize);
            if (trackManager.SelectedTrack == track)
            {
                using Pen outline = new(Settings.Color.Playline);
                e.Graphics.DrawRectangle(outline, 0, 0, Math.Max(0, pan.ClientSize.Width - 1), Math.Max(0, pan.ClientSize.Height - 1));
            }
        };
        panTracks.Controls.Add(pan);
        track.LinkPanels(pan, panTracks);

        return pan;
    }


    private void UpdateSizes()
    {
        const int PADDING = 15;

        int width = Math.Max(100, ClientSize.Width - PADDING * 2);

        // Zoom slider
        trkDrawTimescale.Left = PADDING;
        trkDrawTimescale.Top = menu.Bottom + PADDING;
        trkDrawTimescale.Width = width;


        // Bottom transport position defines the available waveform height.
        btnPlay.AutoSize = false;
        btnPlay.Size = new Size(75, 75);
        btnPlay.BackgroundImageLayout = ImageLayout.Zoom;
        btnPlay.Left = PADDING;
        btnPlay.Top = ClientSize.Height - btnPlay.Height - PADDING;

        // Track area
        panTracks.Left = PADDING;
        LayoutTimelineRuler();
        LayoutRangeReadout();
        panTracks.Top = lblSelectionRange.Bottom + 3;
        panTracks.Width = width;
        panTracks.Height = Math.Max(100,
            btnPlay.Top - panTracks.Top - PADDING);





        LayoutTimelineTracks();

        btnPlay.BringToFront();
        LayoutTransportControls();
        UpdateTransportControls();
    }


    private void trimToolStripMenuItem_Click(object sender, EventArgs e)
    {

    }


}
