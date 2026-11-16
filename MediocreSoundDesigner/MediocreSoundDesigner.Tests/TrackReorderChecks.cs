using MediocreSoundDesigner;
using NAudio.Wave;
using System.Reflection;

internal static partial class Program
{
    private static void RunTrackReorderChecks()
    {
        Run("Header grips preview a drop without changing project content", ReorderPreview);
        Run("First, middle and last tracks move through header gestures", ReorderPositions);
        Run("One reorder restores ordering, selection and range with Undo/Redo", ReorderGestureHistory);
        Run("Canceled and stationary header gestures preserve history", ReorderCancellation);
        Run("Removing a dragged track cancels the pending drop", ReorderRemovedSource);
        Run("Header dragging scrolls a long track list without changing timing", ReorderScrolling);
        Run("Reordering and its history do not restart running playback", ReorderPlayback);
        Run("Header handles remain independent from waveform selection and seeking", ReorderGestureSeparation);
    }

    private static T ReorderField<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static Control Grip(Track track) => track.TrackHeader.Controls["trackReorderHandle"]!;
    private static void ReorderPointer(Control handle, string name, MouseButtons button, int y)
        => typeof(Control).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(handle, new object[] { new MouseEventArgs(button, 1, 10, y, 0) });
    private static void DragTrackTo(Track track, int panelY)
    {
        Control grip = Grip(track);
        ReorderPointer(grip, "OnMouseDown", MouseButtons.Left, 10);
        ReorderPointer(grip, "OnMouseMove", MouseButtons.Left, panelY - track.TrackHeader.Top);
        ReorderPointer(grip, "OnMouseUp", MouseButtons.Left, panelY - track.TrackHeader.Top);
    }

    private static void ReorderPreview() => WithTimeline((form, manager, _) =>
    {
        Track first = manager.Tracks[0]; Control handle = Grip(first);
        Assert(handle.Cursor == Cursors.SizeNS && handle.AccessibleName!.Contains(first.Name), "Grip is missing its drag affordance.");
        ReorderPointer(handle, "OnMouseDown", MouseButtons.Left, 10);
        ReorderPointer(handle, "OnMouseMove", MouseButtons.Left, manager.Tracks[1].PanDrawSamples.Bottom - 2);
        Assert(ReorderField<bool>(form, "reorderActive") && ReorderField<int>(form, "reorderDropIndex") == 1 && !manager.CanUndo && manager.Tracks[0] == first,
            "Preview changed order/history before release.");
        Label marker = Field<Label>(form, "reorderMarker");
        Assert(marker.Parent == Field<Panel>(form, "panTracks") && marker.Height == 3 && marker.Width > 0, "Drop marker is not laid out.");
        InvokeTimeline(form, "CancelTrackReorder");
    });

    private static void ReorderPositions() => WithTimeline((form, manager, _) =>
    {
        for (int i = 0; i < 3; i++) manager.AddTrack(new Track(Constant(1000)));
        Track[] original = manager.Tracks.ToArray(); manager.ClearHistory();
        DragTrackTo(original[0], original[^1].PanDrawSamples.Bottom + 4);
        Assert(manager.Tracks[^1] == original[0], "First-to-last move failed.");
        DragTrackTo(original[0], -5); Assert(manager.Tracks.SequenceEqual(original), "Last-to-first move failed.");
        Track middle = original[2]; DragTrackTo(middle, original[1].TrackHeader.Top);
        Assert(manager.Tracks[1] == middle, "Middle move failed.");
        for (int index = 1; index < manager.Tracks.Count; index++)
            Assert(manager.Tracks[index].TrackHeader.Top > manager.Tracks[index - 1].TrackHeader.Top, "Visual/model order differ.");
    });

    private static void ReorderGestureHistory() => WithTimeline((form, manager, _) =>
    {
        Track first = manager.Tracks[0]; Track second = manager.Tracks[1];
        first.Rename("Rain"); first.SetTrackColor(Color.Orange); first.MoveToFrame(500); first.SetVolume(0.3f);
        manager.SelectAllTracks(); manager.SetRangeSelection(first, 600, 900); manager.SeekToFrame(700);
        float[] audio = first.CloneSamples(); int color = first.TrackColor.ToArgb(); manager.ClearHistory();
        DragTrackTo(first, second.PanDrawSamples.Bottom + 5);
        Chosen(manager, first, second);
        Assert(first.Name == "Rain" && first.StartFrame == 500 && first.Volume == 0.3f && first.TrackColor.ToArgb() == color &&
            manager.RangeSelection?.StartFrame == 600 && manager.GetPlayheadFrame() == 700, "Reorder changed identity, timing or selection.");
        Equal(first.CloneSamples(), audio, 0);
        Assert(manager.Undo() && manager.Tracks[0] == first && !manager.CanUndo && manager.RangeSelection?.EndFrame == 900, "Reorder was not one complete Undo step.");
        Assert(manager.Redo() && manager.Tracks[1] == first && manager.SelectedTrack == first, "Redo failed to restore row selection/order.");
    });

    private static void ReorderCancellation() => WithTimeline((form, manager, _) =>
    {
        Track first = manager.Tracks[0]; first.FadeIn(); manager.Undo(); Control handle = Grip(first);
        ReorderPointer(handle, "OnMouseDown", MouseButtons.Left, 10);
        ReorderPointer(handle, "OnMouseUp", MouseButtons.Left, 10);
        Assert(manager.CanRedo && !manager.CanUndo, "Clicking a grip created an edit.");
        ReorderPointer(handle, "OnMouseDown", MouseButtons.Left, 10);
        ReorderPointer(handle, "OnMouseMove", MouseButtons.Left, 500);
        InvokeTimeline(form, "CancelTrackReorder"); ReorderPointer(handle, "OnMouseUp", MouseButtons.Left, 500);
        Assert(manager.Tracks[0] == first && manager.CanRedo && !manager.CanUndo && !ReorderField<bool>(form, "reorderActive"), "Canceled drag changed content/history.");
        ReorderPointer(handle, "OnMouseDown", MouseButtons.Left, 10);
        ReorderPointer(handle, "OnMouseMove", MouseButtons.None, 500);
        Assert(Field<Control>(form, "reorderSource") is null, "Missing button did not cancel drag.");
    });

    private static void ReorderRemovedSource() => WithTimeline((form, manager, _) =>
    {
        Track first = manager.Tracks[0]; Control handle = Grip(first);
        ReorderPointer(handle, "OnMouseDown", MouseButtons.Left, 10);
        ReorderPointer(handle, "OnMouseMove", MouseButtons.Left, 500);
        manager.RemTrack(first); ReorderPointer(handle, "OnMouseUp", MouseButtons.Left, 500);
        Assert(Field<Control>(form, "reorderSource") is null && manager.Tracks.Count == 1, "Removed track left a pending drag.");
        manager.Undo(); Assert(manager.Tracks[0] == first && Grip(first) == handle, "Undo did not reattach the same header.");
    });

    private static void ReorderScrolling() => WithTimeline((form, manager, _) =>
    {
        for (int i = 0; i < 10; i++) manager.AddTrack(new Track(Constant(1000)));
        Panel panel = Field<Panel>(form, "panTracks"); Track first = manager.Tracks[0]; Control handle = Grip(first); manager.ClearHistory();
        typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(bool) }, null)!.Invoke(panel, new object[] { true });
        InvokeTimeline(form, "UpdateSizes");
        ReorderPointer(handle, "OnMouseDown", MouseButtons.Left, 10);
        ReorderPointer(handle, "OnMouseMove", MouseButtons.Left, panel.ClientSize.Height - 2);
        for (int i = 0; i < 8; i++) InvokeTimeline(form, "ScrollTrackReorder");
        Assert(panel.AutoScrollPosition.Y < 0 && !manager.CanUndo, "Edge drag did not scroll or prematurely edited content.");
        ReorderPointer(handle, "OnMouseUp", MouseButtons.Left, panel.ClientSize.Height - 2 - first.TrackHeader.Top);
        Assert(manager.Tracks.ToList().IndexOf(first) > 2 && first.StartFrame == 0, "Scrolled drop lost row or audio position.");
    });

    private static void ReorderPlayback() => WithTimeline((form, manager, devices) =>
    {
        manager.PlayAll(); devices[0].AdvanceFrames(1000); Track first = manager.Tracks[0];
        DragTrackTo(first, manager.Tracks[1].PanDrawSamples.Bottom + 5); manager.RefreshPlaybackAfterEdit();
        manager.Undo(); manager.RefreshPlaybackAfterEdit(); manager.Redo(); manager.RefreshPlaybackAfterEdit();
        Assert(devices.Count == 1 && !devices[0].Disposed && manager.State == PlaybackState.Playing && manager.GetPlayheadFrame() == 1000,
            "Row order/history restarted audio or moved the cursor.");
    });

    private static void ReorderGestureSeparation() => WithTimeline((form, manager, _) =>
    {
        Track first = manager.Tracks[0];
        Pointer(first.PanDrawSamples, "OnMouseDown", MouseButtons.Right, 30);
        Assert(manager.GetPlayheadFrame() == 3000 && Field<Control>(form, "reorderSource") is null, "Seeking started a row reorder.");
        Pointer(first.PanDrawSamples, "OnMouseUp", MouseButtons.Right, 30);
        Pointer(first.PanDrawSamples, "OnMouseDown", MouseButtons.Left, 10);
        Pointer(first.PanDrawSamples, "OnMouseMove", MouseButtons.Left, 20);
        Pointer(first.PanDrawSamples, "OnMouseUp", MouseButtons.Left, 20);
        Assert(manager.RangeSelection?.DurationFrames == 1000 && !manager.CanUndo && Field<Control>(form, "reorderSource") is null,
            "Waveform range selection was confused with a reorder.");
    });
}
