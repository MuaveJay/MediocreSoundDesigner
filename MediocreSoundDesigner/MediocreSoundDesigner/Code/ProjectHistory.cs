namespace MediocreSoundDesigner;

public partial class TrackManager
{
    private sealed record TrackEntry(Track Track, Track.EditState State);
    private sealed record ProjectState(TrackEntry[] Tracks, Track Selected, TimelineSelection Range, Track[] Selection, Track Anchor);
    private sealed record EditEntry(ProjectState Before, ProjectState After);

    private readonly Stack<EditEntry> undoHistory = new();
    private readonly Stack<EditEntry> redoHistory = new();
    private readonly HashSet<Track> ownedTracks = new();
    private readonly Dictionary<Track, (long Revision, Track.EditState State)> capturedTracks = new();
    private int editDepth;
    private int trackSequence;
    private bool restoringHistory;

    public bool CanUndo => undoHistory.Count > 0;
    public bool CanRedo => redoHistory.Count > 0;
    public event EventHandler ProjectChanged;
    internal bool IsEditing => editDepth > 0 || restoringHistory;

    private void OwnTrack(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.HistoryOwner is not null && track.HistoryOwner != this)
            throw new InvalidOperationException("This track already belongs to another project.");
        if (ownedTracks.Add(track))
        {
            int number = ++trackSequence;
            track.InitializeIdentity(number, TrackVisuals.DefaultColor(number - 1));
            track.ClearLocalHistory();
            track.HistoryOwner = this;
        }
    }

    private ProjectState CaptureProject()
    {
        TrackEntry[] entries = new TrackEntry[Tracks.Count];
        for (int i = 0; i < Tracks.Count; i++)
        {
            Track track = Tracks[i];
            if (!capturedTracks.TryGetValue(track, out var captured) || captured.Revision != track.Revision)
            {
                captured = (track.Revision, track.CaptureEditState());
                capturedTracks[track] = captured;
            }
            entries[i] = new(track, captured.State);
        }
        return new(entries, Tracks.Contains(SelectedTrack) ? SelectedTrack : null, ValidateRange(RangeSelection), SelectedTracks.ToArray(), selectionAnchor);
    }

    private static bool SameContent(ProjectState left, ProjectState right)
    {
        if (left.Tracks.Length != right.Tracks.Length)
            return false;
        for (int i = 0; i < left.Tracks.Length; i++)
        {
            TrackEntry before = left.Tracks[i];
            TrackEntry after = right.Tracks[i];
            if (!ReferenceEquals(before.Track, after.Track))
                return false;
            if (ReferenceEquals(before.State, after.State))
                continue;
            if (before.State.Volume != after.State.Volume || before.State.StartFrame != after.State.StartFrame ||
                before.State.IsDisabled != after.State.IsDisabled || before.State.Name != after.State.Name ||
                before.State.TrackColor.ToArgb() != after.State.TrackColor.ToArgb() ||
                !before.State.Samples.AsSpan().SequenceEqual(after.State.Samples) ||
                !before.State.Automation.SequenceEqual(after.State.Automation))
                return false;
        }
        return true;
    }

    private void RestoreProject(ProjectState state)
    {
        restoringHistory = true;
        try
        {
            tracks.Clear();
            foreach (TrackEntry entry in state.Tracks)
            {
                OwnTrack(entry.Track);
                entry.Track.RestoreEditState(entry.State);
                tracks.Add(entry.Track);
                capturedTracks[entry.Track] = (entry.Track.Revision, entry.State);
            }
            ApplyTrackSelection(state.Selection, state.Selected, state.Anchor);
            RangeSelection = ValidateRange(state.Range);
        }
        finally { restoringHistory = false; }
    }

    /// <summary>Groups all changes inside the action into one project-wide Undo step.</summary>
    public bool ExecuteEdit(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsEditing)
        {
            action();
            return true;
        }

        ProjectState before = CaptureProject();
        ProjectState after;
        editDepth++;
        try
        {
            action();
            ReconcileTrackSelection();
            RangeSelection = ValidateRange(RangeSelection);
            after = CaptureProject();
        }
        catch
        {
            RestoreProject(before);
            ProjectChanged?.Invoke(this, EventArgs.Empty);
            throw;
        }
        finally { editDepth--; }

        if (SameContent(before, after))
            return false;
        undoHistory.Push(new(before, after));
        redoHistory.Clear();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public T ExecuteEdit<T>(Func<T> action)
    {
        T result = default;
        ExecuteEdit(() => { result = action(); });
        return result;
    }

    public bool Undo()
    {
        if (IsEditing || !undoHistory.TryPop(out EditEntry entry))
            return false;
        RestoreProject(entry.Before);
        redoHistory.Push(entry);
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (IsEditing || !redoHistory.TryPop(out EditEntry entry))
            return false;
        RestoreProject(entry.After);
        undoHistory.Push(entry);
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public Track PasteTrack(Track copiedTrack)
    {
        if (copiedTrack is null)
            return null;
        return ExecuteEdit(() =>
        {
            Track pasted = copiedTrack.Duplicate();
            AddTrack(pasted);
            SelectTrack(pasted);
            return pasted;
        });
    }

    public bool CanSplitTrackAtFrame(Track track, long projectFrame)
        => track is not null && Tracks.Contains(track) &&
           projectFrame > track.StartFrame && projectFrame < track.EndFrame;

    public bool CanSplitAtPlayhead => CanSplitTrackAtFrame(SelectedTrack, GetPlayheadFrame());

    public Track SplitAtPlayhead()
    {
        long frame = GetPlayheadFrame();
        return SplitTrackAtFrame(SelectedTrack, frame);
    }

    public Track SplitTrack(Track track, double seconds)
    {
        if (track is null || !Tracks.Contains(track) || !double.IsFinite(seconds) ||
            seconds <= 0 || seconds >= track.DurationSeconds)
            return null;
        long localFrame = (long)Math.Round(seconds * AudioData.SampleRate, MidpointRounding.AwayFromZero);
        return SplitTrackAtFrame(track, checked(track.StartFrame + localFrame));
    }

    public Track SplitTrackAtFrame(Track track, long projectFrame)
    {
        if (!CanSplitTrackAtFrame(track, projectFrame))
            return null;
        return ExecuteEdit(() =>
        {
            Track second = track.SplitAtFrame(projectFrame - track.StartFrame);
            if (second is null)
                return null;
            AddTrack(second);
            SelectTrack(second);
            return second;
        });
    }

    public void MoveTrack(Track track, int destinationIndex)
    {
        if (!Tracks.Contains(track))
            throw new ArgumentException("The track is not in this project.", nameof(track));
        if (destinationIndex < 0 || destinationIndex >= Tracks.Count)
            throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        ExecuteEdit(() =>
        {
            tracks.Remove(track);
            tracks.Insert(destinationIndex, track);
        });
    }

    public void ClearHistory()
    {
        if (IsEditing)
            throw new InvalidOperationException("Cannot clear history during an edit.");
        undoHistory.Clear();
        redoHistory.Clear();
        capturedTracks.Clear();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeProjectTracks()
    {
        foreach (Track track in ownedTracks)
        {
            track.HistoryOwner = null;
            track.PanDrawSamples?.Dispose();
            track.TrackHeader?.ContextMenuStrip?.Dispose();
            track.TrackHeader?.Dispose();
            track.Dispose();
        }
        tracks.Clear();
        ownedTracks.Clear();
        RangeSelection = null;
        editGroupBefore = null;
        editDepth = 0;
        selectedTracks.Clear();
        SelectedTrack = null;
        selectionAnchor = null;
        capturedTracks.Clear();
        savedProjectContent = null;
        savedProjectLoop = false;
        undoHistory.Clear();
        redoHistory.Clear();
    }
}

public partial class Track
{
    internal sealed record EditState(float[] Samples, float Volume, long StartFrame, bool IsDisabled, string Name, Color TrackColor,
        List<(long Frame, float Gain)> Automation);
    internal TrackManager HistoryOwner { get; set; }
    private bool NeedsProjectEdit => HistoryOwner is not null && !HistoryOwner.IsEditing;

    internal EditState CaptureEditState() => new(CloneSamples(), Volume, startFrame, IsDisabled, Name, TrackColor,
        automationPoints.ToList());

    internal void RestoreEditState(EditState state)
    {
        bool audioChanged = !samples.AsSpan().SequenceEqual(state.Samples);
        bool automationChanged = !automationPoints.SequenceEqual(state.Automation);
        bool mixChanged = audioChanged || automationChanged || Volume != state.Volume ||
            startFrame != state.StartFrame || IsDisabled != state.IsDisabled;
        samples = (float[])state.Samples.Clone();
        Volume = state.Volume;
        startFrame = state.StartFrame;
        IsDisabled = state.IsDisabled;
        Name = state.Name;
        TrackColor = state.TrackColor;
        automationPoints.Clear();
        automationPoints.AddRange(state.Automation);
        ClearLocalHistory();
        Changed(audioChanged, mixChanged);
    }

    internal void ClearLocalHistory()
    {
        undoHistory.Clear();
        redoHistory.Clear();
    }
}
