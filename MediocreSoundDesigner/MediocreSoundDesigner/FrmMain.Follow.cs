using System.Diagnostics;
using NAudio.Wave;

namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private ToolStripMenuItem menuFollowPlayhead;
    private bool followTracking;
    private bool followSuspended;
    private bool followSeekPending;
    private long? previousFollowFrame;
    private PlaybackState previousFollowState;
    private long followTimestamp;

    private void SetupPlayheadFollowing()
    {
        menuFollowPlayhead = new("Follow Playhead")
        {
            Name = "menuFollowPlayhead", CheckOnClick = true, Checked = true,
            ToolTipText = "Smoothly follow playback. Manual panning pauses following until Play/resume, a seek, or re-enabling this option."
        };
        menuFollowPlayhead.CheckedChanged += (sender, e) =>
        {
            if (menuFollowPlayhead.Checked) ResumePlayheadFollowing(seek: true);
            else
            {
                PausePlayheadFollowing();
                followSuspended = false;
                menuFollowPlayhead.Text = "Follow Playhead";
            }
        };
        menuView.DropDownItems.Add(menuFollowPlayhead);
    }

    private void ResumePlayheadFollowing(bool seek = false)
    {
        followSuspended = false;
        followTracking = false;
        followSeekPending = seek;
        previousFollowFrame = null;
        followTimestamp = 0;
        if (menuFollowPlayhead is not null) menuFollowPlayhead.Text = "Follow Playhead";
    }

    private void PausePlayheadFollowing()
    {
        followTracking = false;
        followSeekPending = false;
        previousFollowState = trackManager.State;
        previousFollowFrame = trackManager.GetPlayheadFrame();
        followTimestamp = 0;
    }

    private void SuspendPlayheadFollowing()
    {
        PausePlayheadFollowing();
        followSuspended = true;
        if (menuFollowPlayhead?.Checked == true) menuFollowPlayhead.Text = "Follow Playhead (paused)";
    }

    private void ResetFollowingAfterZoom()
    {
        followTracking = false;
        previousFollowFrame = null;
        followTimestamp = 0;
    }

    private void StopPlayheadFollowing()
    {
        ResumePlayheadFollowing();
        previousFollowState = PlaybackState.Stopped;
        if (menuFollowPlayhead?.Checked == true) MoveFollowingViewport(0);
    }

    private void TickPlayheadFollowing()
    {
        long now = Stopwatch.GetTimestamp();
        double seconds = followTimestamp == 0 ? tmrUpdate.Interval / 1000.0 : (now - followTimestamp) / (double)Stopwatch.Frequency;
        followTimestamp = now;
        AdvancePlayheadFollowing(seconds);
    }

    private void AdvancePlayheadFollowing(double seconds)
    {
        long frame = trackManager.GetPlayheadFrame();
        PlaybackState state = trackManager.State;
        if (menuFollowPlayhead?.Checked != true || followSuspended || panTracks.ClientSize.Width <= 0 || trackManager.DurationFrames == 0)
        {
            previousFollowFrame = frame; previousFollowState = state;
            return;
        }
        if (timelineDragSource is not null || rangeDragSource is not null || reorderSource is not null || trkDrawTimescale.Capture)
        {
            previousFollowFrame = frame; previousFollowState = state;
            return;
        }
        int width = panTracks.ClientSize.Width;
        double scale = trackManager.DrawTimescale;
        double pixel = TimelineScale.PixelAtFrame(frame, scale, trackManager.ViewStartFrame);
        // Loop wraps are discontinuous in audio time. Return to the beginning,
        // then restart the same gentle tracking rather than sweeping backwards.
        if (state == PlaybackState.Playing && previousFollowState == PlaybackState.Playing &&
            previousFollowFrame is long previous && frame < previous && !followSeekPending)
        {
            MoveFollowingViewport(Math.Max(0, frame - width * scale * PlayheadFollow.Anchor));
            followTracking = false;
        }
        else if (state == PlaybackState.Playing || (state == PlaybackState.Stopped && previousFollowState == PlaybackState.Playing))
        {
            if (followTracking || pixel >= width * PlayheadFollow.FollowThreshold || pixel < 0)
            {
                followTracking = true;
                MoveFollowingViewport(PlayheadFollow.NextStart(trackManager.ViewStartFrame, frame, scale, width,
                    trackManager.TimelineExtentFrames, seconds));
            }
            followSeekPending = false;
        }
        else if (followSeekPending)
        {
            // A deliberate paused/stopped seek is a jump in time. Reframe only
            // when off-screen, preserving the location of ordinary ruler clicks.
            if (pixel < 0 || pixel >= width)
                MoveFollowingViewport(frame - width * scale * PlayheadFollow.Anchor);
            followSeekPending = false;
        }
        previousFollowFrame = frame;
        previousFollowState = state;
    }

    private void MoveFollowingViewport(double start)
    {
        double previous = trackManager.ViewStartFrame;
        trackManager.SetViewport(start, trackManager.DrawTimescale, panTracks.ClientSize.Width);
        if (trackManager.ViewStartFrame != previous) trackManager.RedrawAll();
    }
}
