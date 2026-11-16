using MediocreSoundDesigner;
using NAudio.Wave;
using System.Reflection;

internal static partial class Program
{
    private static void RunShortcutHelpChecks()
    {
        Run("Shortcut help covers every actual assigned menu hotkey exactly once", ShortcutMenuCoverage);
        Run("Shortcut help includes editor keys, gestures and scope explanations", ShortcutCommandCoverage);
        Run("Shortcut help search matches keys, actions and sections case-insensitively", ShortcutSearch);
        Run("Shortcut popup is read-only, resizable, searchable and dismissible", ShortcutDialogInterface);
        Run("View menu exposes the shortcut popup through F1", ShortcutViewInterface);
        Run("Creating and searching shortcut help preserves project and fake playback", ShortcutProjectState);
        Run("Shortcut help opens from an empty project and lays out at minimum size", ShortcutEmptyLayout);
    }

    private static IReadOnlyList<ShortcutHelpEntry> HelpEntries(FrmMain form) => ShortcutHelpCatalog.Build(Field<MenuStrip>(form, "menu"));
    private static KeyboardShortcutsDialog HelpDialog(FrmMain form) => (KeyboardShortcutsDialog)typeof(FrmMain)
        .GetMethod("CreateKeyboardShortcutsDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null)!;
    private static IEnumerable<ToolStripMenuItem> HelpMenuItems(ToolStripItemCollection items)
    {
        foreach (ToolStripMenuItem item in items.OfType<ToolStripMenuItem>())
        {
            yield return item;
            foreach (var child in HelpMenuItems(item.DropDownItems)) yield return child;
        }
    }

    private static void ShortcutMenuCoverage() => WithTimeline((form, manager, _) =>
    {
        var entries = HelpEntries(form);
        foreach (var menuItem in HelpMenuItems(Field<MenuStrip>(form, "menu").Items).Where(item => item.ShortcutKeys != Keys.None))
            Assert(entries.Count(entry => entry.Shortcut == menuItem.ShortcutKeys) == 1, "Missing or duplicated actual menu shortcut: " + menuItem.Text);
        MenuStrip temporary = new();
        using (temporary)
        {
            ToolStripMenuItem menu = new("Future"); menu.DropDownItems.Add(new ToolStripMenuItem("Future action") { ShortcutKeys = Keys.Control | Keys.B }); temporary.Items.Add(menu);
            Assert(ShortcutHelpCatalog.Build(temporary).Any(entry => entry.Shortcut == (Keys.Control | Keys.B) && entry.Action == "Future action"),
                "New menu shortcuts do not appear automatically.");
        }
    });

    private static void ShortcutCommandCoverage() => WithTimeline((form, manager, _) =>
    {
        var entries = HelpEntries(form);
        foreach (Keys key in new[] { Keys.Space, Keys.Delete, Keys.F2, Keys.Left, Keys.Right, Keys.Escape, Keys.F1, Keys.F11 })
            Assert(entries.Any(entry => entry.Shortcut == key), "Missing editor hotkey: " + key);
        foreach (string text in new[] { "Ctrl + left-click", "Shift + left-click", "Ctrl + Shift + left-click", "Mouse wheel", "Waveform left-drag", "Waveform right-click / right-drag", "Header right-click", "Drag the :: header grip" })
            Assert(entries.Any(entry => entry.Input == text), "Missing mouse gesture: " + text);
        Assert(entries.Any(entry => entry.Input.Contains("Page Up") && entry.Action.Contains("focused")) &&
            entries.Single(entry => entry.Shortcut == Keys.Escape).Action.Contains("fullscreen first"), "Context-sensitive controls are unclear.");
        using KeyboardShortcutsDialog dialog = HelpDialog(form);
        bool scopeFound = Descendants(dialog).OfType<Label>().Any(label => label.Text.Contains("Volume and Delete affect selected tracks") && label.Text.Contains("active track"));
        Assert(scopeFound, "Help lacks active-versus-selected action scope.");
    });

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }

    private static void ShortcutSearch() => WithTimeline((form, manager, _) =>
    {
        var entries = HelpEntries(form);
        Assert(ShortcutHelpCatalog.Filter(entries, "  ").Count == entries.Count, "Whitespace search hid controls.");
        Assert(ShortcutHelpCatalog.Filter(entries, "rEnAmE").Any(entry => entry.Shortcut == Keys.F2), "Action search is case-sensitive or misses rename.");
        Assert(ShortcutHelpCatalog.Filter(entries, "F11").Any(entry => entry.Shortcut == Keys.F11), "Key search failed.");
        Assert(ShortcutHelpCatalog.Filter(entries, "mouse").Count >= 10 && ShortcutHelpCatalog.Filter(entries, "no-such-control-403").Count == 0,
            "Section or unmatched search is incorrect.");
    });

    private static void ShortcutDialogInterface() => WithTimeline((form, manager, _) =>
    {
        using KeyboardShortcutsDialog dialog = HelpDialog(form);
        TextBox search = Field<TextBox>(dialog, "search"); DataGridView grid = Field<DataGridView>(dialog, "shortcutGrid");
        Assert(dialog.FormBorderStyle == FormBorderStyle.Sizable && !dialog.ShowInTaskbar && dialog.CancelButton is Button button &&
            button.DialogResult == DialogResult.Cancel && grid.ReadOnly && !grid.AllowUserToAddRows && grid.Columns.Count == 3,
            "Popup is not resizable/read-only/dismissible.");
        Button close = (Button)dialog.CancelButton!;
        Assert(TrackVisuals.Contrast(close.ForeColor, close.BackColor) >= 3, "Close button text is unreadable.");
        Assert(grid.RowCount == HelpEntries(form).Count && grid.DefaultCellStyle.WrapMode == DataGridViewTriState.True, "Popup lost entries or text wrapping.");
        search.Text = "F11"; Assert(grid.Rows.Cast<DataGridViewRow>().Any(row => row.Cells[1].Value?.ToString() == "F11"), "Search field is not wired.");
        search.Text = "no-such-control-403"; Assert(grid.RowCount == 0 && Field<Label>(dialog, "resultCount").Text.Contains("No matching"), "No-result guidance is missing.");
        search.Text = ""; Assert(grid.RowCount == HelpEntries(form).Count, "Clearing search did not restore controls.");
        Assert(TrackVisuals.Contrast(grid.DefaultCellStyle.ForeColor, grid.DefaultCellStyle.BackColor) >= 3, "Popup text lacks contrast.");
    });

    private static void ShortcutViewInterface() => WithTimeline((form, manager, _) =>
    {
        var item = Field<ToolStripMenuItem>(form, "menuKeyboardShortcuts");
        Assert(item.ShortcutKeys == Keys.F1 && item.Text == "Keyboard Shortcuts..." &&
            Field<ToolStripMenuItem>(form, "menuView").DropDownItems.Contains(item), "View/F1 shortcut entry is missing.");
    });

    private static void ShortcutProjectState() => WithTimeline((form, manager, devices) =>
    {
        Track source = manager.Tracks[0]; source.FadeIn(); manager.Undo(); manager.SelectAllTracks(); manager.SetRangeSelection(source, 100, 400);
        manager.PlayAll(); devices[0].AdvanceFrames(1000); float[] samples = manager.GetMixedSamples();
        using (KeyboardShortcutsDialog dialog = HelpDialog(form))
        {
            Field<TextBox>(dialog, "search").Text = "volume";
            Assert(manager.SelectedTracks.Count == 2 && manager.RangeSelection?.StartFrame == 100 && manager.GetPlayheadFrame() == 1000 &&
                manager.State == PlaybackState.Playing && devices.Count == 1 && !devices[0].Disposed && manager.CanRedo && !manager.CanUndo,
                "Help creation/search changed playback, selection or history.");
        }
        Equal(manager.GetMixedSamples(), samples, 0);
    });

    private static void ShortcutEmptyLayout() => WithTimeline((form, manager, devices) =>
    {
        using KeyboardShortcutsDialog dialog = HelpDialog(form);
        dialog.Size = dialog.MinimumSize; dialog.PerformLayout();
        DataGridView grid = Field<DataGridView>(dialog, "shortcutGrid");
        Assert(grid.Height > 100 && grid.Width >= 500 && Descendants(dialog).OfType<TextBox>().Single().Width > 100 &&
            grid.RowCount > 20 && manager.Tracks.Count == 0 && devices.Count == 0 && !manager.CanUndo, "Empty project help is incomplete or has unusable layout.");
    }, addTracks: false);
}
