namespace MediocreSoundDesigner;

public partial class TrackManager
{
    private readonly HashSet<Track> selectedTracks = new();
    private Track selectionAnchor;
    public IReadOnlyList<Track> SelectedTracks => Tracks.Where(selectedTracks.Contains).ToArray();
    public event EventHandler TrackSelectionChanged;
    public bool IsTrackSelected(Track track) => track is not null && selectedTracks.Contains(track);

    public void SelectTrack(Track track)
    {
        Track valid = track is not null && Tracks.Contains(track) ? track : null;
        ApplyTrackSelection(valid is null ? Array.Empty<Track>() : new[] { valid }, valid, valid);
    }

    public void SelectTrackByClick(Track track, bool toggle, bool extend)
    {
        if (track is null || !Tracks.Contains(track))
            return;
        Track anchor = selectionAnchor is not null && Tracks.Contains(selectionAnchor) ? selectionAnchor : track;
        if (extend)
        {
            int first = Math.Min(tracks.IndexOf(anchor), tracks.IndexOf(track));
            int last = Math.Max(tracks.IndexOf(anchor), tracks.IndexOf(track));
            var range = tracks.Skip(first).Take(last - first + 1);
            ApplyTrackSelection(toggle ? SelectedTracks.Concat(range) : range, track, anchor);
        }
        else if (toggle)
        {
            HashSet<Track> next = new(selectedTracks);
            if (!next.Remove(track))
                next.Add(track);
            Track active = next.Contains(track) ? track : next.Contains(SelectedTrack) ? SelectedTrack : Tracks.FirstOrDefault(next.Contains);
            ApplyTrackSelection(next, active, track);
        }
        else
            SelectTrack(track);
    }

    public void SelectAllTracks()
        => ApplyTrackSelection(Tracks, SelectedTrack ?? Tracks.FirstOrDefault(), selectionAnchor ?? Tracks.FirstOrDefault());

    public void ClearTrackSelection() => ApplyTrackSelection(Array.Empty<Track>(), null, null);

    private void ApplyTrackSelection(IEnumerable<Track> chosen, Track active, Track anchor)
    {
        HashSet<Track> next = new(chosen.Where(track => track is not null && Tracks.Contains(track)));
        if (active is null || !next.Contains(active))
            active = Tracks.FirstOrDefault(next.Contains);
        anchor = anchor is not null && Tracks.Contains(anchor) ? anchor : active;
        bool changed = !next.SetEquals(selectedTracks) || SelectedTrack != active || selectionAnchor != anchor;
        if (SelectedTrack != active)
            ClearRangeSelection();
        selectedTracks.Clear();
        selectedTracks.UnionWith(next);
        SelectedTrack = active;
        selectionAnchor = anchor;
        RefreshSelectionAppearance();
        if (changed)
            TrackSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReconcileTrackSelection()
    {
        Track[] remaining = SelectedTracks.ToArray();
        Track active = remaining.Contains(SelectedTrack) ? SelectedTrack : remaining.FirstOrDefault();
        if (active is null && SelectedTrack is not null)
        {
            active = Tracks.FirstOrDefault();
            remaining = active is null ? Array.Empty<Track>() : new[] { active };
        }
        ApplyTrackSelection(remaining, active, selectionAnchor);
    }

    public void RefreshSelectionAppearance()
    {
        for (int index = 0; index < Tracks.Count; index++)
        {
            Track track = Tracks[index];
            bool selected = IsTrackSelected(track);
            Color background = selected ? Settings.Color.TrackBg_Selected : Settings.Color.TrackBg_Unselected;
            if (track.PanDrawSamples is not null)
            {
                track.PanDrawSamples.BorderStyle = BorderStyle.None;
                track.PanDrawSamples.BackColor = background;
                track.PanDrawSamples.Invalidate();
            }
            if (track.TrackHeader is not null)
            {
                string active = track == SelectedTrack ? " (active)" : "";
                track.TrackHeader.Text = $"Track {index + 1}{active}";
                track.TrackHeader.AccessibleName = track.TrackHeader.Text + (selected ? ", selected" : ", unselected");
                track.TrackHeader.BackColor = background;
                track.TrackHeader.ForeColor = Settings.Color.WinFg;
                track.TrackHeader.Invalidate();
            }
        }
    }

    public void RemoveSelectedTracks()
    {
        Track[] chosen = SelectedTracks.ToArray();
        if (chosen.Length == 0)
            return;
        ExecuteEdit(() => { foreach (Track track in chosen) RemTrack(track); });
    }
}
