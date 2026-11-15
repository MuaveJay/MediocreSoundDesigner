using System.Reflection;
using MediocreSoundDesigner;

internal static partial class Program
{
    private static void RunHistoryChecks()
    {
        Run("A split is one Undo step and retains earlier fades", SplitProjectHistory);
        Run("Pasted tracks undo and redo with independent audio", PasteProjectHistory);
        Run("Undo follows edits across tracks and restores selection", CrossTrackHistory);
        Run("Only a new content edit clears Redo", RedoInvalidation);
        Run("Project trim history restores absolute position", ProjectTrimHistory);
        Run("Nested grouped volume edits form one history entry", GroupedVolumeHistory);
        Run("Reordering preserves tracks and is undoable", ReorderHistory);
        Run("Deleting a track can be undone and redone", DeleteHistory);
        Run("A failed transaction rolls back its complete project edit", FailedTransaction);
        Run("Invalid and no-op edits create no Undo entries", NoOpHistory);
        Run("Track Undo and Redo delegate to project history", DirectTrackHistory);
        Run("Undo and Redo synchronize waveform rows and menu state", HistoryUserInterface);
        Run("Volume dialog Apply participates in project history", DialogHistory);
        Run("Standalone splitting preserves prior track history", StandaloneSplitHistory);
        Run("A mixed structural edit chain restores exactly in both directions", MixedHistoryChain);
    }

    private static (TrackManager Manager, Track First, Track Second) HistoryProject()
    {
        TrackManager manager = new(null!);
        Track first = new(Constant(4410, 0.25f));
        Track second = new(Constant(4410, 0.5f));
        manager.ExecuteEdit(() =>
        {
            manager.AddTrack(first);
            manager.AddTrack(second);
            manager.SelectTrack(first);
        });
        manager.ClearHistory();
        return (manager, first, second);
    }

    private static void SplitProjectHistory()
    {
        var (manager, first, _) = HistoryProject();
        using (manager)
        {
            float[] original = first.CloneSamples();
            first.FadeIn();
            float[] faded = first.CloneSamples();
            Track tail = manager.SplitTrack(first, 0.04);
            Assert(manager.Tracks.Count == 3 && manager.SelectedTrack == tail, "Split did not add and select the tail.");
            Assert(manager.Undo() && manager.Tracks.Count == 2, "Undo did not remove the split tail.");
            Equal(first.CloneSamples(), faded);
            Assert(manager.SelectedTrack == first, "Undo did not restore the original selection.");
            Assert(manager.Undo(), "The fade history was lost by splitting.");
            Equal(first.CloneSamples(), original);
            Assert(!manager.CanUndo, "One operation created more than one Undo entry.");
            Assert(manager.Redo() && manager.Redo(), "Split/fade redo failed.");
            Assert(manager.Tracks[2] == tail && manager.SelectedTrack == tail, "Redo did not restore the same split track.");
            Near(tail.StartSeconds, 0.04);
        }
    }

    private static void PasteProjectHistory()
    {
        var (manager, first, _) = HistoryProject();
        using (manager)
        using (Track clipboard = first.Duplicate())
        {
            Track pasted = manager.PasteTrack(clipboard);
            pasted.FadeOut();
            Assert(manager.Undo(), "Pasted fade undo failed.");
            Equal(pasted.CloneSamples(), clipboard.CloneSamples());
            Assert(manager.Undo() && manager.Tracks.Count == 2, "Paste was not undone as a single operation.");
            Assert(manager.SelectedTrack == first && !manager.CanUndo, "Paste undo did not restore selection/history.");
            Assert(manager.Redo() && manager.Tracks[2] == pasted, "Paste redo did not restore its track.");
            Assert(manager.Redo(), "Pasted fade redo failed.");
            Near(pasted.CloneSamples()[^1], 0);
            Near(first.CloneSamples()[^1], 0.25);
            Near(clipboard.CloneSamples()[^1], 0.25);
        }
    }

    private static void CrossTrackHistory()
    {
        var (manager, first, second) = HistoryProject();
        using (manager)
        {
            first.ChangeVolume(0.3f);
            manager.SelectTrack(second);
            second.Flip();
            manager.SelectTrack(first);
            Assert(manager.Undo(), "Second-track edit was not undone.");
            Near(second.CloneSamples()[0], 0.5);
            Assert(manager.SelectedTrack == second, "Undo did not restore the edited track selection.");
            Assert(manager.Undo(), "First-track edit was not undone next.");
            Near(first.Volume, 1);
            Assert(manager.SelectedTrack == first, "First edit selection was not restored.");
            Assert(manager.Redo() && manager.Redo(), "Cross-track redo failed.");
            Near(first.Volume, 0.3); Near(second.CloneSamples()[0], -0.5);
        }
    }

    private static void RedoInvalidation()
    {
        var (manager, first, second) = HistoryProject();
        using (manager)
        {
            first.FadeIn(); manager.Undo();
            manager.SelectTrack(second);
            second.ChangeVolume(second.Volume);
            Assert(manager.SplitTrack(second, 0) is null, "Invalid split accepted.");
            Assert(manager.CanRedo, "Selection/no-op/invalid edits cleared Redo.");
            second.Flip();
            Assert(!manager.CanRedo && !manager.Redo(), "A new edit retained the old Redo branch.");
        }
    }

    private static void ProjectTrimHistory()
    {
        var (manager, first, _) = HistoryProject();
        using (manager)
        {
            first.MoveToFrame(44100); manager.ClearHistory();
            float[] original = first.CloneSamples();
            Assert(first.Trim(0.02, 0.07), "Trim failed.");
            Near(first.StartSeconds, 1.02); Near(first.DurationSeconds, 0.05);
            Assert(manager.Undo(), "Project trim undo failed.");
            Near(first.StartSeconds, 1); Equal(first.CloneSamples(), original);
            Assert(manager.Redo(), "Project trim redo failed.");
            Near(first.StartSeconds, 1.02); Near(first.DurationSeconds, 0.05);
        }
    }

    private static void GroupedVolumeHistory()
    {
        var (manager, first, second) = HistoryProject();
        using (manager)
        {
            first.SetVolume(0.2f); second.SetVolume(0.6f); manager.ClearHistory();
            manager.ExecuteEdit(() =>
            {
                first.ChangeVolume(0.5f);
                manager.ExecuteEdit(() => second.ChangeVolume(0.5f));
            });
            Assert(manager.Undo() && !manager.CanUndo, "Grouped change was not one Undo step.");
            Near(first.Volume, 0.2); Near(second.Volume, 0.6);
            Assert(manager.Redo(), "Grouped redo failed.");
            Near(first.Volume, 0.5); Near(second.Volume, 0.5);
        }
    }

    private static void ReorderHistory()
    {
        var (manager, first, second) = HistoryProject();
        using (manager)
        {
            second.MoveToFrame(1234); manager.SelectTrack(second); manager.ClearHistory();
            float[] before = manager.GetMixedSamples();
            manager.MoveTrack(second, 0);
            Assert(manager.Tracks[0] == second && manager.Tracks[1] == first, "Reorder failed.");
            Equal(manager.GetMixedSamples(), before);
            Assert(manager.Undo() && manager.Tracks[0] == first, "Reorder undo failed.");
            Assert(manager.Redo() && manager.Tracks[0] == second, "Reorder redo failed.");
            Assert(second.StartFrame == 1234 && manager.SelectedTrack == second, "Reorder changed track properties.");
        }
    }

    private static void DeleteHistory()
    {
        var (manager, first, second) = HistoryProject();
        using (manager)
        {
            first.MoveToFrame(500); first.SetVolume(0.4f); first.ToggleDisabled(); manager.ClearHistory();
            float[] original = first.CloneSamples();
            manager.RemTrack(first);
            Assert(manager.Tracks.Count == 1 && manager.SelectedTrack == second, "Delete did not update selection.");
            Assert(manager.Undo() && manager.Tracks[0] == first && manager.SelectedTrack == first, "Deleted track was not restored.");
            Equal(first.CloneSamples(), original);
            Assert(first.StartFrame == 500 && first.IsDisabled, "Deleted track state was lost."); Near(first.Volume, 0.4);
            Assert(manager.Redo() && manager.Tracks.Count == 1, "Delete redo failed.");
        }
    }

    private static void FailedTransaction()
    {
        var (manager, first, _) = HistoryProject();
        using (manager)
        {
            float[] original = first.CloneSamples();
            try
            {
                manager.ExecuteEdit(() =>
                {
                    first.Flip(); manager.AddTrack(new Track(Constant(50)));
                    throw new InvalidOperationException("Expected test failure");
                });
                throw new Exception("The expected failure did not propagate.");
            }
            catch (InvalidOperationException error) when (error.Message == "Expected test failure") { }
            Assert(manager.Tracks.Count == 2 && manager.SelectedTrack == first && !manager.CanUndo,
                   "Failed transaction left partial project changes or history.");
            Equal(first.CloneSamples(), original);
        }
    }

    private static void NoOpHistory()
    {
        var (manager, first, _) = HistoryProject();
        using (manager)
        {
            first.SetVolume(first.Volume);
            Assert(!first.Trim(0, first.DurationSeconds), "Whole-track no-op trim changed content.");
            Assert(manager.SplitTrack(first, double.NaN) is null, "NaN split accepted.");
            manager.MoveTrack(first, 0);
            Assert(!manager.CanUndo && !manager.CanRedo, "No-op commands created history.");
        }
    }

    private static void DirectTrackHistory()
    {
        var (manager, first, _) = HistoryProject();
        using (manager)
        {
            first.Flip();
            Assert(manager.CanUndo && first.Undo(), "Direct track edit bypassed project history.");
            Near(first.CloneSamples()[0], 0.25);
            Assert(first.Redo(), "Track Redo did not delegate to project history."); Near(first.CloneSamples()[0], -0.25);
        }
    }

    private static T Field<T>(object target, string name) where T : class
        => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void ClickHandler(object target, string name)
        => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, new object[] { target, EventArgs.Empty });

    private static void HistoryUserInterface()
    {
        using FrmMain form = new(); // Never shown: this is a control-tree integration check.
        TrackManager manager = Field<TrackManager>(form, "trackManager");
        Panel panel = Field<Panel>(form, "panTracks");
        ToolStripMenuItem undo = Field<ToolStripMenuItem>(form, "menuEditUndo");
        ToolStripMenuItem redo = Field<ToolStripMenuItem>(form, "menuEditRedo");
        try
        {
            Track first = new(Constant(4410));
            manager.ExecuteEdit(() => { manager.AddTrack(first); manager.SelectTrack(first); });
            Track pasted = manager.PasteTrack(first);
            Panel pastedPanel = pasted.PanDrawSamples;
            Assert(panel.Controls.OfType<Panel>().Count() == 2 && undo.Enabled, "Paste did not update visible rows/history menu.");
            ClickHandler(form, "menuEditUndo_Click");
            Assert(panel.Controls.OfType<Panel>().Count() == 1 && pastedPanel.Parent is null && !pastedPanel.IsDisposed,
                   "Undo left a ghost waveform or disposed a track needed by Redo.");
            Assert(redo.Enabled && manager.SelectedTrack == first, "Undo menu/selection state is incorrect.");
            ClickHandler(form, "menuEditRedo_Click");
            Assert(pastedPanel.Parent == panel && manager.SelectedTrack == pasted && !redo.Enabled, "Redo did not restore the waveform row.");
            Track split = manager.SplitTrack(pasted, 0.04);
            Assert(panel.Controls.OfType<Panel>().Count() == 3, "Split row was not shown.");
            ClickHandler(form, "menuEditUndo_Click");
            Assert(panel.Controls.OfType<Panel>().Count() == 2 && split.PanDrawSamples.Parent is null,
                   "Undo did not remove the split waveform row.");
            manager.RemTrack(first); manager.Undo();
            Assert(first.PanDrawSamples.Parent == panel && !first.PanDrawSamples.IsDisposed, "Delete undo could not restore its row.");
        }
        finally { manager.Dispose(); }
    }

    private static void DialogHistory()
    {
        var (manager, first, _) = HistoryProject();
        using (manager)
        using (FrmEditVolume dialog = new())
        {
            dialog.SetSound(first);
            dialog.EditExecutor = action => { manager.ExecuteEdit(action); };
            Field<NumericUpDown>(dialog, "nudVolume").Value = 50;
            dialog.btnApply_Click(dialog, EventArgs.Empty);
            Near(first.Volume, 0.5);
            Assert(manager.Undo() && !manager.CanUndo, "Volume dialog Apply was not one project transaction.");
            Near(first.Volume, 1);
        }
    }

    private static void StandaloneSplitHistory()
    {
        using Track first = new(Constant(4410));
        first.FadeIn(); float[] faded = first.CloneSamples();
        using Track second = first.Split(0.04);
        Assert(first.Undo(), "Standalone split has no history."); Equal(first.CloneSamples(), faded);
        Assert(first.Undo(), "Standalone split erased its earlier fade history.");
        Equal(first.CloneSamples(), Constant(4410, 0.25f, 2));
    }

    private sealed record ExpectedProject(Track[] Tracks, float[][] Samples, float[] Volumes,
                                          long[] Starts, bool[] Disabled, Track? Selected);

    private static ExpectedProject Expected(TrackManager manager) => new(manager.Tracks.ToArray(),
        manager.Tracks.Select(t => t.CloneSamples()).ToArray(), manager.Tracks.Select(t => t.Volume).ToArray(),
        manager.Tracks.Select(t => t.StartFrame).ToArray(), manager.Tracks.Select(t => t.IsDisabled).ToArray(), manager.SelectedTrack);

    private static void Match(TrackManager manager, ExpectedProject state)
    {
        Assert(manager.Tracks.SequenceEqual(state.Tracks) && manager.SelectedTrack == state.Selected, "Track list or selection differs.");
        for (int i = 0; i < state.Tracks.Length; i++)
        {
            Track track = manager.Tracks[i]; Equal(track.CloneSamples(), state.Samples[i], 0);
            Near(track.Volume, state.Volumes[i]);
            Assert(track.StartFrame == state.Starts[i] && track.IsDisabled == state.Disabled[i], "Timeline/mute state differs.");
        }
    }

    private static void MixedHistoryChain()
    {
        var (manager, first, second) = HistoryProject();
        using (manager)
        {
            List<ExpectedProject> states = new() { Expected(manager) };
            first.FadeIn(); states.Add(Expected(manager));
            second.ChangeVolume(0.3f); states.Add(Expected(manager));
            first.Trim(0.01, 0.08); states.Add(Expected(manager));
            Track pasted = manager.PasteTrack(second); states.Add(Expected(manager));
            Track tail = manager.SplitTrack(pasted, 0.04); states.Add(Expected(manager));
            manager.MoveTrack(tail, 0); states.Add(Expected(manager));
            manager.RemTrack(second); states.Add(Expected(manager));
            first.ToggleDisabled(); states.Add(Expected(manager));
            for (int i = states.Count - 2; i >= 0; i--)
            {
                Assert(manager.Undo(), "History chain ended early while undoing."); Match(manager, states[i]);
            }
            Assert(!manager.CanUndo, "History chain created extra Undo entries.");
            for (int i = 1; i < states.Count; i++)
            {
                Assert(manager.Redo(), "History chain ended early while redoing."); Match(manager, states[i]);
            }
            Assert(!manager.CanRedo, "History chain created extra Redo entries.");
        }
    }
}
