namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private ToolStripMenuItem menuNewProject;
    private ToolStripMenuItem menuOpenProject;
    private ToolStripMenuItem menuSaveProject;
    private ToolStripMenuItem menuSaveProjectAs;
    private ToolStripMenuItem loopItem;
    private string currentProjectPath;
    private long projectSaveGeneration;
    private bool projectFilesReady;
    private Func<DialogResult> promptUnsavedProject;
    private Action<string, string> reportProjectError;

    private void SetupProjectFiles()
    {
        menuNewProject = new("New Project", null, (sender, e) => NewProject()) { ShortcutKeys = Keys.Control | Keys.N };
        menuOpenProject = new("Open Project...", null, (sender, e) => OpenProjectDialog()) { ShortcutKeys = Keys.Control | Keys.O };
        menuSaveProject = new("Save Project", null, (sender, e) => SaveProject(saveAs: false)) { ShortcutKeys = Keys.Control | Keys.S };
        menuSaveProjectAs = new("Save Project As...", null, (sender, e) => SaveProject(saveAs: true)) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.S };
        menuFileOpen.Text = "Import Audio..."; menuFileOpen.ShortcutKeys = Keys.Control | Keys.I;
        menuFileSave.Text = "Export Audio..."; menuFileSave.ShortcutKeys = Keys.Control | Keys.E;
        menuFile.DropDownItems.Clear();
        menuFile.DropDownItems.AddRange(new ToolStripItem[]
        {
            menuNewProject, menuOpenProject, menuSaveProject, menuSaveProjectAs, new ToolStripSeparator(),
            menuFileOpen, menuFileSave, new ToolStripSeparator(), menuFileSettings
        });
        promptUnsavedProject = () => MessageBox.Show(this,
            "Save changes to this project before continuing?\nYes: Save    No: Discard    Cancel: Keep editing",
            "Unsaved Project", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3);
        reportProjectError = (title, message) => MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        projectFilesReady = true;
        UpdateProjectCaption();
    }

    private void UpdateProjectCaption()
    {
        if (!projectFilesReady) return;
        string name = currentProjectPath is null ? "Untitled" : Path.GetFileName(currentProjectPath);
        Text = name + (trackManager.IsProjectDirty ? " *" : "") + " - Mediocre Sound Designer";
    }

    private void FinishProjectGesture()
    {
        FinishVolumeGesture(); EndRangeDrag(); EndTimelineDrag(); CancelTrackReorder();
    }

    private bool ConfirmUnsavedProject()
    {
        FinishProjectGesture();
        if (!trackManager.IsProjectDirty) return true;
        return promptUnsavedProject() switch
        {
            DialogResult.Yes => SaveProject(saveAs: false),
            DialogResult.No => true,
            _ => false
        };
    }

    private bool SaveProject(bool saveAs)
    {
        FinishProjectGesture();
        string path = currentProjectPath;
        if (saveAs || path is null)
        {
            using SaveFileDialog dialog = new()
            {
                Title = "Save Project", Filter = "Sound Designer Project (*.msdproj)|*.msdproj",
                DefaultExt = "msdproj", AddExtension = true, OverwritePrompt = true,
                FileName = path is null ? "Untitled.msdproj" : Path.GetFileName(path),
                InitialDirectory = path is null ? "" : Path.GetDirectoryName(path)
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }
        try { SaveProjectPath(path); return true; }
        catch (Exception error)
        {
            reportProjectError("Save Project", "Unable to save project: " + error.Message);
            return false;
        }
    }

    private void SaveProjectPath(string path)
    {
        FinishProjectGesture();
        ProjectDocument document = trackManager.CaptureDocument(menuFollowPlayhead.Checked);
        ProjectFile.Save(path, document);
        currentProjectPath = Path.GetFullPath(path);
        trackManager.MarkProjectSaved(); projectSaveGeneration++;
        UpdateProjectCaption();
    }

    private void OpenProjectDialog()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Open Project", Filter = "Sound Designer Project (*.msdproj)|*.msdproj|All files (*.*)|*.*",
            DefaultExt = "msdproj", CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { OpenProjectPath(dialog.FileName); }
        catch (Exception error) { reportProjectError("Open Project", "Unable to open project: " + error.Message); }
    }

    private bool OpenProjectPath(string path)
    {
        // Invalid files fail before the unsaved-work prompt or current-session changes.
        ProjectDocument document = ProjectFile.Read(path);
        long generation = projectSaveGeneration;
        if (!ConfirmUnsavedProject()) return false;
        // Saving changes before reopening this same path must reopen the newly
        // saved content, not the older candidate read before confirmation.
        if (generation != projectSaveGeneration && string.Equals(Path.GetFullPath(path), currentProjectPath, StringComparison.OrdinalIgnoreCase))
            document = ProjectFile.Read(path);
        ApplyOpenedProject(document, Path.GetFullPath(path));
        return true;
    }

    private bool NewProject()
    {
        if (!ConfirmUnsavedProject()) return false;
        ApplyOpenedProject(new ProjectDocument(), null);
        return true;
    }

    private void ApplyOpenedProject(ProjectDocument document, string path)
    {
        FinishProjectGesture();
        trackManager.ReplaceWithDocument(document, panTracks.ClientSize.Width);
        copiedTrack?.Dispose(); copiedTrack = null;
        currentProjectPath = path;
        loopItem.Checked = document.LoopPlayback;
        menuFollowPlayhead.Checked = document.FollowPlayhead;
        ResumePlayheadFollowing();
        paintedPlayheadPixel = null;
        // Reapply the requested view after new row/scrollbar geometry is known.
        ApplyViewport(document.ViewStartFrame, document.FramesPerPixel);
        trackManager.MarkProjectSaved(); UpdateProjectCaption();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!e.Cancel && projectFilesReady && !ConfirmUnsavedProject()) e.Cancel = true;
    }
}
