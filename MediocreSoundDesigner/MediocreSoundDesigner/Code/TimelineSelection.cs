namespace MediocreSoundDesigner;

public sealed record TimelineSelection(Track Track, long StartFrame, long EndFrame)
{
    public long DurationFrames => EndFrame - StartFrame;
}

public partial class TrackManager
{
    public TimelineSelection RangeSelection { get; private set; }
    public event EventHandler RangeSelectionChanged;

    private TimelineSelection ValidateRange(TimelineSelection range)
    {
        if (range is null || !Tracks.Contains(range.Track) || range.Track != SelectedTrack)
            return null;
        long start = Math.Clamp(range.StartFrame, range.Track.StartFrame, range.Track.EndFrame);
        long end = Math.Clamp(range.EndFrame, range.Track.StartFrame, range.Track.EndFrame);
        return end > start ? new(range.Track, start, end) : null;
    }

    public void SetRangeSelection(Track track, long anchorFrame, long endFrame)
    {
        if (track is null || !Tracks.Contains(track))
        {
            ClearRangeSelection();
            return;
        }
        if (SelectedTrack != track)
            SelectTrack(track);
        TimelineSelection next = ValidateRange(new(track, Math.Min(anchorFrame, endFrame), Math.Max(anchorFrame, endFrame)));
        if (next == RangeSelection)
            return;
        RangeSelection = next;
        RangeSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearRangeSelection()
    {
        if (RangeSelection is null)
            return;
        RangeSelection = null;
        RangeSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool CanTrimSelection => ValidateRange(RangeSelection) is TimelineSelection range &&
        (range.StartFrame != range.Track.StartFrame || range.EndFrame != range.Track.EndFrame);

    public bool TrimToSelection()
    {
        TimelineSelection range = ValidateRange(RangeSelection);
        if (range is null || !CanTrimSelection)
            return false;
        bool trimmed = ExecuteEdit(() => range.Track.TrimFrames(
            range.StartFrame - range.Track.StartFrame, range.EndFrame - range.Track.StartFrame));
        if (trimmed && State == NAudio.Wave.PlaybackState.Playing)
            SeekToFrame(GetPlayheadFrame());
        return trimmed;
    }
}
