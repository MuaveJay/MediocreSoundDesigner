using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediocreSoundDesigner;

public static class ProjectFile
{
    public const string Extension = ".msdproj";
    public const long MaximumAudioBytes = 1024L * 1024 * 1024;
    public const int MaximumTracks = 4096;
    private const int MaximumManifestBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32
    };
    private static string AudioEntry(int index) => $"tracks/{index:D6}.f32";

    public static void Validate(ProjectDocument project, bool requireAudio = true)
    {
        if (project is null || project.Format != "MediocreSoundDesigner" || project.Version != 1)
            throw new InvalidDataException("Unsupported Sound Designer project format or version.");
        if (project.SampleRate != AudioData.SampleRate || project.Channels != AudioData.Channels)
            throw new InvalidDataException("The project must use 44.1 kHz stereo audio.");
        if (project.Tracks is null || project.Tracks.Count > MaximumTracks || project.TrackSequence < project.Tracks.Count || project.TrackSequence == int.MaxValue)
            throw new InvalidDataException("Invalid project track count or sequence.");
        long bytes = 0, duration = 0;
        foreach (ProjectTrackData track in project.Tracks)
        {
            if (track is null || !Track.IsValidTrackName(track.Name) || track.Name != track.Name.Trim() ||
                ((uint)track.ColorArgb >> 24) != 255 || !float.IsFinite(track.Volume) || track.Volume < 0 || track.Volume > 2 ||
                track.SampleCount < 0 || track.SampleCount % AudioData.Channels != 0 || track.StartFrame < 0 ||
                track.StartFrame > long.MaxValue - track.SampleCount / AudioData.Channels)
                throw new InvalidDataException("Invalid track identity, timing, volume or audio length.");
            bytes = checked(bytes + (long)track.SampleCount * sizeof(float));
            if (bytes > MaximumAudioBytes) throw new InvalidDataException("Project audio exceeds the supported 1 GiB decoded-audio limit.");
            if (requireAudio && (track.Samples is null || track.Samples.Length != track.SampleCount))
                throw new InvalidDataException("Track audio does not match its metadata.");
            if (!track.Muted && track.SampleCount > 0) duration = Math.Max(duration, track.StartFrame + track.SampleCount / AudioData.Channels);
        }
        if (project.PlayheadFrame < 0 || project.PlayheadFrame > duration || !double.IsFinite(project.ViewStartFrame) || project.ViewStartFrame < 0 ||
            !double.IsFinite(project.FramesPerPixel) || project.FramesPerPixel < ZoomScale.Minimum || project.FramesPerPixel > ZoomScale.Maximum)
            throw new InvalidDataException("Invalid project playhead or waveform view.");
        bool ValidIndex(int index) => index >= -1 && index < project.Tracks.Count;
        if (project.SelectedTracks is null || project.SelectedTracks.Distinct().Count() != project.SelectedTracks.Length ||
            project.SelectedTracks.Any(index => index < 0 || index >= project.Tracks.Count) || !ValidIndex(project.ActiveTrack) || !ValidIndex(project.SelectionAnchor) ||
            (project.SelectedTracks.Length == 0 ? project.ActiveTrack != -1 : !project.SelectedTracks.Contains(project.ActiveTrack)))
            throw new InvalidDataException("Invalid selected or active track.");
        if (project.Range is not null)
        {
            if (project.ActiveTrack < 0) throw new InvalidDataException("An audio range requires an active track.");
            ProjectTrackData active = project.Tracks[project.ActiveTrack];
            if (project.Range.StartFrame < active.StartFrame || project.Range.EndFrame <= project.Range.StartFrame ||
                project.Range.EndFrame > active.StartFrame + active.SampleCount / AudioData.Channels)
                throw new InvalidDataException("The audio range is outside the active track.");
        }
    }

    public static void Save(string path, ProjectDocument project)
    {
        Validate(project);
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Project audio requires little-endian float storage.");
        foreach (ProjectTrackData track in project.Tracks)
            track.Sha256 = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(track.Samples.AsSpan())));
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(project, JsonOptions);
        if (manifest.Length > MaximumManifestBytes) throw new InvalidDataException("Project metadata is too large.");
        string destination = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(destination);
        // Do not touch the destination until the entire archive is written and flushed.
        string temporary = Path.Combine(directory, ".msd-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream file = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                using (ZipArchive archive = new(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    using (Stream metadata = archive.CreateEntry("project.json", CompressionLevel.Fastest).Open()) metadata.Write(manifest);
                    for (int i = 0; i < project.Tracks.Count; i++)
                    {
                        using Stream audio = archive.CreateEntry(AudioEntry(i), CompressionLevel.Fastest).Open();
                        audio.Write(MemoryMarshal.AsBytes(project.Tracks[i].Samples.AsSpan()));
                    }
                }
                file.Flush(flushToDisk: true);
            }
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static ProjectDocument Read(string path)
    {
        try
        {
            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using ZipArchive archive = new(file, ZipArchiveMode.Read);
            if (archive.Entries.Count > MaximumTracks + 1 || archive.Entries.GroupBy(entry => entry.FullName).Any(group => group.Count() != 1))
                throw new InvalidDataException("The project contains duplicate or excessive archive entries.");
            ZipArchiveEntry metadata = archive.GetEntry("project.json") ?? throw new InvalidDataException("Project metadata is missing.");
            if (metadata.Length <= 0 || metadata.Length > MaximumManifestBytes) throw new InvalidDataException("Project metadata is too large or empty.");
            byte[] bytes = new byte[(int)metadata.Length];
            using (Stream input = metadata.Open())
            {
                input.ReadExactly(bytes);
                if (input.ReadByte() != -1) throw new InvalidDataException("Project metadata exceeds its declared length.");
            }
            using (JsonDocument header = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }))
            {
                if (header.RootElement.ValueKind != JsonValueKind.Object || !header.RootElement.TryGetProperty("version", out var version) ||
                    version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number) || number != 1)
                    throw new InvalidDataException("This project uses an unsupported version.");
            }
            ProjectDocument project = JsonSerializer.Deserialize<ProjectDocument>(bytes, JsonOptions);
            Validate(project, requireAudio: false);
            if (archive.Entries.Count != project.Tracks.Count + 1) throw new InvalidDataException("The project archive has unexpected or missing content.");
            // Validate all declared lengths/checksums before allocating any track audio.
            for (int i = 0; i < project.Tracks.Count; i++)
            {
                ProjectTrackData track = project.Tracks[i];
                ZipArchiveEntry entry = archive.GetEntry(AudioEntry(i)) ?? throw new InvalidDataException("Track audio is missing.");
                if (entry.Length != (long)track.SampleCount * sizeof(float) || track.Sha256 is null || track.Sha256.Length != 64 || !track.Sha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Track audio length or checksum metadata is invalid.");
            }
            for (int i = 0; i < project.Tracks.Count; i++)
            {
                ProjectTrackData track = project.Tracks[i]; track.Samples = new float[track.SampleCount];
                using Stream audio = archive.GetEntry(AudioEntry(i)).Open();
                audio.ReadExactly(MemoryMarshal.AsBytes(track.Samples.AsSpan()));
                if (audio.ReadByte() != -1) throw new InvalidDataException("Track audio exceeds its declared length.");
                string hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(track.Samples.AsSpan())));
                if (!string.Equals(hash, track.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Track audio is damaged (checksum mismatch).");
            }
            return project;
        }
        catch (JsonException error) { throw new InvalidDataException("Invalid project metadata: " + error.Message, error); }
        catch (EndOfStreamException error) { throw new InvalidDataException("The project is truncated.", error); }
    }
}
