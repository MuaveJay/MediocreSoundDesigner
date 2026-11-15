using MediocreSoundDesigner;
using NAudio.Wave;

internal static partial class Program
{
    private static void RunTrackIdentityChecks()
    {
        Run("Default track names and colors are meaningful and distinguishable", DefaultTrackIdentity);
        Run("Imported filenames become track names", ImportedTrackName);
        Run("Generated tracks receive descriptive numbered names", GeneratedTrackNames);
        Run("Rename validates single-line names and preserves audio", TrackNameValidation);
        Run("Rename and color changes Undo and Redo independently", IdentityHistory);
        Run("Standalone metadata Undo retains track identity", StandaloneIdentityHistory);
        Run("Copy/paste preserves source identity and uses one Copy suffix", CopyTrackIdentity);
        Run("Split tails inherit color and receive a related name", SplitTrackIdentity);
        Run("Reordering and audio edits preserve names and colors", IdentityAcrossEdits);
        Run("Selected and muted track colors retain readable contrast", IdentityContrast);
        Run("Names and color changes do not invalidate active audio playback", IdentityPlayback);
        Run("Headers expose rename/color controls and stable names", IdentityHeaderInterface);
        Run("Name dialog validates input without changing the track prematurely", TrackNameDialogValidation);
        Run("No-op and invalid identity edits preserve Redo", NoOpIdentity);
    }

    private static void DefaultTrackIdentity()
    {
        var (manager, tracks) = SelectionProject(16);
        using (manager)
        {
            Assert(tracks.Select(t => t.Name).Distinct().Count() == tracks.Length && tracks.All(t => t.Name.StartsWith("Audio ")),
                "Default names are missing or indistinguishable.");
            Assert(tracks.All(t => !t.TrackColor.IsEmpty) && tracks.Select(t => t.TrackColor.ToArgb()).Distinct().Count() == tracks.Length,
                "Default colors are missing or repeat too early.");
        }
    }

    private static void ImportedTrackName()
    {
        string path = Path.Combine(Path.GetTempPath(), "Footsteps-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (WaveFileWriter writer = new(path, AudioData.MixFormat)) { float[] audio = Constant(100, 0.2f, 2); writer.WriteSamples(audio, 0, audio.Length); }
            using TrackManager manager = new(null!); Track track = new(path); manager.AddTrack(track);
            Assert(track.Name == Path.GetFileNameWithoutExtension(path), "Import discarded the filename identity.");
        }
        finally { File.Delete(path); }
    }

    private static void GeneratedTrackNames()
    {
        using FrmMain form = new(); TrackManager manager = Field<TrackManager>(form, "trackManager");
        try
        {
            ClickHandler(form, "menuGenNoise_Click"); ClickHandler(form, "menuGenGradientNoise_Click");
            Assert(manager.Tracks[0].Name.StartsWith("Noise ") && manager.Tracks[1].Name.StartsWith("Gradient Noise "),
                "Generated track defaults are not descriptive.");
        }
        finally { manager.Dispose(); }
    }

    private static void TrackNameValidation()
    {
        using Track track = new(Constant(1000)); float[] samples = track.CloneSamples();
        foreach (string invalid in new[] { "", "   ", "line\nbreak", "tab\tname", "line\u2028break", new string('a', 101) })
            Assert(!track.Rename(invalid), "Invalid name was accepted.");
        Assert(track.Rename("  Rain & Wind  ") && track.Name == "Rain & Wind", "Valid name was not trimmed/preserved.");
        Equal(track.CloneSamples(), samples, 0);
    }

    private static void IdentityHistory()
    {
        var (manager, tracks) = SelectionProject(2);
        using (manager)
        {
            Track track = tracks[0]; string original = track.Name; int color = track.TrackColor.ToArgb();
            track.Rename("Wind"); track.SetTrackColor(Color.Crimson);
            Assert(manager.Undo() && track.Name == "Wind" && track.TrackColor.ToArgb() == color, "Color Undo lost name/old color.");
            Assert(manager.Undo() && track.Name == original && !manager.CanUndo, "Rename Undo failed.");
            manager.Redo(); manager.Redo(); Assert(track.Name == "Wind" && track.TrackColor.ToArgb() == Color.Crimson.ToArgb(), "Metadata Redo failed.");
        }
    }

    private static void StandaloneIdentityHistory()
    {
        using Track track = new(Constant(100)); string before = track.Name;
        track.Rename("Standalone"); track.SetTrackColor(Color.Red);
        Assert(track.Undo() && track.TrackColor.IsEmpty && track.Name == "Standalone", "Standalone color Undo failed.");
        Assert(track.Undo() && track.Name == before, "Standalone rename Undo failed.");
        track.Redo(); track.Redo(); Assert(track.Name == "Standalone" && track.TrackColor.ToArgb() == Color.Red.ToArgb(), "Standalone metadata Redo failed.");
    }

    private static void CopyTrackIdentity()
    {
        var (manager, tracks) = SelectionProject(1);
        using (manager)
        {
            Track source = tracks[0]; source.Rename("Rain"); source.SetTrackColor(Color.SteelBlue); manager.ClearHistory();
            using Track clipboard = source.Duplicate(preserveIdentity: true);
            Track copy = manager.PasteTrack(clipboard);
            Assert(clipboard.Name == "Rain" && copy.Name == "Rain Copy" && copy.TrackColor.ToArgb() == source.TrackColor.ToArgb(),
                "Copy/paste accumulated unwanted Copy suffixes or lost color.");
            copy.Rename("Thunder"); Assert(source.Name == "Rain", "Renaming the copy changed its source.");
        }
    }

    private static void SplitTrackIdentity()
    {
        var (manager, tracks) = SelectionProject(1);
        using (manager)
        {
            Track source = tracks[0]; source.Rename(new string('R', 100)); source.SetTrackColor(Color.Orange); manager.SelectTrack(source); manager.ClearHistory();
            manager.SeekToFrame(337); Track tail = manager.SplitAtPlayhead();
            Assert(tail.Name.EndsWith(" Split") && tail.Name.Length == 100 && tail.TrackColor.ToArgb() == source.TrackColor.ToArgb(),
                "Split tail lost a readable related identity.");
            manager.Undo(); manager.Redo(); Assert(manager.Tracks[1].Name == tail.Name && tail.StartFrame == 337, "Split identity/placement changed through history.");
        }
    }

    private static void IdentityAcrossEdits()
    {
        var (manager, tracks) = SelectionProject(3);
        using (manager)
        {
            Track track = tracks[0]; track.Rename("Persistent"); track.SetTrackColor(Color.Purple); manager.ClearHistory();
            manager.MoveTrack(track, 2); track.FadeIn(); track.MoveToFrame(1000); track.ToggleDisabled();
            Assert(track.Name == "Persistent" && track.TrackColor.ToArgb() == Color.Purple.ToArgb(), "Audio/reorder edits changed identity.");
            while (manager.CanUndo) manager.Undo();
            Assert(track.Name == "Persistent" && track.TrackColor.ToArgb() == Color.Purple.ToArgb(), "Unrelated Undo changed identity.");
        }
    }

    private static void IdentityContrast()
    {
        var (manager, tracks) = SelectionProject(2);
        using (manager)
        {
            Track track = tracks[0]; using Panel panel = new(); track.LinkPanels(panel, panel);
            foreach (Color color in new[] { Color.Black, Color.White, Color.Gray, Color.Yellow, Color.Navy, Color.Lime })
            foreach (bool selected in new[] { false, true })
            {
                track.SetTrackColor(color); if (selected) manager.SelectTrack(track); else manager.ClearTrackSelection();
                Color background = TrackVisuals.Background(track, selected); panel.BackColor = background;
                Assert(TrackVisuals.Contrast(TrackVisuals.Waveform(track), background) >= 3, "Waveform color is unreadable.");
                Assert(TrackVisuals.Contrast(TrackVisuals.Readable(Settings.Color.WinFg, background), background) >= 3, "Header text is unreadable.");
            }
            track.ToggleDisabled(); Assert(TrackVisuals.Contrast(TrackVisuals.Waveform(track), panel.BackColor) >= 3, "Muted waveform is unreadable.");
        }
    }

    private static void IdentityPlayback()
    {
        var (manager, source, devices) = PlaybackProject();
        using (manager)
        {
            manager.PlayAll(); devices[0].AdvanceFrames(11025); long revision = source.MixRevision;
            source.Rename("Live Name"); source.SetTrackColor(Color.Red); manager.RefreshPlaybackAfterEdit();
            Assert(source.MixRevision == revision && devices.Count == 1 && !devices[0].Disposed && manager.GetPlayheadFrame() == 11025,
                "Visual identity edits rebuilt/interrupted audio.");
            manager.Undo(); manager.Undo(); manager.RefreshPlaybackAfterEdit();
            Assert(source.MixRevision == revision && devices.Count == 1 && manager.State == PlaybackState.Playing,
                "Metadata Undo invalidated unchanged audio.");
        }
    }

    private static void IdentityHeaderInterface() => WithTimeline((form, manager, _) =>
    {
        Track track = manager.Tracks[0]; track.Rename("Rain & Wind"); track.SetTrackColor(Color.HotPink); manager.SelectAllTracks();
        Assert(track.TrackHeader.Text.Contains("Rain & Wind") && track.TrackHeader.ContextMenuStrip?.Items.Count == 2 &&
            track.TrackHeader.AccessibleName?.Contains("selected") == true, "Header lost identity controls or selection state.");
        int color = track.TrackColor.ToArgb(); manager.MoveTrack(track, 1);
        Assert(track.TrackHeader.Text.Contains("Rain & Wind") && track.TrackColor.ToArgb() == color && manager.IsTrackSelected(track),
            "Reordering renumbered or recolored a named track.");
        track.ToggleDisabled(); Assert(track.TrackHeader.Text.Contains("muted"), "Disabled header lacks its muted indicator.");
    });

    private static void TrackNameDialogValidation()
    {
        using TrackNameDialog dialog = new("Before");
        TextBox input = Field<TextBox>(dialog, "nameInput"); Button confirm = Field<Button>(dialog, "confirm");
        input.Text = "  ";
        typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(confirm, new object[] { EventArgs.Empty });
        Assert(dialog.DialogResult != DialogResult.OK, "Name dialog accepted an empty name.");
        input.Text = "  After  ";
        typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(confirm, new object[] { EventArgs.Empty });
        Assert(dialog.DialogResult == DialogResult.OK && dialog.TrackName == "After", "Name dialog did not validate/trim input.");
    }

    private static void NoOpIdentity()
    {
        var (manager, tracks) = SelectionProject(1);
        using (manager)
        {
            Track track = tracks[0]; track.FadeIn(); manager.Undo();
            Assert(!track.Rename(track.Name) && !track.Rename(" ") && !track.SetTrackColor(track.TrackColor) && !track.SetTrackColor(Color.Transparent),
                "No-op/invalid identity edit was accepted.");
            Assert(manager.CanRedo && !manager.CanUndo, "No-op identity edit cleared Redo or created history.");
        }
    }
}
