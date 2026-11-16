using MediocreSoundDesigner;
using NAudio.Wave;
using System.Reflection;
using System.Runtime.InteropServices;

internal static partial class Program
{
    private static void RunModifierSelectionChecks()
    {
        Run("Native WinForms modifier lookup reaches header selection for complete clicks", ModifierHeaderClicks);
        Run("Ctrl waveform clicks toggle tracks through mouse-up and click", ModifierWaveformClicks);
        Run("Shift waveform clicks retain a stable anchor in both directions", ModifierWaveformRanges);
        Run("Ctrl-Shift adds ranges across headers, waveforms and grips", ModifierMixedSurfaces);
        Run("Modifier grip clicks select without starting reorder or range drags", ModifierGripClicks);
        Run("Releasing modifiers before mouse-up does not collapse a selected group", ModifierReleaseBeforeMouseUp);
        Run("Modifier waveform double-clicks do not mute audio or add history", ModifierDoubleClick);
        Run("Plain waveform drags and right seeking retain their existing behavior", ModifierPlainGestures);
        Run("Modifier selection preserves running audio and Redo, and drives grouped volume", ModifierPlaybackAndVolume);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] state);

    private sealed class ThreadModifiers : IDisposable
    {
        private readonly byte[] previous = new byte[256];
        public ThreadModifiers(Keys keys)
        {
            Assert(GetKeyboardState(previous), "Could not read test-thread keyboard state.");
            byte[] next = (byte[])previous.Clone();
            foreach (int code in new[] { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }) next[code] = 0;
            if ((keys & Keys.Control) != 0) next[0x11] = next[0xA2] = 0x80;
            if ((keys & Keys.Shift) != 0) next[0x10] = next[0xA0] = 0x80;
            // This changes only this test thread's keyboard table. No SendInput,
            // global key presses, desktop capture or visible forms are involved.
            Assert(SetKeyboardState(next), "Could not set test-thread modifiers.");
            Assert((Control.ModifierKeys & (Keys.Control | Keys.Shift)) == keys, "WinForms did not see the test-thread modifiers.");
        }
        public void Dispose() => Assert(SetKeyboardState(previous), "Could not restore test-thread keyboard state.");
    }

    private static void ModifierPointer(Control surface, string name, MouseButtons button = MouseButtons.Left, int x = 50)
        => typeof(Control).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(surface, new object[] { new MouseEventArgs(button, 1, x, 12, 0) });
    private static void ModifierClick(Control surface, Keys keys = Keys.None)
    {
        using ThreadModifiers modifiers = new(keys);
        ModifierPointer(surface, "OnMouseDown");
        ModifierPointer(surface, "OnMouseUp");
        ModifierPointer(surface, "OnMouseClick");
    }
    private static void WithModifierProject(Action<FrmMain, TrackManager, List<FakeOutput>> check)
        => WithTimeline((form, manager, devices) =>
        {
            for (int i = 0; i < 3; i++) manager.AddTrack(new Track(Constant(44100)));
            manager.SelectTrack(manager.Tracks[0]); manager.ClearHistory();
            check(form, manager, devices);
        });

    private static void ModifierHeaderClicks() => WithModifierProject((form, manager, _) =>
    {
        Track[] tracks = manager.Tracks.ToArray();
        ModifierClick(tracks[0].TrackHeader); ModifierClick(tracks[2].TrackHeader, Keys.Control); Chosen(manager, tracks[0], tracks[2]);
        ModifierClick(tracks[0].TrackHeader, Keys.Control); Chosen(manager, tracks[2]);
        ModifierClick(tracks[4].TrackHeader, Keys.Shift); Chosen(manager, tracks);
        ModifierClick(tracks[1].TrackHeader, Keys.Shift); Chosen(manager, tracks[0], tracks[1]);
        Assert(manager.SelectedTrack == tracks[1] && !manager.CanUndo, "Header clicks lost their anchor or created history.");
    });

    private static void ModifierWaveformClicks() => WithModifierProject((form, manager, _) =>
    {
        Track[] tracks = manager.Tracks.ToArray();
        ModifierClick(tracks[1].PanDrawSamples, Keys.Control); Chosen(manager, tracks[0], tracks[1]);
        Assert(manager.SelectedTrack == tracks[1] && manager.RangeSelection is null &&
            Field<Control>(form, "rangeDragSource") is null, "Ctrl-click started trimming instead of selecting.");
        Assert(tracks[0].TrackHeader.AccessibleName!.EndsWith(", selected") && tracks[1].TrackHeader.Text.Contains("active"), "Group/active header appearance was not updated.");
        ModifierClick(tracks[0].PanDrawSamples, Keys.Control); Chosen(manager, tracks[1]);
        ModifierClick(tracks[1].PanDrawSamples, Keys.Control); Chosen(manager);
    });

    private static void ModifierWaveformRanges() => WithModifierProject((form, manager, _) =>
    {
        Track[] tracks = manager.Tracks.ToArray(); ModifierClick(tracks[1].TrackHeader);
        ModifierClick(tracks[4].PanDrawSamples, Keys.Shift); Chosen(manager, tracks[1], tracks[2], tracks[3], tracks[4]);
        ModifierClick(tracks[0].PanDrawSamples, Keys.Shift); Chosen(manager, tracks[0], tracks[1]);
        Assert(manager.RangeSelection is null && manager.SelectedTrack == tracks[0], "Shift range changed active/range state unexpectedly.");
    });

    private static void ModifierMixedSurfaces() => WithModifierProject((form, manager, _) =>
    {
        Track[] tracks = manager.Tracks.ToArray();
        ModifierClick(Grip(tracks[3]), Keys.Control); Chosen(manager, tracks[0], tracks[3]);
        ModifierClick(tracks[4].PanDrawSamples, Keys.Control | Keys.Shift); Chosen(manager, tracks[0], tracks[3], tracks[4]);
        ModifierClick(tracks[2].TrackHeader, Keys.Shift); Chosen(manager, tracks[2], tracks[3]);
    });

    private static void ModifierGripClicks() => WithModifierProject((form, manager, _) =>
    {
        Track[] tracks = manager.Tracks.ToArray(); Control grip = Grip(tracks[1]);
        ModifierClick(grip, Keys.Control); Chosen(manager, tracks[0], tracks[1]);
        using (new ThreadModifiers(Keys.Shift))
        {
            ModifierPointer(Grip(tracks[4]), "OnMouseDown");
            ModifierPointer(Grip(tracks[4]), "OnMouseMove");
            ModifierPointer(Grip(tracks[4]), "OnMouseUp");
        }
        Chosen(manager, tracks[1], tracks[2], tracks[3], tracks[4]);
        Assert(Field<Control>(form, "reorderSource") is null && Field<Control>(form, "rangeDragSource") is null && !manager.CanUndo,
            "Modifier grip gesture started a drag or changed project history.");
    });

    private static void ModifierReleaseBeforeMouseUp() => WithModifierProject((form, manager, _) =>
    {
        Track[] tracks = manager.Tracks.ToArray();
        using (new ThreadModifiers(Keys.Control)) ModifierPointer(tracks[1].PanDrawSamples, "OnMouseDown");
        using (new ThreadModifiers(Keys.None))
        {
            ModifierPointer(tracks[1].PanDrawSamples, "OnMouseUp"); ModifierPointer(tracks[1].PanDrawSamples, "OnMouseClick");
        }
        Chosen(manager, tracks[0], tracks[1]);
        Assert(manager.RangeSelection is null, "Mouse release converted selection into trimming.");
    });

    private static void ModifierDoubleClick() => WithModifierProject((form, manager, _) =>
    {
        Track track = manager.Tracks[0];
        using (new ThreadModifiers(Keys.Control)) ModifierPointer(track.PanDrawSamples, "OnMouseDoubleClick");
        using (new ThreadModifiers(Keys.Shift)) ModifierPointer(track.PanDrawSamples, "OnMouseDoubleClick");
        Assert(!track.IsDisabled && !manager.CanUndo, "Modifier double-click muted audio or added history.");
    });

    private static void ModifierPlainGestures() => WithModifierProject((form, manager, _) =>
    {
        Track[] tracks = manager.Tracks.ToArray(); manager.SelectAllTracks();
        using (new ThreadModifiers(Keys.None))
        {
            ModifierPointer(tracks[0].PanDrawSamples, "OnMouseDown", x: 10);
            ModifierPointer(tracks[0].PanDrawSamples, "OnMouseMove", x: 20);
            ModifierPointer(tracks[0].PanDrawSamples, "OnMouseUp", x: 20);
            ModifierPointer(tracks[0].PanDrawSamples, "OnMouseClick", x: 20);
        }
        Chosen(manager, tracks[0]); Assert(manager.RangeSelection?.DurationFrames == 1000, "Plain range drag no longer works.");
        manager.SelectAllTracks();
        using (new ThreadModifiers(Keys.Control))
        {
            ModifierPointer(tracks[0].PanDrawSamples, "OnMouseDown", MouseButtons.Right, 30);
            ModifierPointer(tracks[0].PanDrawSamples, "OnMouseUp", MouseButtons.Right, 30);
            ModifierPointer(tracks[0].PanDrawSamples, "OnMouseClick", MouseButtons.Right, 30);
        }
        Chosen(manager, tracks); Assert(manager.GetPlayheadFrame() == 3000, "Right seeking changed selection or missed the cursor.");
    });

    private static void ModifierPlaybackAndVolume() => WithModifierProject((form, manager, devices) =>
    {
        Track[] tracks = manager.Tracks.ToArray(); tracks[0].FadeIn(); manager.Undo(); float[] audio = manager.GetMixedSamples();
        manager.PlayAll(); devices[0].AdvanceFrames(1000);
        ModifierClick(tracks[1].PanDrawSamples, Keys.Control); Chosen(manager, tracks[0], tracks[1]);
        Assert(manager.CanRedo && !manager.CanUndo && devices.Count == 1 && !devices[0].Disposed &&
            manager.State == PlaybackState.Playing && manager.GetPlayheadFrame() == 1000, "Modifier selection changed history or playback.");
        Equal(manager.GetMixedSamples(), audio, 0);
        TrackBar volume = Field<TrackBar>(form, "trkSelectedVolume"); volume.Value = 45; ClickHandler(form, "trkSelectedVolume_Scroll");
        Assert(tracks[0].Volume == 0.45f && tracks[1].Volume == 0.45f && tracks.Skip(2).All(track => track.Volume == 1), "Selected volume changed the wrong tracks.");
    });
}
