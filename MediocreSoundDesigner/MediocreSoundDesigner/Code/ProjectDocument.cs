using System.Text.Json.Serialization;

namespace MediocreSoundDesigner;

public sealed class ProjectDocument
{
    [JsonRequired] public string Format { get; set; } = "MediocreSoundDesigner";
    [JsonRequired] public int Version { get; set; } = 1;
    [JsonRequired] public int SampleRate { get; set; } = AudioData.SampleRate;
    [JsonRequired] public int Channels { get; set; } = AudioData.Channels;
    [JsonRequired] public List<ProjectTrackData> Tracks { get; set; } = new();
    [JsonRequired] public int TrackSequence { get; set; }
    [JsonRequired] public long PlayheadFrame { get; set; }
    [JsonRequired] public double FramesPerPixel { get; set; } = ZoomScale.FromSlider(500);
    [JsonRequired] public double ViewStartFrame { get; set; }
    [JsonRequired] public bool LoopPlayback { get; set; }
    [JsonRequired] public bool FollowPlayhead { get; set; } = true;
    [JsonRequired] public int[] SelectedTracks { get; set; } = Array.Empty<int>();
    [JsonRequired] public int ActiveTrack { get; set; } = -1;
    [JsonRequired] public int SelectionAnchor { get; set; } = -1;
    [JsonRequired] public ProjectRangeData Range { get; set; }
}

public sealed class ProjectTrackData
{
    [JsonRequired] public string Name { get; set; }
    [JsonRequired] public int ColorArgb { get; set; }
    [JsonRequired] public long StartFrame { get; set; }
    [JsonRequired] public float Volume { get; set; }
    [JsonRequired] public bool Muted { get; set; }
    [JsonRequired] public int SampleCount { get; set; }
    [JsonRequired] public string Sha256 { get; set; }
    [JsonIgnore] public float[] Samples { get; set; }
}

public sealed class ProjectRangeData
{
    [JsonRequired] public long StartFrame { get; set; }
    [JsonRequired] public long EndFrame { get; set; }
}
