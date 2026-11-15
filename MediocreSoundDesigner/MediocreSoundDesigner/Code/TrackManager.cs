using NAudio.Wave;

namespace MediocreSoundDesigner;

public class TrackManager : IDisposable
{
    private Track pseudoCombinedTrack;
    private readonly Func<IWavePlayer> playbackFactory;
    private (Track Track, long Revision)[] mixedTracks = Array.Empty<(Track, long)>();
    private int drawTimescale = 1;

    public List<Track> Tracks { get; } = new();
    public Track SelectedTrack { get; private set; }
    public int DrawTimescale
    {
        get => drawTimescale;
        set => drawTimescale = Math.Max(1, value);
    }
    public PlaybackState State => pseudoCombinedTrack?.State ?? PlaybackState.Stopped;
    public bool LoopPlayback { get; set; }

    public TrackManager(Panel panContainer, Func<IWavePlayer> playbackFactory = null)
    {
        this.playbackFactory = playbackFactory;
    }

    public void AddTrack(Track track) => Tracks.Add(track);

    public void RemTrack(Track track)
    {
        Tracks.Remove(track);
        if (SelectedTrack == track)
            SelectTrack(Tracks.FirstOrDefault());
    }

    public void SelectTrack(Track track)
    {
        SelectedTrack = track;
        foreach (Track item in Tracks)
        {
            if (item.PanDrawSamples is not null)
            {
                item.PanDrawSamples.BorderStyle = BorderStyle.None;
                item.PanDrawSamples.BackColor = Settings.Color.TrackBg_Unselected;
            }
        }
        if (track?.PanDrawSamples is not null)
        {
            track.PanDrawSamples.BorderStyle = BorderStyle.Fixed3D;
            track.PanDrawSamples.BackColor = Settings.Color.TrackBg_Selected;
        }
    }

    public void RedrawAll()
    {
        foreach (Track track in Tracks)
            track.PanDrawSamples?.Invalidate();
    }

    public void ApplySettings()
    {
        SelectTrack(SelectedTrack);
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

    public void PlayAll()
    {
        bool resume = State == PlaybackState.Paused;
        if (resume && MixIsCurrent)
        {
            pseudoCombinedTrack.Play();
            return;
        }
        long position = resume ? pseudoCombinedTrack.GetPlayheadFrame() : 0;
        Track next = new(GetMixedSamples(), AudioData.SampleRate, AudioData.Channels, playbackFactory);
        pseudoCombinedTrack?.Dispose();
        pseudoCombinedTrack = next;
        mixedTracks = Tracks.Select(track => (track, track.Revision)).ToArray();
        pseudoCombinedTrack.PlayFromFrame(position);
    }

    public void PauseAll() => pseudoCombinedTrack?.Pause();
    public long GetPlayheadFrame() => pseudoCombinedTrack?.GetPlayheadFrame() ?? 0;
    public int GetPlayheadPos() => (int)Math.Min(int.MaxValue, GetPlayheadFrame() / DrawTimescale);

    public void Dispose()
    {
        pseudoCombinedTrack?.Dispose();
        foreach (Track track in Tracks)
            track.Dispose();
    }
}
