using NAudio.Wave;

namespace MediocreSoundDesigner;

public partial class TrackManager
{
    private long playheadFrame;
    private PlaybackState transportState;
    private bool useDevicePosition;
    private bool awaitingPlaybackEnd;

    public long DurationFrames => Tracks.Where(track => !track.IsDisabled && track.TimelineFrameCount > 0)
        .Select(track => track.EndFrame).DefaultIfEmpty(0).Max();

    public void PlayAll()
    {
        if (State == PlaybackState.Playing)
            return;

        playheadFrame = GetPlayheadFrame();
        long duration = DurationFrames;
        if (duration == 0)
        {
            StopAll();
            return;
        }
        // At the end, an explicit Play starts another pass. Natural completion
        // keeps the cursor at the end until Play, Seek, or Stop is requested.
        if (playheadFrame >= duration)
            playheadFrame = 0;

        bool resume = transportState == PlaybackState.Paused && useDevicePosition &&
                      MixIsCurrent && pseudoCombinedTrack.State == PlaybackState.Paused &&
                      pseudoCombinedTrack.GetPlayheadFrame() == playheadFrame;
        awaitingPlaybackEnd = false;
        try
        {
            if (resume)
                pseudoCombinedTrack.Play();
            else
            {
                if (!MixIsCurrent)
                {
                    Track next = new(GetMixedSamples(), AudioData.SampleRate, AudioData.Channels, playbackFactory);
                    pseudoCombinedTrack?.Dispose();
                    pseudoCombinedTrack = next;
                    mixedTracks = Tracks.Select(track => (track, track.Revision)).ToArray();
                }
                pseudoCombinedTrack.PlayFromFrame(playheadFrame);
            }
            useDevicePosition = true;
            transportState = pseudoCombinedTrack.State;
            awaitingPlaybackEnd = transportState == PlaybackState.Playing;
        }
        catch
        {
            pseudoCombinedTrack?.Reset();
            useDevicePosition = false;
            transportState = PlaybackState.Stopped;
            throw;
        }
    }

    public void PauseAll()
    {
        awaitingPlaybackEnd = false;
        if (State != PlaybackState.Playing)
            return;
        pseudoCombinedTrack.Pause();
        playheadFrame = GetPlayheadFrame();
        transportState = PlaybackState.Paused;
    }

    public void StopAll()
    {
        // Cancel completion before releasing the player, so Stop can never loop.
        awaitingPlaybackEnd = false;
        pseudoCombinedTrack?.Reset();
        useDevicePosition = false;
        transportState = PlaybackState.Stopped;
        playheadFrame = 0;
    }

    public void SeekToFrame(long frame)
    {
        PlaybackState previous = State;
        awaitingPlaybackEnd = false;
        pseudoCombinedTrack?.Reset();
        useDevicePosition = false;
        playheadFrame = Math.Clamp(frame, 0, DurationFrames);
        transportState = previous == PlaybackState.Paused ? PlaybackState.Paused : PlaybackState.Stopped;
        if (previous == PlaybackState.Playing && playheadFrame < DurationFrames)
            PlayAll();
    }

    public long GetPlayheadFrame()
    {
        long frame = useDevicePosition ? pseudoCombinedTrack.GetPlayheadFrame() : playheadFrame;
        return Math.Clamp(frame, 0, DurationFrames);
    }

    public int GetPlayheadPos() => (int)Math.Clamp(Math.Floor(TimelineScale.PixelAtFrame(GetPlayheadFrame(), DrawTimescale, ViewStartFrame)), int.MinValue, int.MaxValue);

    /// <summary>Polls completion on the UI thread; explicit Stop and Seek never trigger looping.</summary>
    public void UpdatePlayback()
    {
        if (DurationFrames == 0)
        {
            StopAll();
            return;
        }
        if (!awaitingPlaybackEnd || pseudoCombinedTrack.State != PlaybackState.Stopped)
            return;
        awaitingPlaybackEnd = false;
        long completedFrame = pseudoCombinedTrack.GetPlayheadFrame();
        bool reachedEnd = completedFrame >= pseudoCombinedTrack.TimelineFrameCount;
        playheadFrame = Math.Clamp(completedFrame, 0, DurationFrames);
        useDevicePosition = false;
        transportState = PlaybackState.Stopped;
        if (reachedEnd && LoopPlayback && DurationFrames > 0)
        {
            playheadFrame = 0;
            PlayAll();
        }
    }
}
