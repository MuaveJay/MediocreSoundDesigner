namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private ToolStripMenuItem menuKeyboardShortcuts;

    private void SetupShortcutHelp()
    {
        menuKeyboardShortcuts = new("Keyboard Shortcuts...", null, (sender, e) => ShowKeyboardShortcuts())
        {
            ShortcutKeys = Keys.F1,
            ToolTipText = "Show all keyboard shortcuts and mouse controls."
        };
        menuView.DropDownItems.Add(new ToolStripSeparator());
        menuView.DropDownItems.Add(menuKeyboardShortcuts);
    }

    private KeyboardShortcutsDialog CreateKeyboardShortcutsDialog() => new(ShortcutHelpCatalog.Build(menu));

    private void ShowKeyboardShortcuts()
    {
        FinishVolumeGesture();
        EndRangeDrag();
        EndTimelineDrag();
        CancelTrackReorder();
        using KeyboardShortcutsDialog dialog = CreateKeyboardShortcutsDialog();
        dialog.ShowDialog(this);
    }
}
