using NAudio.Wave;

namespace MediocreSoundDesigner;

public partial class TrackManager : IDisposable
{
    private Track pseudoCombinedTrack;
    private readonly Func<IWavePlayer> playbackFactory;
    private (Track Track, long Revision)[] mixedTracks = Array.Empty<(Track, long)>();
    private double drawTimescale = 1;

    private readonly List<Track> tracks = new();
    public IReadOnlyList<Track> Tracks => tracks;
    public Track SelectedTrack { get; private set; }
    public double DrawTimescale
    {
        get => drawTimescale;
        set => drawTimescale = ZoomScale.Sanitize(value);
    }
    public PlaybackState State => transportState == PlaybackState.Playing
        ? pseudoCombinedTrack?.State ?? PlaybackState.Stopped : transportState;
    public bool LoopPlayback { get; set; }

    public TrackManager(Panel panContainer, Func<IWavePlayer> playbackFactory = null)
    {
        this.playbackFactory = playbackFactory;
    }

    public void AddTrack(Track track)
    {
        ExecuteEdit(() =>
        {
            if (tracks.Contains(track))
                throw new ArgumentException("The track is already in this project.", nameof(track));
            OwnTrack(track);
            tracks.Add(track);
        });
    }

    public void RemTrack(Track track)
    {
        ExecuteEdit(() =>
        {
            tracks.Remove(track);
            ReconcileTrackSelection();
        });
    }

    public void RedrawAll()
    {
        foreach (Track track in Tracks)
            track.PanDrawSamples?.Invalidate();
    }

    public void ApplySettings()
    {
        RefreshSelectionAppearance();
        RedrawAll();
    }

    private bool MixIsCurrent => pseudoCombinedTrack is not null && mixedTracks.Length == Tracks.Count &&
        mixedTracks.Select((snapshot, index) => ReferenceEquals(snapshot.Track, Tracks[index]) &&
                           snapshot.Revision == Tracks[index].Revision).All(current => current);

    public float[] GetMixedSamples()
    {
        Track[] enabled = Tracks.Where(track => !track.IsDisabled && track.TimelineFrameCount > 0).ToArray();
        long frames = enabled.Length == 0 ? 0 : enabled.Max(track => track.EndFrame);
        float[] result = new float[checked((int)(frames * AudioData.Channels))];
        foreach (Track track in enabled)
        {
            float[] normalized = track.GetMixSamples();
            int offset = checked((int)(track.StartFrame * AudioData.Channels));
            for (int sample = 0; sample < normalized.Length; sample++)
                result[offset + sample] += normalized[sample] * track.Volume;
        }
        return result;
    }

    public void SaveToFile(string filename)
    {
        // Export an independent mix without disposing or restarting active playback.
        using Track export = new(GetMixedSamples(), AudioData.SampleRate, AudioData.Channels);
        export.SaveToFile(filename);
    }

    public void Dispose()
    {
        pseudoCombinedTrack?.Dispose();
        DisposeProjectTracks();
    }
}
