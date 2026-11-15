using MediocreSoundDesigner;
using NAudio.Wave;

internal static partial class Program
{
    private static void RunTrackSelectionChecks()
    {
        Run("Plain selection and Ctrl toggles maintain a valid active track", ToggleTrackSelection);
        Run("Shift selects a contiguous interval from its stable anchor", ShiftTrackSelection);
        Run("Ctrl-Shift adds an interval without clearing prior selection", AddRangeTrackSelection);
        Run("Select all, clear, and outsider clicks are safe", AllTrackSelection);
        Run("Selection navigation preserves audio and Redo history", SelectionIsNotEdit);
        Run("Deleting an active member preserves other selected tracks", SelectionAfterDelete);
        Run("Deleting a selected group is one complete Undo step", GroupDeleteHistory);
        Run("Paste and split Undo restore the previous selected group", StructuralSelectionHistory);
        Run("Reordering preserves selection membership and Shift anchor", ReorderedTrackSelection);
        Run("Failed edit rollback restores the selected group", SelectionRollback);
        Run("Headers display all selected tracks and mark the active one", SelectionHeaderInterface);
        Run("Right-click scrubbing and zoom preserve group selection", SelectionScrubZoom);
        Run("Header rows restore with complete group-delete Undo", HeaderHistory);
        Run("Header and waveform layout stays aligned through scrolling", HeaderLayout);
        Run("Waveform range editing clearly returns to a single active track", RangeSelectsOneTrack);
    }

    private static (TrackManager Manager, Track[] Tracks) SelectionProject(int count = 5)
    {
        TrackManager manager = new(null!);
        Track[] tracks = Enumerable.Range(0, count).Select(_ => new Track(Constant(1000))).ToArray();
        manager.ExecuteEdit(() => { foreach (Track track in tracks) manager.AddTrack(track); });
        manager.ClearHistory();
        return (manager, tracks);
    }

    private static void Chosen(TrackManager manager, params Track[] expected)
        => Assert(manager.SelectedTracks.SequenceEqual(manager.Tracks.Where(expected.Contains)), "Selected group membership/order is incorrect.");

    private static void ToggleTrackSelection()
    {
        var (manager, tracks) = SelectionProject();
        using (manager)
        {
            manager.SelectTrackByClick(tracks[0], false, false); Chosen(manager, tracks[0]);
            manager.SelectTrackByClick(tracks[2], true, false); Chosen(manager, tracks[0], tracks[2]);
            Assert(manager.SelectedTrack == tracks[2], "Ctrl-added track was not made active.");
            manager.SelectTrackByClick(tracks[2], true, false); Chosen(manager, tracks[0]);
            Assert(manager.SelectedTrack == tracks[0], "Removing the active track left a stale active reference.");
            manager.SelectTrackByClick(tracks[0], true, false);
            Assert(manager.SelectedTrack is null && manager.SelectedTracks.Count == 0, "Last toggle did not clear selection.");
        }
    }

    private static void ShiftTrackSelection()
    {
        var (manager, tracks) = SelectionProject();
        using (manager)
        {
            manager.SelectTrack(tracks[1]); manager.SelectTrackByClick(tracks[4], false, true);
            Chosen(manager, tracks[1], tracks[2], tracks[3], tracks[4]);
            manager.SelectTrackByClick(tracks[2], false, true); Chosen(manager, tracks[1], tracks[2]);
            manager.SelectTrackByClick(tracks[0], false, true); Chosen(manager, tracks[0], tracks[1]);
            Assert(manager.SelectedTrack == tracks[0], "Shift target was not active.");
        }
    }

    private static void AddRangeTrackSelection()
    {
        var (manager, tracks) = SelectionProject();
        using (manager)
        {
            manager.SelectTrack(tracks[0]); manager.SelectTrackByClick(tracks[3], true, false);
            manager.SelectTrackByClick(tracks[4], true, true); Chosen(manager, tracks[0], tracks[3], tracks[4]);
        }
    }

    private static void AllTrackSelection()
    {
        var (manager, tracks) = SelectionProject();
        using (manager)
        {
            manager.SelectAllTracks(); Chosen(manager, tracks); Assert(manager.SelectedTrack == tracks[0], "Select all lacks an active track.");
            using Track outsider = new(Constant(10)); manager.SelectTrackByClick(outsider, false, false); Chosen(manager, tracks);
            manager.ClearTrackSelection(); Chosen(manager); Assert(manager.SelectedTrack is null, "Clear retained the active track.");
        }
        using TrackManager empty = new(null!); empty.SelectAllTracks(); empty.ClearTrackSelection();
        Assert(empty.SelectedTracks.Count == 0 && empty.SelectedTrack is null, "Empty selection operations failed.");
    }

    private static void SelectionIsNotEdit()
    {
        var (manager, tracks) = SelectionProject();
        using (manager)
        {
            float[] mixed = manager.GetMixedSamples(); tracks[0].FadeIn(); manager.Undo();
            manager.SelectTrack(tracks[0]); manager.SelectTrackByClick(tracks[2], true, false);
            manager.SelectAllTracks(); manager.ClearTrackSelection();
            Assert(!manager.CanUndo && manager.CanRedo, "Selection changes altered edit history.");
            Equal(manager.GetMixedSamples(), mixed, 0);
        }
    }

    private static void SelectionAfterDelete()
    {
        var (manager, tracks) = SelectionProject();
        using (manager)
        {
            manager.SelectTrack(tracks[0]); manager.SelectTrackByClick(tracks[2], true, false); manager.RemTrack(tracks[2]);
            Chosen(manager, tracks[0]); Assert(manager.SelectedTrack == tracks[0] && !manager.IsTrackSelected(tracks[2]), "Delete retained a stale active/selected track.");
            manager.Undo(); Chosen(manager, tracks[0], tracks[2]); Assert(manager.SelectedTrack == tracks[2], "Delete Undo lost group/active state.");
        }
    }

    private static void GroupDeleteHistory()
    {
        var (manager, tracks) = SelectionProject();
        using (manager)
        {
            manager.SelectTrack(tracks[0]); manager.SelectTrackByClick(tracks[2], true, false);
            manager.RemoveSelectedTracks(); Assert(manager.Tracks.Count == 3 && !manager.Tracks.Contains(tracks[0]) && !manager.Tracks.Contains(tracks[2]), "Group delete removed the wrong tracks.");
            Assert(manager.Undo() && manager.Tracks.Count == 5 && !manager.CanUndo, "Group delete did not form one Undo step.");
            Chosen(manager, tracks[0], tracks[2]); Assert(manager.SelectedTrack == tracks[2], "Group delete Undo lost active track.");
            Assert(manager.Redo() && manager.Tracks.Count == 3 && manager.SelectedTracks.All(manager.Tracks.Contains), "Redo retained stale selection members.");
        }
    }

    private static void StructuralSelectionHistory()
    {
        var (manager, tracks) = SelectionProject();
        using (manager)
        using (Track copy = tracks[0].Duplicate())
        {
            manager.SelectTrack(tracks[0]); manager.SelectTrackByClick(tracks[2], true, false);
            Track pasted = manager.PasteTrack(copy); Chosen(manager, pasted);
            manager.Undo(); Chosen(manager, tracks[0], tracks[2]);
            manager.SeekToFrame(500); Track tail = manager.SplitAtPlayhead(); Chosen(manager, tail);
            manager.Undo(); Chosen(manager, tracks[0], tracks[2]); Assert(manager.SelectedTrack == tracks[2], "Split Undo lost the primary selection.");
        }
    }

    private static void ReorderedTrackSelection()
    {
        var (manager, tracks) = SelectionProject();
        using (manager)
        {
            manager.SelectTrack(tracks[1]); manager.SelectTrackByClick(tracks[3], true, false);
            manager.MoveTrack(tracks[1], 4); Chosen(manager, tracks[1], tracks[3]);
            manager.Undo(); Chosen(manager, tracks[1], tracks[3]);
            manager.SelectTrackByClick(tracks[4], false, true); Chosen(manager, tracks[3], tracks[4]);
        }
    }

    private static void SelectionRollback()
    {
        var (manager, tracks) = SelectionProject();
        using (manager)
        {
            manager.SelectTrack(tracks[0]); manager.SelectTrackByClick(tracks[2], true, false);
            try { manager.ExecuteEdit(() => { manager.ClearTrackSelection(); tracks[0].FadeIn(); throw new InvalidOperationException("Expected rollback"); }); }
            catch (InvalidOperationException e) when (e.Message == "Expected rollback") { }
            Chosen(manager, tracks[0], tracks[2]); Assert(manager.SelectedTrack == tracks[2] && !manager.CanUndo, "Rollback lost selection or added history.");
        }
    }

    private static void SelectionHeaderInterface()
    {
        using FrmMain form = new(); TrackManager manager = Field<TrackManager>(form, "trackManager");
        try
        {
            Track first = new(Constant(4410)), second = new(Constant(4410)); manager.AddTrack(first); manager.AddTrack(second);
            Pointer(first.TrackHeader, "OnMouseDown", MouseButtons.Left, 10);
            Assert(manager.IsTrackSelected(first), "Header click was not wired to selection.");
            manager.SelectTrack(first); manager.SelectTrackByClick(second, true, false);
            Assert(first.TrackHeader.Text == first.Name && second.TrackHeader.Text.Contains("active") &&
                (first.TrackHeader.AccessibleName?.Contains("selected") ?? false) && (second.TrackHeader.AccessibleName?.Contains("selected") ?? false), "Headers do not identify all selected tracks and their active member.");
            Assert(Field<Label>(form, "lblSelectionRange").Text.Contains("2 selected"), "Selection summary did not update.");
            Assert(first.PanDrawSamples.BorderStyle == BorderStyle.None && second.PanDrawSamples.BorderStyle == BorderStyle.None,
                "Selected headers shifted waveform client origins.");
        }
        finally { manager.Dispose(); }
    }

    private static void SelectionScrubZoom() => WithTimeline((form, manager, devices) =>
    {
        Track[] tracks = manager.Tracks.ToArray(); manager.SelectAllTracks();
        manager.PlayAll();
        Pointer(tracks[0].PanDrawSamples, "OnMouseDown", MouseButtons.Right, 100);
        Pointer(tracks[0].PanDrawSamples, "OnMouseUp", MouseButtons.Right, 100);
        typeof(Control).GetMethod("OnMouseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(tracks[0].PanDrawSamples, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 100, 10, 0) });
        Chosen(manager, tracks);
        manager.DrawTimescale = 999; InvokeTimeline(form, "UpdateSizes"); Chosen(manager, tracks);
        Assert(manager.State == PlaybackState.Playing && manager.SelectedTrack == tracks[0], "Scrubbing changed active track/playback state.");
    });

    private static void HeaderHistory() => WithTimeline((form, manager, _) =>
    {
        Track[] tracks = manager.Tracks.ToArray(); manager.SelectAllTracks();
        Control[] headers = tracks.Select(t => t.TrackHeader).ToArray();
        manager.RemoveSelectedTracks(); Assert(headers.All(h => h.Parent is null && !h.IsDisposed), "Deleted header was disposed instead of retained for Redo.");
        manager.Undo(); Chosen(manager, tracks);
        Panel panel = Field<Panel>(form, "panTracks");
        Assert(headers.All(h => h.Parent == panel) && panel.Controls.OfType<Panel>().Count() == 2, "Undo did not restore matching headers and waveforms.");
        manager.Redo(); Assert(headers.All(h => h.Parent is null), "Redo left a ghost track header.");
    });

    private static void HeaderLayout() => WithTimeline((form, manager, _) =>
    {
        for (int i = 0; i < 8; i++) manager.AddTrack(new(Constant(100)));
        foreach (Size size in new[] { new Size(650, 500), new Size(1280, 720) })
        {
            form.Size = size; InvokeTimeline(form, "UpdateSizes");
            int width = Field<TimelineRuler>(form, "timelineRuler").Width;
            foreach (Track track in manager.Tracks)
                Assert(track.TrackHeader.Bottom == track.PanDrawSamples.Top && track.TrackHeader.Width == width && track.PanDrawSamples.Width == width,
                    "Track headers overlap or shift their waveforms.");
        }
    });

    private static void RangeSelectsOneTrack() => WithTimeline((form, manager, _) =>
    {
        manager.SelectAllTracks(); Track first = manager.Tracks[0];
        Pointer(first.PanDrawSamples, "OnMouseDown", MouseButtons.Left, 50);
        Pointer(first.PanDrawSamples, "OnMouseUp", MouseButtons.Left, 100);
        Chosen(manager, first);
        Assert(manager.RangeSelection == new TimelineSelection(first, 5000, 10000), "Range edit did not clearly choose one active track.");
    });
}
