namespace MediocreSoundDesigner;

public partial class Track
{
    private bool needsDefaultName = true;
    private string defaultNamePrefix = "Audio";
    public string Name { get; private set; } = "Audio";
    public Color TrackColor { get; private set; } = Color.Empty;

    public static bool IsValidTrackName(string name)
        => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100 && !name.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029');

    internal void SetInitialName(string name, bool numberOnAdd = false)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Audio" : name.Trim()[..Math.Min(100, name.Trim().Length)];
        defaultNamePrefix = Name;
        needsDefaultName = numberOnAdd;
    }

    internal void InitializeIdentity(int number, Color color)
    {
        if (needsDefaultName)
        {
            Name = $"{defaultNamePrefix} {number}";
            needsDefaultName = false;
        }
        if (TrackColor.IsEmpty) TrackColor = color;
        Changed(false, mixChanged: false);
    }

    private static string NameWithSuffix(string name, string suffix)
        => name[..Math.Min(name.Length, 100 - suffix.Length)] + suffix;

    public bool Rename(string name)
    {
        if (!IsValidTrackName(name)) return false;
        name = name.Trim();
        if (Name == name) return false;
        if (NeedsProjectEdit) return HistoryOwner.ExecuteEdit(() => Rename(name));
        SaveState(); Name = name; needsDefaultName = false; Changed(false, mixChanged: false);
        return true;
    }

    public bool SetTrackColor(Color color)
    {
        if (color.IsEmpty || color.A == 0) return false;
        color = Color.FromArgb(color.R, color.G, color.B);
        if (TrackColor.ToArgb() == color.ToArgb()) return false;
        if (NeedsProjectEdit) return HistoryOwner.ExecuteEdit(() => SetTrackColor(color));
        SaveState(); TrackColor = color; Changed(false, mixChanged: false);
        return true;
    }
}

public static class TrackVisuals
{
    public static readonly Color[] Palette = { Color.Cyan, Color.Orange, Color.LimeGreen, Color.Violet,
        Color.Gold, Color.Coral, Color.DeepSkyBlue, Color.LightPink };
    public static Color DefaultColor(int index)
    {
        if (index < Palette.Length) return Palette[index];
        double hue = index * 137.507764 % 360 / 60;
        double x = 0.65 * (1 - Math.Abs(hue % 2 - 1));
        (double r, double g, double b) = (int)hue switch
        {
            0 => (0.65, x, 0.0), 1 => (x, 0.65, 0.0), 2 => (0.0, 0.65, x),
            3 => (0.0, x, 0.65), 4 => (x, 0.0, 0.65), _ => (0.65, 0.0, x)
        };
        return Color.FromArgb((int)((r + 0.3) * 255), (int)((g + 0.3) * 255), (int)((b + 0.3) * 255));
    }
    private static Color Blend(Color a, Color b, double amount) => Color.FromArgb(
        (int)Math.Round(a.R * (1 - amount) + b.R * amount),
        (int)Math.Round(a.G * (1 - amount) + b.G * amount),
        (int)Math.Round(a.B * (1 - amount) + b.B * amount));
    public static Color Background(Track track, bool selected)
        => track.TrackColor.IsEmpty ? (selected ? Settings.Color.TrackBg_Selected : Settings.Color.TrackBg_Unselected)
            : Blend(selected ? Settings.Color.TrackBg_Selected : Settings.Color.TrackBg_Unselected, track.TrackColor, 0.14);
    private static double Light(Color color)
    {
        double Channel(byte value) { double c = value / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
    public static double Contrast(Color a, Color b) => (Math.Max(Light(a), Light(b)) + 0.05) / (Math.Min(Light(a), Light(b)) + 0.05);
    public static Color Readable(Color desired, Color background)
    {
        Color target = Contrast(Color.White, background) >= Contrast(Color.Black, background) ? Color.White : Color.Black;
        for (int i = 0; i <= 10; i++)
        {
            Color candidate = Blend(desired, target, i / 10.0);
            if (Contrast(candidate, background) >= 3) return candidate;
        }
        return target;
    }
    public static Color Waveform(Track track)
    {
        Color desired = track.IsDisabled ? Settings.Color.TrackSamples_Disabled
            : track.TrackColor.IsEmpty ? Settings.Color.TrackSamples_Enabled : track.TrackColor;
        return Readable(desired, track.PanDrawSamples?.BackColor ?? Background(track, false));
    }
}
