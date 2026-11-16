using MediocreSoundDesigner;
using NAudio.Wave;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

internal static partial class Program
{
    private static void RunProjectFileChecks()
    {
        Run("Project files round-trip edited, split, moved, muted and reordered audio exactly", ProjectRoundTrip);
        Run("Empty projects and empty audio tracks can be saved and reopened", ProjectEmptyRoundTrip);
        Run("Embedded projects reopen after the original WAV is removed", ProjectEmbeddedAudio);
        Run("Project snapshots and loaded tracks own independent sample buffers", ProjectSnapshotIsolation);
        Run("Saved checkpoints track content Undo/Redo while ignoring navigation", ProjectDirtyCheckpoint);
        Run("Saving preserves history and Undo/Redo can revisit the saved state", ProjectSavedHistory);
        Run("A failed replacement retains the original project file and dirty indicator", ProjectAtomicSaveFailure);
        Run("Invalid, truncated and unsupported project containers are rejected", ProjectInvalidContainers);
        Run("Invalid identity, timing, selection and required metadata are rejected", ProjectInvalidMetadata);
        Run("Missing, changed and mismatched embedded audio is rejected", ProjectDamagedAudio);
        Run("Duplicate and unexpected archive paths are rejected without extraction", ProjectArchiveLayout);
        Run("Project metadata, track and decoded-audio limits are enforced before loading", ProjectSizeLimits);
        Run("A failed UI open leaves audio, filename, playback and history untouched", ProjectFailedOpenPreservesSession);
        Run("Canceling Open preserves the complete running session", ProjectCanceledOpen);
        Run("Discarding changes opens a stopped session with fresh history and controls", ProjectDiscardedOpen);
        Run("Saving before reopening the same path uses the newly saved content", ProjectSaveThenReopenSameFile);
        Run("New Project honors Cancel and clears the session only after Discard", ProjectNewChoices);
        Run("Close honors Cancel and saves to the current path when requested", ProjectCloseChoices);
        Run("Exporting a WAV does not clear unsaved project changes", ProjectExportIsSeparate);
        Run("Saving a running session preserves its device, cursor, selection and history", ProjectLiveSave);
        Run("Project, import and export commands have distinct standard hotkeys", ProjectFileMenus);
        Run("Reopened selection anchors retain subsequent Shift-selection behavior", ProjectSelectionAnchor);
        Run("Saving commits one volume gesture and retains its Undo/Redo", ProjectSaveVolumeGesture);
        Run("Project files preserve large integer timeline positions without rounding", ProjectLargeFrames);
        Run("Raw float bit patterns survive project storage without numeric conversion", ProjectFloatBits);
        Run("Failed requested saves prevent both opening another project and closing", ProjectSaveFailureBlocksReplacement);
        Run("Invalid in-memory project data cannot replace a running manager", ProjectInvalidDocumentPreservesManager);
        Run("Replaced projects release old track audio, local history and header menus", ProjectReplacedResources);
        Run("Unicode and long project filenames retain safe atomic-save behavior", ProjectLongFilename);
    }

    private sealed class ProjectTestDirectory : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "MSD-project-check-" + Guid.NewGuid().ToString("N"));
        public ProjectTestDirectory() => Directory.CreateDirectory(Folder);
        public string File(string name = "session.msdproj") => Path.Combine(Folder, name);
        public void Dispose()
        {
            string resolved = Path.GetFullPath(Folder);
            Assert(resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(resolved).StartsWith("MSD-project-check-"), "Unexpected temporary test path.");
            foreach (string file in Directory.EnumerateFiles(resolved)) System.IO.File.Delete(file);
            Directory.Delete(resolved); // Only this test's now-empty directory; never recursive.
        }
    }

    private static T ProjectCall<T>(object target, string name, params object[] args)
        => (T)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!;
    private static void ProjectCall(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static T ProjectField<T>(object target, string name)
        => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void ProjectPrompt(FrmMain form, Func<DialogResult> answer)
        => typeof(FrmMain).GetField("promptUnsavedProject", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, answer);
    private static void ProjectThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (Exception error)
        {
            while (error is TargetInvocationException { InnerException: not null } wrapped) error = wrapped.InnerException!;
            Assert(error is T, "Expected " + typeof(T).Name + ", received " + error.GetType().Name + ": " + error.Message); return;
        }
        throw new InvalidOperationException("Expected " + typeof(T).Name + " was not raised.");
    }
    private static void ProjectAudioEqual(float[] a, float[] b)
        => Assert(MemoryMarshal.AsBytes(a.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(b.AsSpan())), "Project samples changed bits.");
    private static void ProjectDocumentsEqual(ProjectDocument a, ProjectDocument b)
    {
        Assert(a.Tracks.Count == b.Tracks.Count && a.TrackSequence == b.TrackSequence && a.PlayheadFrame == b.PlayheadFrame &&
            a.FramesPerPixel == b.FramesPerPixel && a.ViewStartFrame == b.ViewStartFrame && a.LoopPlayback == b.LoopPlayback && a.FollowPlayhead == b.FollowPlayhead &&
            a.SelectedTracks.SequenceEqual(b.SelectedTracks) && a.ActiveTrack == b.ActiveTrack && a.SelectionAnchor == b.SelectionAnchor &&
            a.Range?.StartFrame == b.Range?.StartFrame && a.Range?.EndFrame == b.Range?.EndFrame, "Project/session metadata changed.");
        for (int i = 0; i < a.Tracks.Count; i++)
        {
            ProjectTrackData x = a.Tracks[i], y = b.Tracks[i];
            Assert(x.Name == y.Name && x.ColorArgb == y.ColorArgb && x.StartFrame == y.StartFrame && x.Volume == y.Volume && x.Muted == y.Muted && x.SampleCount == y.SampleCount,
                "Track metadata changed at index " + i);
            ProjectAudioEqual(x.Samples, y.Samples);
        }
    }
    private static string ProjectFixture(ProjectTestDirectory directory, string name = "fixture.msdproj")
    {
        var (manager, tracks) = SelectionProject(3);
        using (manager)
        {
            tracks[0].Rename("Rain"); tracks[1].Rename("Accent"); tracks[2].Rename("Wind");
            manager.SelectTrack(tracks[0]); manager.SelectTrackByClick(tracks[2], true, false); manager.SetRangeSelection(tracks[2], 100, 500);
            manager.SeekToFrame(500); manager.SetViewport(200.25, 0.5, 400); manager.LoopPlayback = true;
            string path = directory.File(name); ProjectFile.Save(path, manager.CaptureDocument(false)); return path;
        }
    }
    private static void RewriteProject(string source, string destination, Action<JsonObject>? metadata = null,
        Func<string, byte[], byte[]?>? audio = null, string? rawMetadata = null, string? extraEntry = null)
    {
        using ZipArchive input = ZipFile.OpenRead(source);
        using ZipArchive output = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (ZipArchiveEntry entry in input.Entries)
        {
            using MemoryStream data = new(); using (Stream read = entry.Open()) read.CopyTo(data);
            byte[] bytes = data.ToArray();
            if (entry.FullName == "project.json")
            {
                JsonObject root = JsonNode.Parse(bytes)!.AsObject(); metadata?.Invoke(root);
                bytes = Encoding.UTF8.GetBytes(rawMetadata ?? root.ToJsonString());
            }
            else if (audio is not null)
            {
                byte[]? changed = audio(entry.FullName, bytes); if (changed is null) continue; bytes = changed;
            }
            using Stream write = output.CreateEntry(entry.FullName).Open(); write.Write(bytes);
        }
        if (extraEntry is not null) { using Stream extra = output.CreateEntry(extraEntry).Open(); extra.WriteByte(0); }
    }

    private static void ProjectRoundTrip()
    {
        using ProjectTestDirectory directory = new(); var (manager, tracks) = SelectionProject(3);
        using (manager)
        {
            tracks[0].Rename("Rain & Wind"); tracks[0].SetTrackColor(Color.HotPink); tracks[0].MoveToFrame(233); tracks[0].FadeIn(); tracks[0].FadeOut();
            tracks[0].SetVolume(0.7f); manager.SelectTrack(tracks[0]); manager.SeekToFrame(570); Track tail = manager.SplitAtPlayhead();
            tracks[0].ToggleDisabled(); manager.MoveTrack(tail, 0); manager.SelectAllTracks(); manager.SetRangeSelection(tail, 600, 900);
            manager.SeekToFrame(700); manager.SetViewport(500.5, 0.25, 400); manager.LoopPlayback = true;
            ProjectDocument expected = manager.CaptureDocument(false); ProjectFile.Save(directory.File(), expected);
            ProjectDocument loaded = ProjectFile.Read(directory.File()); ProjectDocumentsEqual(expected, loaded);
            using TrackManager reopened = new(null!); reopened.ReplaceWithDocument(loaded, 400);
            ProjectDocumentsEqual(expected, reopened.CaptureDocument(false)); ProjectAudioEqual(manager.GetMixedSamples(), reopened.GetMixedSamples());
            Assert(!reopened.IsProjectDirty && !reopened.CanUndo && !reopened.CanRedo && reopened.State == PlaybackState.Stopped, "Opened project retained dirty/history/playback state.");
        }
    }

    private static void ProjectEmptyRoundTrip()
    {
        using ProjectTestDirectory directory = new(); using TrackManager manager = new(null!);
        ProjectFile.Save(directory.File(), manager.CaptureDocument(true)); manager.ReplaceWithDocument(ProjectFile.Read(directory.File()), 800);
        Assert(manager.Tracks.Count == 0 && !manager.IsProjectDirty, "Empty project could not round-trip.");
        Track blank = new(Array.Empty<float>()); manager.AddTrack(blank); manager.SelectTrack(blank);
        ProjectFile.Save(directory.File(), manager.CaptureDocument(true)); manager.ReplaceWithDocument(ProjectFile.Read(directory.File()), 800);
        Assert(manager.Tracks.Count == 1 && manager.Tracks[0].TimelineFrameCount == 0 && manager.DurationFrames == 0, "Empty audio row was lost.");
    }

    private static void ProjectEmbeddedAudio()
    {
        using ProjectTestDirectory directory = new(); string wave = directory.File("original.wav");
        System.IO.File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "beat-it.wav"), wave);
        using TrackManager manager = new(null!); Track source = new(wave); manager.AddTrack(source); float[] expected = source.CloneSamples();
        ProjectFile.Save(directory.File(), manager.CaptureDocument(true)); System.IO.File.Delete(wave);
        using TrackManager reopened = new(null!); reopened.ReplaceWithDocument(ProjectFile.Read(directory.File()), 800);
        ProjectAudioEqual(expected, reopened.Tracks[0].CloneSamples());
    }

    private static void ProjectSnapshotIsolation()
    {
        var (manager, tracks) = SelectionProject(1);
        using (manager)
        {
            manager.MarkProjectSaved(); float first = tracks[0].CloneSamples()[0]; ProjectDocument document = manager.CaptureDocument(true);
            document.Tracks[0].Samples[0] = 0.91f;
            Assert(tracks[0].CloneSamples()[0] == first && !manager.IsProjectDirty, "Document mutated live samples or the saved checkpoint.");
            using TrackManager reopened = new(null!); reopened.ReplaceWithDocument(document, 400); document.Tracks[0].Samples[0] = -0.91f;
            Assert(reopened.Tracks[0].CloneSamples()[0] == 0.91f, "Loaded audio aliases mutable input data.");
        }
    }

    private static void ProjectDirtyCheckpoint()
    {
        var (manager, tracks) = SelectionProject(2);
        using (manager)
        {
            Assert(manager.IsProjectDirty, "Unsaved tracks appear clean."); manager.MarkProjectSaved();
            foreach (Action edit in new Action[] { () => tracks[0].Rename("Edited"), () => tracks[0].SetTrackColor(Color.Red),
                () => tracks[0].SetVolume(0.4f), () => tracks[0].MoveToFrame(20), () => tracks[0].ToggleDisabled(),
                () => tracks[0].FadeIn(), () => manager.MoveTrack(tracks[0], 1) })
            {
                edit(); Assert(manager.IsProjectDirty, "Content edit did not mark the project dirty.");
                manager.Undo(); Assert(!manager.IsProjectDirty, "Undo to saved state remained dirty.");
                manager.Redo(); Assert(manager.IsProjectDirty, "Redo did not restore dirty state."); manager.Undo();
            }
            manager.SelectAllTracks(); manager.SetRangeSelection(tracks[0], 100, 200); manager.SeekToFrame(150); manager.SetViewport(100, 0.5, 100);
            Assert(!manager.IsProjectDirty, "Navigation alone marks the project modified.");
            manager.LoopPlayback = true; Assert(manager.IsProjectDirty, "Loop setting was excluded from project changes.");
            manager.LoopPlayback = false; Assert(!manager.IsProjectDirty, "Restoring loop setting did not restore the checkpoint.");
        }
    }

    private static void ProjectSavedHistory()
    {
        var (manager, tracks) = SelectionProject(1);
        using (manager)
        {
            tracks[0].FadeIn(); manager.MarkProjectSaved(); Assert(manager.CanUndo && !manager.IsProjectDirty, "Saving cleared history or remained dirty.");
            manager.Undo(); Assert(manager.IsProjectDirty && manager.CanRedo, "Undo before the saved state was not dirty.");
            manager.Redo(); Assert(!manager.IsProjectDirty, "Redo to the saved state stayed dirty.");
        }
    }

    private static void ProjectAtomicSaveFailure() => WithTimeline((form, manager, _) =>
    {
        using ProjectTestDirectory directory = new(); string path = directory.File(); ProjectCall(form, "SaveProjectPath", path);
        byte[] original = System.IO.File.ReadAllBytes(path); manager.Tracks[0].Rename("Unsaved change"); string title = form.Text;
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.None))
            ProjectThrows<IOException>(() => ProjectCall(form, "SaveProjectPath", path));
        Assert(System.IO.File.ReadAllBytes(path).SequenceEqual(original) && manager.IsProjectDirty && form.Text == title &&
            ProjectField<string>(form, "currentProjectPath") == path && !Directory.EnumerateFiles(directory.Folder, "*.tmp").Any(), "Failed atomic save damaged the original, checkpoint or filename.");
        ProjectThrows<DirectoryNotFoundException>(() => ProjectCall(form, "SaveProjectPath", Path.Combine(directory.Folder, "missing", "file.msdproj")));
        Assert(manager.IsProjectDirty && ProjectField<string>(form, "currentProjectPath") == path, "Failed Save As changed the active filename/checkpoint.");
    });

    private static void ProjectInvalidContainers()
    {
        using ProjectTestDirectory directory = new(); string source = ProjectFixture(directory);
        string plain = directory.File("plain.msdproj"); System.IO.File.WriteAllText(plain, "not a project"); ProjectThrows<InvalidDataException>(() => ProjectFile.Read(plain));
        string truncated = directory.File("truncated.msdproj"); System.IO.File.WriteAllBytes(truncated, System.IO.File.ReadAllBytes(source)[..100]); ProjectThrows<InvalidDataException>(() => ProjectFile.Read(truncated));
        foreach (string json in new[] { "[]", "null", "{", "{\"version\":\"wrong\"}" })
        {
            string path = directory.File(Guid.NewGuid().ToString("N") + ".msdproj"); RewriteProject(source, path, rawMetadata: json);
            ProjectThrows<InvalidDataException>(() => ProjectFile.Read(path));
        }
        string future = directory.File("future.msdproj"); RewriteProject(source, future, root => root["version"] = 99); ProjectThrows<InvalidDataException>(() => ProjectFile.Read(future));
    }

    private static void ProjectInvalidMetadata()
    {
        using ProjectTestDirectory directory = new(); string source = ProjectFixture(directory);
        foreach (Action<JsonObject> mutate in new Action<JsonObject>[]
        {
            root => root["format"] = "wrong", root => root["channels"] = 1, root => root["framesPerPixel"] = 0,
            root => root["playheadFrame"] = long.MaxValue, root => root["activeTrack"] = -1,
            root => root["selectedTracks"] = new JsonArray(999), root => root["trackSequence"] = -1,
            root => root["tracks"]![0]!["name"] = " ", root => root["tracks"]![0]!["volume"] = -1,
            root => root["tracks"]![0]!["startFrame"] = -1, root => root["tracks"]![0]!["colorArgb"] = 0,
            root => root["tracks"]![0]!["sampleCount"] = 3, root => root["tracks"]![0]!.AsObject().Remove("volume"),
            root => root["range"]!["endFrame"] = 999999, root => root.Remove("playheadFrame"),
            root => root["range"]!.AsObject().Remove("startFrame"), root => root["unknownField"] = true
        })
        {
            string path = directory.File(Guid.NewGuid().ToString("N") + ".msdproj"); RewriteProject(source, path, mutate);
            ProjectThrows<InvalidDataException>(() => ProjectFile.Read(path));
        }
    }

    private static void ProjectDamagedAudio()
    {
        using ProjectTestDirectory directory = new(); string source = ProjectFixture(directory);
        string missing = directory.File("missing.msdproj"); RewriteProject(source, missing, audio: (name, bytes) => name.EndsWith("000000.f32") ? null : bytes);
        ProjectThrows<InvalidDataException>(() => ProjectFile.Read(missing));
        string changed = directory.File("changed.msdproj"); RewriteProject(source, changed, audio: (name, bytes) => { bytes[0] ^= 1; return bytes; });
        ProjectThrows<InvalidDataException>(() => ProjectFile.Read(changed));
        string shortened = directory.File("short.msdproj"); RewriteProject(source, shortened, audio: (name, bytes) => bytes[..^4]);
        ProjectThrows<InvalidDataException>(() => ProjectFile.Read(shortened));
    }

    private static void ProjectArchiveLayout()
    {
        using ProjectTestDirectory directory = new(); string source = ProjectFixture(directory);
        foreach (string name in new[] { "project.json", "tracks/000000.f32", "../../outside.txt" })
        {
            string path = directory.File(Guid.NewGuid().ToString("N") + ".msdproj"); RewriteProject(source, path, extraEntry: name);
            ProjectThrows<InvalidDataException>(() => ProjectFile.Read(path));
        }
        Assert(!System.IO.File.Exists(directory.File("outside.txt")), "Archive entries were extracted to disk.");
    }

    private static void ProjectSizeLimits()
    {
        using ProjectTestDirectory directory = new(); string source = ProjectFixture(directory);
        string oversized = directory.File("metadata.msdproj"); RewriteProject(source, oversized, rawMetadata: new string('x', 1024 * 1024 + 1));
        ProjectThrows<InvalidDataException>(() => ProjectFile.Read(oversized));
        string audio = directory.File("large-audio.msdproj"); RewriteProject(source, audio, root => root["tracks"]![0]!["sampleCount"] = (int)(ProjectFile.MaximumAudioBytes / 4 + 2));
        ProjectThrows<InvalidDataException>(() => ProjectFile.Read(audio));
        ProjectDocument document = new() { TrackSequence = ProjectFile.MaximumTracks + 1 };
        for (int i = 0; i <= ProjectFile.MaximumTracks; i++) document.Tracks.Add(new());
        ProjectThrows<InvalidDataException>(() => ProjectFile.Validate(document));
    }

    private static void ProjectFailedOpenPreservesSession() => WithTimeline((form, manager, devices) =>
    {
        using ProjectTestDirectory directory = new(); ProjectCall(form, "SaveProjectPath", directory.File());
        manager.Tracks[0].Rename("Unsaved"); Track source = manager.Tracks[0]; ClickHandler(form, "btnPlay_Click"); devices[0].AdvanceFrames(1234);
        string title = form.Text; string broken = directory.File("bad.msdproj"); System.IO.File.WriteAllText(broken, "broken");
        ProjectPrompt(form, () => throw new InvalidOperationException("Invalid file must fail before prompting."));
        ProjectThrows<InvalidDataException>(() => ProjectCall<bool>(form, "OpenProjectPath", broken));
        Assert(manager.Tracks[0] == source && manager.IsProjectDirty && manager.CanUndo && form.Text == title &&
            manager.State == PlaybackState.Playing && manager.GetPlayheadFrame() == 1234 && devices.Count == 1 && !devices[0].Disposed,
            "Failed open replaced the active session.");
    });

    private static void ProjectCanceledOpen() => WithTimeline((form, manager, devices) =>
    {
        using ProjectTestDirectory directory = new(); string candidate = ProjectFixture(directory); Track source = manager.Tracks[0];
        ClickHandler(form, "btnPlay_Click"); devices[0].AdvanceFrames(1000); ProjectPrompt(form, () => DialogResult.Cancel);
        Assert(!ProjectCall<bool>(form, "OpenProjectPath", candidate) && manager.Tracks[0] == source && manager.GetPlayheadFrame() == 1000 &&
            manager.State == PlaybackState.Playing && !devices[0].Disposed && manager.IsProjectDirty, "Cancel replaced or stopped the session.");
    });

    private static void ProjectDiscardedOpen() => WithTimeline((form, manager, devices) =>
    {
        using ProjectTestDirectory directory = new(); string candidate = ProjectFixture(directory); ClickHandler(form, "btnPlay_Click");
        ProjectPrompt(form, () => DialogResult.No);
        Assert(ProjectCall<bool>(form, "OpenProjectPath", candidate) && manager.Tracks.Count == 3 && manager.Tracks[0].Name == "Rain" &&
            manager.State == PlaybackState.Stopped && manager.GetPlayheadFrame() == 500 && !manager.IsProjectDirty && !manager.CanUndo && !manager.CanRedo && devices[0].Disposed,
            "Open did not restore a stopped, clean session.");
        Assert(Field<ToolStripMenuItem>(form, "loopItem").Checked && !Field<ToolStripMenuItem>(form, "menuFollowPlayhead").Checked &&
            manager.SelectedTracks.Count == 2 && manager.RangeSelection?.StartFrame == 100 && form.Text.Contains("fixture.msdproj") && !form.Text.Contains(" *"),
            "Opened session controls/selection/caption are inconsistent.");
    });

    private static void ProjectSaveThenReopenSameFile() => WithTimeline((form, manager, _) =>
    {
        using ProjectTestDirectory directory = new(); string path = directory.File(); ProjectCall(form, "SaveProjectPath", path);
        manager.Tracks[0].Rename("Latest saved content"); ProjectPrompt(form, () => DialogResult.Yes);
        Assert(ProjectCall<bool>(form, "OpenProjectPath", path) && manager.Tracks[0].Name == "Latest saved content" && !manager.IsProjectDirty,
            "Reopening after Save loaded the stale pre-confirmation snapshot.");
        Assert(ProjectFile.Read(path).Tracks[0].Name == "Latest saved content", "The requested save did not reach disk.");
    });

    private static void ProjectNewChoices() => WithTimeline((form, manager, devices) =>
    {
        Track source = manager.Tracks[0]; ClickHandler(form, "btnPlay_Click"); ProjectPrompt(form, () => DialogResult.Cancel);
        Assert(!ProjectCall<bool>(form, "NewProject") && manager.Tracks[0] == source && manager.State == PlaybackState.Playing, "Cancel New lost the session.");
        ProjectPrompt(form, () => DialogResult.No);
        Assert(ProjectCall<bool>(form, "NewProject") && manager.Tracks.Count == 0 && !manager.IsProjectDirty && !manager.CanUndo &&
            manager.State == PlaybackState.Stopped && devices[0].Disposed && form.Text.Contains("Untitled") && !form.Text.Contains(" *"), "New Project did not reset the document cleanly.");
    });

    private static void ProjectCloseChoices() => WithTimeline((form, manager, _) =>
    {
        using ProjectTestDirectory directory = new(); ProjectPrompt(form, () => DialogResult.Cancel);
        FormClosingEventArgs canceled = new(CloseReason.UserClosing, false); ProjectCall(form, "OnFormClosing", canceled);
        Assert(canceled.Cancel && manager.Tracks.Count == 2, "Close Cancel was ignored.");
        ProjectCall(form, "SaveProjectPath", directory.File()); manager.Tracks[0].Rename("Save on close"); ProjectPrompt(form, () => DialogResult.Yes);
        FormClosingEventArgs saved = new(CloseReason.UserClosing, false); ProjectCall(form, "OnFormClosing", saved);
        Assert(!saved.Cancel && !manager.IsProjectDirty && ProjectFile.Read(directory.File()).Tracks[0].Name == "Save on close", "Close did not complete the requested Save.");
    });

    private static void ProjectExportIsSeparate()
    {
        using ProjectTestDirectory directory = new(); var (manager, tracks) = SelectionProject(1);
        using (manager)
        {
            manager.MarkProjectSaved(); tracks[0].FadeIn(); manager.SaveToFile(directory.File("mix.wav"));
            Assert(manager.IsProjectDirty && manager.CanUndo && System.IO.File.Exists(directory.File("mix.wav")), "Audio export incorrectly saved the project checkpoint.");
        }
    }

    private static void ProjectLiveSave() => WithTimeline((form, manager, devices) =>
    {
        using ProjectTestDirectory directory = new(); Track source = manager.Tracks[0]; source.FadeIn(); manager.Undo();
        manager.SelectAllTracks(); manager.SetRangeSelection(source, 100, 400); manager.LoopPlayback = true;
        Field<ToolStripMenuItem>(form, "menuFollowPlayhead").Checked = false;
        ClickHandler(form, "btnPlay_Click"); devices[0].AdvanceFrames(1600); float[] audio = manager.GetMixedSamples();
        ProjectCall(form, "SaveProjectPath", directory.File());
        Assert(manager.State == PlaybackState.Playing && manager.GetPlayheadFrame() == 1600 && devices.Count == 1 && !devices[0].Disposed &&
            manager.SelectedTracks.Count == 2 && manager.RangeSelection?.EndFrame == 400 && manager.CanRedo && !manager.IsProjectDirty,
            "Save interrupted playback or changed project state/history.");
        ProjectAudioEqual(audio, manager.GetMixedSamples()); ProjectDocument saved = ProjectFile.Read(directory.File());
        Assert(saved.PlayheadFrame == 1600 && saved.LoopPlayback && !saved.FollowPlayhead, "Save lost session settings/cursor.");
    });

    private static void ProjectFileMenus() => WithTimeline((form, manager, _) =>
    {
        foreach (var item in new[] { ("menuNewProject", Keys.Control | Keys.N), ("menuOpenProject", Keys.Control | Keys.O),
            ("menuSaveProject", Keys.Control | Keys.S), ("menuSaveProjectAs", Keys.Control | Keys.Shift | Keys.S),
            ("menuFileOpen", Keys.Control | Keys.I), ("menuFileSave", Keys.Control | Keys.E) })
            Assert(Field<ToolStripMenuItem>(form, item.Item1).ShortcutKeys == item.Item2 && HelpEntries(form).Any(entry => entry.Shortcut == item.Item2),
                "Missing file command/shortcut help: " + item.Item1);
        Assert(Field<ToolStripMenuItem>(form, "menuFileOpen").Text == "Import Audio..." && Field<ToolStripMenuItem>(form, "menuFileSave").Text == "Export Audio...",
            "Project saving and audio import/export remain ambiguous.");
    });

    private static void ProjectSelectionAnchor()
    {
        var (manager, tracks) = SelectionProject(5);
        using (manager)
        {
            manager.SelectTrack(tracks[1]); manager.SelectTrackByClick(tracks[3], true, false); manager.SelectTrackByClick(tracks[3], true, false);
            ProjectDocument document = manager.CaptureDocument(true); using TrackManager reopened = new(null!); reopened.ReplaceWithDocument(document, 400);
            reopened.SelectTrackByClick(reopened.Tracks[4], false, true); Chosen(reopened, reopened.Tracks[3], reopened.Tracks[4]);
        }
    }

    private static void ProjectSaveVolumeGesture() => WithTimeline((form, manager, _) =>
    {
        using ProjectTestDirectory directory = new(); manager.SelectAllTracks(); manager.ClearHistory(); TrackBar slider = Field<TrackBar>(form, "trkSelectedVolume");
        Pointer(slider, "OnMouseDown", MouseButtons.Left, 10); slider.Value = 50; ClickHandler(form, "trkSelectedVolume_Scroll");
        ProjectCall(form, "SaveProjectPath", directory.File());
        Assert(!manager.HasOpenEditGroup && manager.CanUndo && !manager.IsProjectDirty, "Save did not finish the active slider gesture.");
        manager.Undo(); Assert(manager.IsProjectDirty && manager.SelectedTracks.All(track => track.Volume == 1), "Saved gesture Undo is incomplete.");
        manager.Redo(); Assert(!manager.IsProjectDirty && manager.SelectedTracks.All(track => track.Volume == 0.5f), "Saved gesture Redo did not return to the checkpoint.");
    });

    private static void ProjectFloatBits()
    {
        using ProjectTestDirectory directory = new(); using TrackManager manager = new(null!);
        float[] audio = new[] { 0f, BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)), float.Epsilon, -float.Epsilon,
            BitConverter.Int32BitsToSingle(unchecked((int)0x7FC12345)), float.PositiveInfinity, float.NegativeInfinity, -1f };
        manager.AddTrack(new Track(audio, AudioData.SampleRate, AudioData.Channels));
        ProjectFile.Save(directory.File(), manager.CaptureDocument(true));
        using TrackManager reopened = new(null!); reopened.ReplaceWithDocument(ProjectFile.Read(directory.File()), 400);
        ProjectAudioEqual(audio, reopened.Tracks[0].CloneSamples());
    }

    private static void ProjectSaveFailureBlocksReplacement() => WithTimeline((form, manager, devices) =>
    {
        using ProjectTestDirectory directory = new(); string current = directory.File(); string other = ProjectFixture(directory);
        ProjectCall(form, "SaveProjectPath", current); manager.Tracks[0].Rename("Keep my unsaved work");
        Track source = manager.Tracks[0]; ClickHandler(form, "btnPlay_Click"); devices[0].AdvanceFrames(1234);
        int errors = 0;
        typeof(FrmMain).GetField("reportProjectError", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(form, (Action<string, string>)((title, message) => errors++));
        ProjectPrompt(form, () => DialogResult.Yes);
        using (FileStream locked = new(current, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert(!ProjectCall<bool>(form, "OpenProjectPath", other), "Open continued despite a failed requested save.");
            FormClosingEventArgs closing = new(CloseReason.UserClosing, false); ProjectCall(form, "OnFormClosing", closing);
            Assert(closing.Cancel, "Close continued despite a failed requested save.");
        }
        Assert(errors == 2 && manager.Tracks[0] == source && manager.IsProjectDirty && manager.State == PlaybackState.Playing &&
            manager.GetPlayheadFrame() == 1234 && !devices[0].Disposed, "Save failure destroyed or interrupted the current work.");
    });

    private static void ProjectInvalidDocumentPreservesManager()
    {
        var (manager, source, devices) = PlaybackProject();
        using (manager)
        {
            manager.PlayAll(); devices[0].AdvanceFrames(1000); ProjectDocument invalid = manager.CaptureDocument(true);
            invalid.Tracks[0].StartFrame = -1;
            ProjectThrows<InvalidDataException>(() => manager.ReplaceWithDocument(invalid, 400));
            Assert(manager.Tracks[0] == source && manager.State == PlaybackState.Playing && manager.GetPlayheadFrame() == 1000 &&
                devices.Count == 1 && !devices[0].Disposed, "Invalid data modified the live manager before validation.");
        }
    }

    private static void ProjectReplacedResources() => WithTimeline((form, manager, _) =>
    {
        using ProjectTestDirectory directory = new(); string next = ProjectFixture(directory);
        Track[] previous = manager.Tracks.ToArray(); ContextMenuStrip[] menus = previous.Select(track => track.TrackHeader.ContextMenuStrip!).ToArray();
        ProjectPrompt(form, () => DialogResult.No); Assert(ProjectCall<bool>(form, "OpenProjectPath", next), "Replacement did not complete.");
        Assert(previous.All(track => track.CloneSamples().Length == 0 && track.PanDrawSamples.IsDisposed && track.TrackHeader.IsDisposed) && menus.All(menu => menu.IsDisposed),
            "Replaced project controls or audio buffers were retained.");
        using Track standalone = new(Constant(100)); standalone.FadeIn(); standalone.Dispose();
        Assert(standalone.CloneSamples().Length == 0 && !standalone.Undo(), "Disposed standalone history retains audio.");
    });

    private static void ProjectLongFilename()
    {
        using ProjectTestDirectory directory = new(); var (manager, tracks) = SelectionProject(1);
        using (manager)
        {
            tracks[0].Rename("Rain & Wind / 雨声"); string path = directory.File(new string('p', 210) + "-雨.msdproj");
            ProjectFile.Save(path, manager.CaptureDocument(true)); ProjectFile.Save(path, manager.CaptureDocument(false));
            ProjectDocument saved = ProjectFile.Read(path);
            Assert(saved.Tracks[0].Name == tracks[0].Name && !saved.FollowPlayhead && !Directory.EnumerateFiles(directory.Folder, "*.tmp").Any(),
                "Unicode/long filenames broke save replacement or metadata.");
        }
    }

    private static void ProjectLargeFrames()
    {
        using ProjectTestDirectory directory = new(); using TrackManager manager = new(null!); Track source = new(Constant(100));
        long start = 10000000000L + 337; source.MoveToFrame(start); manager.AddTrack(source); manager.SelectTrack(source); manager.SeekToFrame(start + 41);
        manager.SetViewport(start + 0.25, 0.125, 400); ProjectFile.Save(directory.File(), manager.CaptureDocument(true));
        using TrackManager reopened = new(null!); reopened.ReplaceWithDocument(ProjectFile.Read(directory.File()), 400);
        Assert(reopened.Tracks[0].StartFrame == start && reopened.GetPlayheadFrame() == start + 41 && reopened.ViewStartFrame == start + 0.25,
            "Integer timeline positions were rounded or truncated in the project file.");
    }
}
