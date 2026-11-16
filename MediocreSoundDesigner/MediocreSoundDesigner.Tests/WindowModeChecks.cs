using MediocreSoundDesigner;
using NAudio.Wave;
using System.Reflection;

internal static partial class Program
{
    private static void RunWindowModeChecks()
    {
        Run("Editor exposes standard maximize and fullscreen controls", WindowModeInterface);
        Run("Repeated fullscreen toggles restore the prior normal window", FullscreenRestore);
        Run("Fullscreen entered from maximized restores maximized and normal bounds", MaximizedFullscreenRestore);
        Run("Window modes preserve playback, selection, audio and viewport", WindowModesPreserveProject);
        Run("Fullscreen and minimum/normal sizes keep essential controls in bounds", WindowModeLayout);
        Run("Window restoration handles removed monitors and smaller work areas", WindowRestoreClamping);
    }

    private static Rectangle WindowBounds(Rectangle desired, Rectangle work) => (Rectangle)typeof(FrmMain).GetMethod("VisibleWindowBounds", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { desired, work })!;

    private static void WindowModeInterface()
    {
        using FrmMain form = new(); TrackManager manager = Field<TrackManager>(form, "trackManager");
        try
        {
            Assert(form.FormBorderStyle == FormBorderStyle.Sizable && form.MaximizeBox && form.MinimizeBox, "Editor still uses a tool window.");
            Assert(Field<ToolStripMenuItem>(form, "menuFullscreen").ShortcutKeys == Keys.F11 &&
                Field<ToolStripMenuItem>(form, "menuView").DropDownItems.Contains(Field<ToolStripMenuItem>(form, "menuFullscreen")), "Window controls are missing.");
            object[] args = { new Message(), Keys.F11 };
            form.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
            Assert(ReorderField<bool>(form, "fullscreen"), "F11 did not enter fullscreen.");
            args = new object[] { new Message(), Keys.Escape };
            form.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
            Assert(!ReorderField<bool>(form, "fullscreen"), "Escape did not leave fullscreen.");
        }
        finally { manager.Dispose(); }
    }

    private static void FullscreenRestore() => WithTimeline((form, manager, _) =>
    {
        Rectangle desired = WindowBounds(new Rectangle(90, 70, 900, 650), Screen.PrimaryScreen!.WorkingArea);
        form.Bounds = desired;
        for (int i = 0; i < 3; i++)
        {
            InvokeTimeline(form, "ToggleFullscreen");
            Assert(ReorderField<bool>(form, "fullscreen") && form.FormBorderStyle == FormBorderStyle.None &&
                Field<ToolStripMenuItem>(form, "menuFullscreen").Checked && !Field<ToolStripMenuItem>(form, "menuMaximize").Enabled,
                "Fullscreen entry/menu state failed.");
            InvokeTimeline(form, "ToggleFullscreen");
            Assert(!ReorderField<bool>(form, "fullscreen") && form.FormBorderStyle == FormBorderStyle.Sizable &&
                form.Bounds == desired && form.WindowState == FormWindowState.Normal, "Fullscreen failed to restore the original window.");
        }
    });

    private static void MaximizedFullscreenRestore() => WithTimeline((form, manager, _) =>
    {
        form.Bounds = WindowBounds(new Rectangle(70, 50, 900, 650), Screen.PrimaryScreen!.WorkingArea);
        Rectangle normal = form.Bounds; form.WindowState = FormWindowState.Maximized;
        InvokeTimeline(form, "ToggleFullscreen"); InvokeTimeline(form, "ToggleFullscreen");
        Assert(form.WindowState == FormWindowState.Maximized && form.RestoreBounds == normal, "Maximized entry lost its original restore bounds/state.");
        form.WindowState = FormWindowState.Normal;
    });

    private static void WindowModesPreserveProject() => WithTimeline((form, manager, devices) =>
    {
        Track track = manager.Tracks[0]; manager.SelectAllTracks(); manager.SetRangeSelection(track, 100, 300);
        manager.SetViewport(0, 10, Field<Panel>(form, "panTracks").ClientSize.Width);
        float[] samples = manager.GetMixedSamples(); manager.ClearHistory(); manager.PlayAll(); devices[0].AdvanceFrames(1000);
        for (int i = 0; i < 2; i++) { InvokeTimeline(form, "ToggleFullscreen"); InvokeTimeline(form, "ToggleFullscreen"); }
        Assert(devices.Count == 1 && !devices[0].Disposed && manager.GetPlayheadFrame() == 1000 && manager.State == PlaybackState.Playing &&
            manager.SelectedTracks.Count == 2 && manager.RangeSelection?.StartFrame == 100 && manager.DrawTimescale == 10 && !manager.CanUndo,
            "Window mode changed the project or playback.");
        Equal(manager.GetMixedSamples(), samples, 0);
    });

    private static void WindowModeLayout() => WithTimeline((form, manager, _) =>
    {
        foreach (Size size in new[] { new Size(650, 500), new Size(1280, 720), new Size(1920, 1080) })
        {
            form.Size = size; InvokeTimeline(form, "UpdateSizes"); CheckWindowControls(form);
        }
        InvokeTimeline(form, "ToggleFullscreen"); CheckWindowControls(form); InvokeTimeline(form, "ToggleFullscreen");
    });

    private static void CheckWindowControls(FrmMain form)
    {
        foreach (string name in new[] { "btnPlay", "btnStop", "trkSelectedVolume", "trkPlaybackPosition", "trkDrawTimescale", "panTracks", "timelineRuler", "scrTimeline" })
        {
            Control control = Field<Control>(form, name);
            Assert(form.ClientRectangle.Contains(control.Bounds), name + " lies outside the client area.");
        }
        Assert(Field<Panel>(form, "panTracks").Bottom < Field<Button>(form, "btnPlay").Top, "Tracks overlap the fixed transport.");
    }

    private static void WindowRestoreClamping()
    {
        Rectangle work = new(-1600, 0, 1600, 900);
        Assert(WindowBounds(new Rectangle(-1500, 50, 1000, 700), work) == new Rectangle(-1500, 50, 1000, 700), "Valid monitor position changed.");
        Rectangle clamped = WindowBounds(new Rectangle(3000, -500, 1900, 1200), work);
        Assert(clamped == work, "Removed-monitor bounds were not placed in the current work area.");
    }
}
