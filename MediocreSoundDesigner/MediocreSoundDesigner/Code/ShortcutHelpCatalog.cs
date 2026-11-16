namespace MediocreSoundDesigner;

public sealed record ShortcutHelpEntry(string Section, string Input, string Action, Keys Shortcut = Keys.None);

public static class ShortcutHelpCatalog
{
    public static IReadOnlyList<ShortcutHelpEntry> Build(MenuStrip menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        List<ShortcutHelpEntry> entries = new();
        void ReadMenus(ToolStripItemCollection items, string section)
        {
            foreach (ToolStripItem item in items)
            {
                if (item is not ToolStripMenuItem command) continue;
                string title = command.Text.Replace("&", "");
                if (command.ShortcutKeys != Keys.None)
                    entries.Add(new(section, new KeysConverter().ConvertToInvariantString(command.ShortcutKeys),
                        string.IsNullOrWhiteSpace(command.ToolTipText) ? title : command.ToolTipText, command.ShortcutKeys));
                ReadMenus(command.DropDownItems, section.Length == 0 ? title : section);
            }
        }
        // Read actual assignments so newly added menu hotkeys appear automatically.
        ReadMenus(menu.Items, "");
        entries.AddRange(new ShortcutHelpEntry[]
        {
            new("Playback", "Space", "Play or pause at the current playhead; resume from the paused position.", Keys.Space),
            new("Editing", "Delete", "Delete selected tracks after confirmation; Undo restores them.", Keys.Delete),
            new("Editing", "F2", "Rename the active track.", Keys.F2),
            new("Editing", "Left arrow", "Move the active track earlier by five waveform pixels; when a slider is focused, adjust that slider instead.", Keys.Left),
            new("Editing", "Right arrow", "Move the active track later by five waveform pixels; when a slider is focused, adjust that slider instead.", Keys.Right),
            new("Window / cancel", "Esc", "Exit fullscreen first; otherwise cancel a reorder or volume drag, or clear the highlighted waveform range.", Keys.Escape),
            new("Focused sliders", "Arrows / Page Up / Page Down / Home / End", "Adjust the focused zoom, playback-position or selected-volume slider using its normal keyboard controls."),
            new("Mouse", "Ctrl + left-click", "Toggle a track in the selection from its header, waveform or :: grip."),
            new("Mouse", "Shift + left-click", "Select a contiguous range of track rows from the selection anchor."),
            new("Mouse", "Ctrl + Shift + left-click", "Add a range of track rows to the current selection."),
            new("Mouse", "Header left-click", "Select one track and make it active."),
            new("Mouse", "Waveform left-drag", "Highlight a range on the active track; Trim keeps that audio at its original timeline position."),
            new("Mouse", "Waveform right-click / right-drag", "Move the playhead; a selected group is preserved. Seeking during playback continues from the new position."),
            new("Mouse", "Ruler click / drag", "Move the shared playhead on the timestamp ruler."),
            new("Mouse", "Mouse wheel", "Zoom over a waveform, the timestamp ruler or the top zoom slider."),
            new("Mouse", "Drag the :: header grip", "Reorder a track vertically. The gold marker shows its drop position; dragging near an edge scrolls the rows."),
            new("Mouse", "Header double-click", "Rename the track. Ctrl/Shift-clicks keep selecting instead of opening a dialog."),
            new("Mouse", "Header right-click", "Open Rename and Change Color controls for that track."),
            new("Mouse", "Waveform double-click", "Toggle mute on that track. Ctrl/Shift-clicks keep selecting instead of muting.")
        });
        return entries.OrderBy(entry => entry.Section).ThenBy(entry => entry.Input).ToArray();
    }

    public static IReadOnlyList<ShortcutHelpEntry> Filter(IEnumerable<ShortcutHelpEntry> entries, string query)
    {
        string text = query?.Trim() ?? "";
        return entries.Where(entry => text.Length == 0 || entry.Section.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            entry.Input.Contains(text, StringComparison.OrdinalIgnoreCase) || entry.Action.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
