namespace MediocreSoundDesigner;

public partial class TrackManager
{
    private ProjectState editGroupBefore;
    public bool HasOpenEditGroup => editGroupBefore is not null;

    public void BeginEditGroup()
    {
        if (IsEditing)
            throw new InvalidOperationException("An edit is already in progress.");
        editGroupBefore = CaptureProject();
        editDepth++;
    }

    public bool EndEditGroup()
    {
        if (editGroupBefore is null)
            return false;
        ProjectState before = editGroupBefore;
        ProjectState after;
        try
        {
            ReconcileTrackSelection();
            RangeSelection = ValidateRange(RangeSelection);
            after = CaptureProject();
        }
        catch
        {
            RestoreProject(before);
            throw;
        }
        finally
        {
            editGroupBefore = null;
            editDepth--;
        }
        if (SameContent(before, after))
            return false;
        undoHistory.Push(new(before, after));
        redoHistory.Clear();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void CancelEditGroup()
    {
        if (editGroupBefore is null)
            return;
        ProjectState before = editGroupBefore;
        editGroupBefore = null;
        editDepth--;
        RestoreProject(before);
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool SetSelectedVolume(float volume)
    {
        if (!float.IsFinite(volume) || volume < 0 || volume > 2)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 0 and 200 percent.");
        Track[] chosen = SelectedTracks.ToArray();
        if (chosen.Length == 0)
            return false;
        return ExecuteEdit(() => { foreach (Track track in chosen) track.ChangeVolume(volume); });
    }

    public void RefreshPlaybackAfterEdit()
    {
        if (State == NAudio.Wave.PlaybackState.Playing && !MixIsCurrent)
            SeekToFrame(GetPlayheadFrame());
    }
}
