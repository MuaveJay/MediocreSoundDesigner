namespace MediocreSoundDesigner;

public partial class TrackManager
{
    private ProjectState savedProjectContent;
    private bool savedProjectLoop;

    public bool IsProjectDirty => savedProjectContent is null
        ? Tracks.Count > 0 || LoopPlayback
        : LoopPlayback != savedProjectLoop || !SameContent(savedProjectContent, CaptureProject());

    public void MarkProjectSaved()
    {
        if (IsEditing) throw new InvalidOperationException("Finish the active edit before saving the project.");
        savedProjectContent = CaptureProject(); savedProjectLoop = LoopPlayback;
    }

    public ProjectDocument CaptureDocument(bool followPlayhead)
    {
        if (IsEditing) throw new InvalidOperationException("Finish the active edit before saving the project.");
        ProjectState snapshot = CaptureProject();
        ProjectDocument document = new()
        {
            TrackSequence = trackSequence, PlayheadFrame = GetPlayheadFrame(),
            FramesPerPixel = DrawTimescale, ViewStartFrame = ViewStartFrame,
            LoopPlayback = LoopPlayback, FollowPlayhead = followPlayhead,
            SelectedTracks = snapshot.Selection.Select(track => tracks.IndexOf(track)).ToArray(),
            ActiveTrack = tracks.IndexOf(SelectedTrack), SelectionAnchor = tracks.IndexOf(selectionAnchor),
            Range = RangeSelection is null ? null : new() { StartFrame = RangeSelection.StartFrame, EndFrame = RangeSelection.EndFrame }
        };
        foreach (TrackEntry entry in snapshot.Tracks)
        {
            Track.EditState state = entry.State;
            document.Tracks.Add(new()
            {
                Name = state.Name, ColorArgb = state.TrackColor.ToArgb(), StartFrame = state.StartFrame,
                Volume = state.Volume, Muted = state.IsDisabled, SampleCount = state.Samples.Length,
                Samples = (float[])state.Samples.Clone(), Sha256 = ""
            });
        }
        return document;
    }

    public void ReplaceWithDocument(ProjectDocument document, int viewportWidth)
    {
        if (IsEditing) throw new InvalidOperationException("Finish the active edit before opening a project.");
        ProjectFile.Validate(document);
        List<Track> incoming = new();
        ProjectState prepared;
        try
        {
            // Complete parsing, validation and track construction before touching
            // the running session. Construction owns an independent audio copy.
            foreach (ProjectTrackData data in document.Tracks) incoming.Add(Track.FromProjectData(data));
            Track activeTrack = document.ActiveTrack < 0 ? null : incoming[document.ActiveTrack];
            Track anchorTrack = document.SelectionAnchor < 0 ? null : incoming[document.SelectionAnchor];
            TimelineSelection range = document.Range is null ? null : new(activeTrack, document.Range.StartFrame, document.Range.EndFrame);
            // Allocate audio checkpoints before releasing the old session too.
            prepared = new(incoming.Select(track => new TrackEntry(track, track.CaptureEditState())).ToArray(), activeTrack, range,
                document.SelectedTracks.Select(index => incoming[index]).ToArray(), anchorTrack);
            tracks.EnsureCapacity(incoming.Count);
            ownedTracks.EnsureCapacity(incoming.Count);
            capturedTracks.EnsureCapacity(incoming.Count);
            selectedTracks.EnsureCapacity(incoming.Count);
        }
        catch
        {
            foreach (Track track in incoming) track.Dispose();
            throw;
        }
        StopAll();
        pseudoCombinedTrack?.Dispose(); pseudoCombinedTrack = null;
        mixedTracks = Array.Empty<(Track, long)>();
        savedProjectContent = null;
        DisposeProjectTracks(); trackSequence = 0;
        for (int i = 0; i < incoming.Count; i++)
        {
            Track track = incoming[i]; OwnTrack(track); tracks.Add(track);
            capturedTracks[track] = (track.Revision, prepared.Tracks[i].State);
        }
        trackSequence = document.TrackSequence;
        LoopPlayback = document.LoopPlayback;
        SetViewport(document.ViewStartFrame, document.FramesPerPixel, viewportWidth);
        playheadFrame = document.PlayheadFrame;
        ApplyTrackSelection(prepared.Selection, prepared.Selected, prepared.Anchor);
        RangeSelection = prepared.Range;
        savedProjectContent = prepared;
        savedProjectLoop = LoopPlayback;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        RangeSelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}

public partial class Track
{
    internal static Track FromProjectData(ProjectTrackData data) => new(data.Samples, AudioData.SampleRate, AudioData.Channels)
    {
        Name = data.Name, needsDefaultName = false, TrackColor = Color.FromArgb(data.ColorArgb),
        startFrame = data.StartFrame, Volume = data.Volume, IsDisabled = data.Muted
    };
}
