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
    new ToolStripMenuItem("Copy Active Track", null, (sender, e) =>
    {
        if (trackManager.SelectedTrack is not null)
        {
            copiedTrack = trackManager.SelectedTrack.Duplicate(preserveIdentity: true);
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

        loopItem = new("Loop Playback")
        {
            CheckOnClick = true
        };

        loopItem.CheckedChanged += (sender, e) =>
        {
            trackManager.LoopPlayback = loopItem.Checked;
            UpdateProjectCaption();
        };

        playbackMenu.DropDownItems.Add(loopItem);
        menu.Items.Add(playbackMenu);
        SetupTransport(playbackMenu);
        SetupTimeline();
        SetupSplitControls();
        SetupTrimControls();
        SetupTrackSelection();
        SetupTrackReordering();
        SetupSelectedVolume();
        SetupWaveformView();
        SetupWindowModes();
        SetupPlayheadFollowing();
        SetupShortcutHelp();
        SetupEditorLayout();
        SetupProjectFiles();

        btnPlay.BackgroundImage = null;
        trackManager.DrawTimescale = ZoomScale.FromSlider(trkDrawTimescale.Value);
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
        lblSelectedVolume.ForeColor = Settings.Color.WinFg;
        trkSelectedVolume.BackColor = Settings.Color.WinBg;
        timelineRuler.Invalidate();
        trackManager.ApplySettings();
        ApplyEditorTheme();
    }

    // GUI controls
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11 || (fullscreen && keyData == Keys.Escape))
        {
            ToggleFullscreen();
            return true;
        }
        if (reorderSource is not null)
        {
            CancelTrackReorder();
            if (keyData == Keys.Escape) return true;
        }
        if (keyData == Keys.F2)
        {
            FinishVolumeGesture();
            RenameTrack(trackManager.SelectedTrack);
            return true;
        }
        if (volumeGesture && keyData == Keys.Escape)
        {
            CancelVolumeGesture();
            return true;
        }
        bool sliderNavigation = keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End;
        if (sliderNavigation && (trkSelectedVolume.Focused || trkPlaybackPosition.Focused || trkDrawTimescale.Focused))
            return base.ProcessCmdKey(ref msg, keyData);
        if (volumeGesture)
            FinishVolumeGesture();
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
                    if (MessageBox.Show($"Delete {trackManager.SelectedTracks.Count} selected track(s)? You can undo this action.", "Are You Sure?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation) == DialogResult.Yes)
                    {
                        trackManager.RemoveSelectedTracks();
                        return true;
                    }
                }
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        CancelTrackReorder();
        FinishVolumeGesture();
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
            {
                trackManager.PauseAll();
                PausePlayheadFollowing();
            }
            else
            {
                trackManager.PlayAll();
                ResumePlayheadFollowing();
            }
        });
    }

    private void menuFileOpen_Click(object sender, EventArgs e)
    {
        ofdLoadFile.Title = "Import Audio";
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
        sfdSaveFile.Title = "Export Audio";
        sfdSaveFile.Filter = "Wave Audio (*.wav)|*.wav";
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
        FinishVolumeGesture();
        trackManager.Undo();
        RunPlaybackAction(trackManager.RefreshPlaybackAfterEdit);
    }

    private void menuEditRedo_Click(object sender, EventArgs e)
    {
        FinishVolumeGesture();
        trackManager.Redo();
        RunPlaybackAction(trackManager.RefreshPlaybackAfterEdit);
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
            using FrmEditVolume frm = new();
            frm.SetSound(trackManager.SelectedTrack);
            frm.EditExecutor = action => { trackManager.ExecuteEdit(action); };
            frm.ShowDialog(this);
            RunPlaybackAction(trackManager.RefreshPlaybackAfterEdit);
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
        newTrack.SetInitialName("Recording", numberOnAdd: true);
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
        newTrack.SetInitialName("Noise", numberOnAdd: true);
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
        newTrack.SetInitialName("Gradient Noise", numberOnAdd: true);
        AddTrackAndSelect(newTrack);
    }

    private void tmrUpdatePlaybackState_Tick(object sender, EventArgs e)
    {
        RunPlaybackAction(() =>
        {
            trackManager.UpdatePlayback();
            TickPlayheadFollowing();
        });
    }

    private void trkDrawTimescale_Scroll(object sender, EventArgs e)
    {
        ZoomToScale(ZoomScale.FromSlider(trkDrawTimescale.Value), ZoomAnchorPixel());
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
        CancelTrackReorder();
        if (rangeDragTrack is not null && (!trackManager.Tracks.Contains(rangeDragTrack) || rangeDragTrack.Revision != rangeDragRevision))
            EndRangeDrag();
        if (timelineDragSource?.Tag is Track dragged && !trackManager.Tracks.Contains(dragged))
            EndTimelineDrag();
        foreach (Control panel in panTracks.Controls.Cast<Control>().ToArray())
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
            if (track.TrackHeader is null || track.TrackHeader.IsDisposed)
                CreateTrackHeader(track);
            else if (track.TrackHeader.Parent != panTracks)
                panTracks.Controls.Add(track.TrackHeader);
        }
        trackManager.RefreshSelectionAppearance();
        menuEditUndo.Enabled = trackManager.CanUndo;
        menuEditRedo.Enabled = trackManager.CanRedo;
        UpdateSizes();
        UpdateProjectCaption();
    }

    private Panel CreateTrackPanel(Track track)
    {
        WaveformCanvas pan = new()
        {
            BackColor = Settings.Color.TrackBg_Unselected,
            Width = panTracks.Width,
            Left = 0,
            Top = 0,
        };
        pan.MouseClick += (sender, e) =>
        {
            // Selection is applied once on mouse-down by the range/selection
            // handler. A later click must not collapse a modifier-selected group.
            UpdateSplitControls();
            UpdateTrimControls();
        };
        pan.Tag = track;
        BindTimelineInput(pan);
        pan.WheelZoom += Waveform_Wheel;
        pan.MouseDoubleClick += (sender, e) =>
        {
            if (e.Button != MouseButtons.Left || (ModifierKeys & (Keys.Control | Keys.Shift)) != 0)
                return;
            track.ToggleDisabled();
            track.PanDrawSamples.Invalidate();
        };
        pan.Paint += (sender, e) =>
        {
            track.DrawSamples(e.Graphics, trackManager.DrawTimescale, trackManager.ViewStartFrame);
            PaintRangeSelection(track, e.Graphics, pan.ClientSize);
            if (trackManager.IsTrackSelected(track))
            {
                using Pen outline = new(track == trackManager.SelectedTrack ? Color.Gold : Settings.Color.Playline);
                e.Graphics.DrawRectangle(outline, 0, 0, Math.Max(0, pan.ClientSize.Width - 1), Math.Max(0, pan.ClientSize.Height - 1));
            }
            // Keep the cursor above selection outlines, including the exact end.
            PaintPlayhead(e.Graphics, pan.ClientSize);
        };
        panTracks.Controls.Add(pan);
        track.LinkPanels(pan, panTracks);
        CreateTrackHeader(track);

        return pan;
    }


    private void UpdateSizes()
    {
        LayoutEditor();
        UpdateTransportControls();
    }


    private void trimToolStripMenuItem_Click(object sender, EventArgs e)
    {

    }


}
