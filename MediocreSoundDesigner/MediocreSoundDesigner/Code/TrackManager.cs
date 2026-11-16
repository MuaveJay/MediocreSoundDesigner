
using NAudio.Wave;

namespace MediocreSoundDesigner;

public partial class TrackManager : IDisposable
{
    private Track pseudoCombinedTrack;
    private readonly Func<IWavePlayer> playbackFactory;
    private (Track Track, long Revision)[] mixedTracks = Array.Empty<(Track, long)>();
    private double drawTimescale = 1;

    private readonly List<Track> tracks = new();
    private readonly HashSet<Track> soloedTracks = new();

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
            soloedTracks.Remove(track);
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

    public bool IsSoloed(Track track)
    {
        return soloedTracks.Contains(track);
    }

    public void ToggleSolo(Track track)
    {
        if (track is null || !tracks.Contains(track))
            return;

        if (!soloedTracks.Add(track))
            soloedTracks.Remove(track);

        // Force the mixer to rebuild after changing solo settings.
        mixedTracks = Array.Empty<(Track, long)>();
        RedrawAll();
    }

    private bool MixIsCurrent =>
        pseudoCombinedTrack is not null &&
        mixedTracks.Length == Tracks.Count &&
        mixedTracks.All(snapshot =>
            Tracks.Contains(snapshot.Track) &&
            snapshot.Revision == snapshot.Track.MixRevision);

    public float[] GetMixedSamples()
    {
        bool hasSolo = Tracks.Any(track => soloedTracks.Contains(track));

        Track[] enabled = Tracks.Where(track =>
            !track.IsDisabled &&
            (!hasSolo || soloedTracks.Contains(track)) &&
            track.TimelineFrameCount > 0).ToArray();

        long frames = enabled.Length == 0
            ? 0
            : enabled.Max(track => track.EndFrame);

        float[] result = new float[checked((int)(frames * AudioData.Channels))];

        foreach (Track track in enabled)
        {
            // Includes track position, volume, and automation.
            float[] adjusted = track.GetAdjustedSamples();

            for (int i = 0; i < adjusted.Length; i++)
                result[i] += adjusted[i];
        }

        return result;
    }

    public void SaveToFile(string filename)
    {
        // Export without interrupting active playback.
        using Track export = new(
            GetMixedSamples(),
            AudioData.SampleRate,
            AudioData.Channels);

        export.SaveToFile(filename);
    }

    public void Dispose()
    {
        pseudoCombinedTrack?.Dispose();
        DisposeProjectTracks();
    }
}
