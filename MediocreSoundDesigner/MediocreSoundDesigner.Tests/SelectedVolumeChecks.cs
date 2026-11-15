using MediocreSoundDesigner;
using NAudio.Wave;

internal static partial class Program
{
    private static void RunSelectedVolumeChecks()
    {
        Run("Selected volume affects only chosen tracks", SelectedVolumeTargets);
        Run("Mixed selected volumes become one displayed absolute level", MixedSelectedVolume);
        Run("Selected mute-level volume preserves unselected audio", SelectedMuteVolume);
        Run("One open edit group produces one complete volume Undo", VolumeGroupHistory);
        Run("Cancelling a volume group restores samples, levels, and Redo", CancelVolumeGroup);
        Run("A no-op volume drag preserves Redo", NoOpVolumeGroup);
        Run("Empty selection and invalid volume values are safe", SelectedVolumeBounds);
        Run("Playing volume edits refresh the mix at the current cursor", PlayingSelectedVolume);
        Run("Paused volume edits resume with current selected levels", PausedSelectedVolume);
        Run("Volume controls show mixed, common, and disabled states", VolumeControlStates);
        Run("A slider drag previews several ticks and commits one Undo", VolumeSliderGesture);
        Run("Cancelling the slider gesture restores original levels", VolumeSliderCancel);
        Run("Undo from the UI completes an active slider gesture first", VolumeGestureUndo);
        Run("Selection changes finish the current gesture safely", VolumeGestureSelectionChange);
        Run("Bottom volume and seek sliders fit beside playback at minimum size", SelectedVolumeLayout);
    }

    private static void SelectedVolumeTargets()
    {
        var (manager, tracks) = SelectionProject(3);
        using (manager)
        {
            float[] audio = tracks[0].CloneSamples();
            manager.SelectTrack(tracks[0]); manager.SelectTrackByClick(tracks[2], true, false);
            Assert(manager.SetSelectedVolume(0.35f), "Selected volume did not make an edit.");
            Near(tracks[0].Volume, 0.35); Near(tracks[2].Volume, 0.35); Near(tracks[1].Volume, 1);
            Equal(tracks[0].CloneSamples(), audio, 0); Chosen(manager, tracks[0], tracks[2]);
        }
    }

    private static void MixedSelectedVolume()
    {
        var (manager, tracks) = SelectionProject(3);
        using (manager)
        {
            tracks[0].SetVolume(0.2f); tracks[2].SetVolume(1.8f); manager.ClearHistory();
            manager.SelectTrack(tracks[0]); manager.SelectTrackByClick(tracks[2], true, false);
            manager.SetSelectedVolume(0.65f); Near(tracks[0].Volume, 0.65); Near(tracks[2].Volume, 0.65);
            Assert(manager.Undo() && !manager.CanUndo, "Mixed volume edit was not grouped.");
            Near(tracks[0].Volume, 0.2); Near(tracks[2].Volume, 1.8);
        }
    }

    private static void SelectedMuteVolume()
    {
        var (manager, tracks) = SelectionProject(3);
        using (manager)
        {
            manager.SelectTrack(tracks[0]); manager.SelectTrackByClick(tracks[2], true, false); manager.SetSelectedVolume(0);
            float[] mixed = manager.GetMixedSamples(); Near(mixed[0], 0.25); Near(mixed[1], 0.25);
            Assert(manager.Undo(), "Mute-level volume could not undo."); Near(manager.GetMixedSamples()[0], 0.75);
        }
    }

    private static void VolumeGroupHistory()
    {
        var (manager, tracks) = SelectionProject(3);
        using (manager)
        {
            manager.SelectAllTracks(); manager.BeginEditGroup();
            foreach (float level in new[] { 0.9f, 0.7f, 0.4f, 0.2f }) manager.SetSelectedVolume(level);
            Assert(manager.HasOpenEditGroup && !manager.CanUndo, "Intermediate ticks created Undo entries.");
            Assert(manager.EndEditGroup() && !manager.HasOpenEditGroup && manager.Undo() && !manager.CanUndo,
                "Completed drag was not one Undo step.");
            foreach (Track track in tracks) Near(track.Volume, 1);
            manager.Redo(); foreach (Track track in tracks) Near(track.Volume, 0.2);
            Chosen(manager, tracks);
        }
    }

    private static void CancelVolumeGroup()
    {
        var (manager, tracks) = SelectionProject(3);
        using (manager)
        {
            manager.SelectAllTracks(); tracks[0].FadeIn(); manager.Undo();
            float[] before = manager.GetMixedSamples();
            manager.BeginEditGroup(); manager.SetSelectedVolume(0.3f); manager.CancelEditGroup();
            Equal(manager.GetMixedSamples(), before, 0);
            Assert(!manager.HasOpenEditGroup && !manager.CanUndo && manager.CanRedo, "Cancelling a drag changed history.");
            Chosen(manager, tracks);
        }
    }

    private static void NoOpVolumeGroup()
    {
        var (manager, tracks) = SelectionProject(2);
        using (manager)
        {
            manager.SelectAllTracks(); tracks[0].FadeIn(); manager.Undo();
            manager.BeginEditGroup(); manager.SetSelectedVolume(0.5f); manager.SetSelectedVolume(1);
            Assert(!manager.EndEditGroup() && !manager.CanUndo && manager.CanRedo, "A no-op gesture cleared Redo or added history.");
        }
    }

    private static void SelectedVolumeBounds()
    {
        var (manager, _) = SelectionProject(2);
        using (manager)
        {
            Assert(!manager.SetSelectedVolume(0.5f) && !manager.CanUndo, "No-selection volume created an edit.");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -0.01f, 2.01f })
            {
                try { manager.SetSelectedVolume(invalid); throw new Exception("Invalid volume was accepted"); }
                catch (ArgumentOutOfRangeException) { }
            }
            manager.SelectAllTracks(); manager.SetSelectedVolume(2);
            Assert(manager.SelectedTracks.All(t => t.Volume == 2), "Maximum volume was not accepted.");
        }
    }

    private static void PlayingSelectedVolume()
    {
        var (manager, source, devices) = PlaybackProject();
        using (manager)
        {
            manager.SelectTrack(source); manager.PlayAll(); devices[0].AdvanceFrames(11025);
            manager.SetSelectedVolume(0.25f); manager.RefreshPlaybackAfterEdit();
            Assert(manager.State == PlaybackState.Playing && manager.GetPlayheadFrame() == 11025 && devices[0].Disposed,
                "Playing volume edit left stale output or moved the cursor.");
            byte[] frame = new byte[8]; devices[^1].Provider!.Read(frame); Near(BitConverter.ToSingle(frame, 0), 0.2);
            manager.Undo(); manager.RefreshPlaybackAfterEdit(); devices[^1].Provider!.Read(frame); Near(BitConverter.ToSingle(frame, 0), 0.8);
        }
    }

    private static void PausedSelectedVolume()
    {
        var (manager, source, devices) = PlaybackProject();
        using (manager)
        {
            manager.SelectTrack(source); manager.PlayAll(); devices[0].AdvanceFrames(11025); manager.PauseAll();
            manager.SetSelectedVolume(0.5f); manager.RefreshPlaybackAfterEdit();
            Assert(manager.State == PlaybackState.Paused && devices.Count == 1, "Paused volume adjustment unexpectedly played.");
            manager.PlayAll(); Assert(manager.GetPlayheadFrame() == 11025 && devices.Count == 2, "Paused resume reused stale audio or restarted.");
            byte[] frame = new byte[8]; devices[^1].Provider!.Read(frame); Near(BitConverter.ToSingle(frame, 0), 0.4);
        }
    }

    private static void VolumeControlStates() => WithTimeline((form, manager, _) =>
    {
        TrackBar slider = Field<TrackBar>(form, "trkSelectedVolume"); Label label = Field<Label>(form, "lblSelectedVolume");
        manager.ClearTrackSelection(); ClickHandler(form, "tmrUpdatePlaybackState_Tick");
        Assert(!slider.Enabled && label.Text.Contains("no selection"), "No selection did not disable volume.");
        manager.Tracks[0].SetVolume(0.25f); manager.Tracks[1].SetVolume(0.75f); manager.SelectAllTracks();
        Assert(slider.Enabled && label.Text.Contains("Mixed"), "Mixed levels were not shown clearly.");
        slider.Value = 60; ClickHandler(form, "trkSelectedVolume_Scroll");
        Assert(manager.SelectedTracks.All(t => t.Volume == 0.6f) && !label.Text.Contains("Mixed") && label.Text.Contains("60%"),
            "Slider did not set the displayed absolute percentage for each selected track.");
        Chosen(manager, manager.Tracks.ToArray());
    });

    private static void VolumeSliderGesture() => WithTimeline((form, manager, _) =>
    {
        manager.SelectAllTracks(); manager.ClearHistory(); TrackBar slider = Field<TrackBar>(form, "trkSelectedVolume");
        Pointer(slider, "OnMouseDown", MouseButtons.Left, 10);
        foreach (int percent in new[] { 90, 75, 50, 25 }) { slider.Value = percent; ClickHandler(form, "trkSelectedVolume_Scroll"); }
        Assert(manager.HasOpenEditGroup && !manager.CanUndo, "Slider preview made intermediate Undo entries.");
        Pointer(slider, "OnMouseUp", MouseButtons.Left, 100);
        Assert(!manager.HasOpenEditGroup && manager.Undo() && !manager.CanUndo && manager.SelectedTracks.All(t => t.Volume == 1),
            "Slider release did not commit one complete group edit.");
        manager.Redo(); Assert(manager.SelectedTracks.All(t => t.Volume == 0.25f), "Slider Redo failed.");
    });

    private static void VolumeSliderCancel() => WithTimeline((form, manager, _) =>
    {
        manager.SelectAllTracks(); manager.ClearHistory(); TrackBar slider = Field<TrackBar>(form, "trkSelectedVolume");
        Pointer(slider, "OnMouseDown", MouseButtons.Left, 10); slider.Value = 25; ClickHandler(form, "trkSelectedVolume_Scroll");
        InvokeTimeline(form, "CancelVolumeGesture");
        Assert(!manager.HasOpenEditGroup && !manager.CanUndo && manager.SelectedTracks.All(t => t.Volume == 1) && slider.Value == 100,
            "Cancelling volume did not restore initial levels/control state.");
    });

    private static void VolumeGestureUndo() => WithTimeline((form, manager, _) =>
    {
        manager.SelectAllTracks(); manager.ClearHistory(); TrackBar slider = Field<TrackBar>(form, "trkSelectedVolume");
        Pointer(slider, "OnMouseDown", MouseButtons.Left, 10); slider.Value = 30; ClickHandler(form, "trkSelectedVolume_Scroll");
        ClickHandler(form, "menuEditUndo_Click");
        Assert(!manager.HasOpenEditGroup && manager.CanRedo && manager.SelectedTracks.All(t => t.Volume == 1),
            "Undo during a gesture failed to finish and undo the volume group.");
    });

    private static void VolumeGestureSelectionChange()
    {
        using FrmMain form = new(); TrackManager manager = Field<TrackManager>(form, "trackManager");
        try
        {
            Track first = new(Constant(1000)), second = new(Constant(1000)); manager.AddTrack(first); manager.AddTrack(second);
            manager.SelectTrack(first); manager.ClearHistory(); TrackBar slider = Field<TrackBar>(form, "trkSelectedVolume");
            Pointer(slider, "OnMouseDown", MouseButtons.Left, 10); slider.Value = 40; ClickHandler(form, "trkSelectedVolume_Scroll");
            manager.SelectTrack(second);
            Assert(!manager.HasOpenEditGroup && first.Volume == 0.4f && second.Volume == 1 && slider.Value == 100,
                "Selection change left a gesture open or changed an unselected track.");
        }
        finally { manager.Dispose(); }
    }

    private static void SelectedVolumeLayout() => WithTimeline((form, manager, _) =>
    {
        foreach (Size size in new[] { new Size(650, 500), new Size(1280, 720), new Size(1920, 1080) })
        {
            form.Size = size; InvokeTimeline(form, "UpdateSizes");
            TrackBar volume = Field<TrackBar>(form, "trkSelectedVolume"), seek = Field<TrackBar>(form, "trkPlaybackPosition");
            Label label = Field<Label>(form, "lblSelectedVolume"); Button play = Field<Button>(form, "btnPlay"), stop = Field<Button>(form, "btnStop");
            Assert(volume.Left > stop.Right && seek.Right < volume.Left && volume.Right <= form.ClientSize.Width &&
                volume.Bottom <= play.Bottom && label.Top == play.Top && Field<Panel>(form, "panTracks").Bottom < play.Top,
                "Bottom volume overlaps playback, seeking, or waveforms.");
        }
    });
}
