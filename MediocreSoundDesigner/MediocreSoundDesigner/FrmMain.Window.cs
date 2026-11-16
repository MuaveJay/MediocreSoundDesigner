namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private ToolStripMenuItem menuView;
    private ToolStripMenuItem menuFullscreen;
    private ToolStripMenuItem menuMaximize;
    private bool fullscreen;
    private Rectangle windowedBounds;
    private FormWindowState windowedState;
    private FormBorderStyle windowedBorder;

    private void SetupWindowModes()
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        menuView = new("View");
        menuFullscreen = new("Fullscreen", null, (sender, e) => ToggleFullscreen()) { ShortcutKeys = Keys.F11 };
        menuMaximize = new("Maximize / Restore", null, (sender, e) =>
        {
            if (!fullscreen) WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        });
        menuView.DropDownItems.AddRange(new ToolStripItem[] { menuMaximize, menuFullscreen });
        menu.Items.Add(menuView);
    }

    private void ToggleFullscreen()
    {
        CancelTrackReorder();
        FinishVolumeGesture();
        EndRangeDrag();
        EndTimelineDrag();
        if (!fullscreen)
        {
            windowedState = WindowState == FormWindowState.Minimized ? FormWindowState.Normal : WindowState;
            windowedBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            windowedBorder = FormBorderStyle;
            Rectangle screenBounds = Screen.FromRectangle(Bounds).Bounds;
            fullscreen = true;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = screenBounds;
        }
        else
        {
            fullscreen = false;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = windowedBorder;
            Bounds = VisibleWindowBounds(windowedBounds, Screen.FromRectangle(windowedBounds).WorkingArea);
            WindowState = windowedState;
        }
        menuFullscreen.Checked = fullscreen;
        menuFullscreen.Text = fullscreen ? "Exit Fullscreen (Esc)" : "Fullscreen";
        menuMaximize.Enabled = !fullscreen;
        UpdateSizes();
    }

    // A removed monitor or reduced work area must not strand the restored window.
    internal static Rectangle VisibleWindowBounds(Rectangle desired, Rectangle workArea)
    {
        int width = Math.Min(desired.Width, workArea.Width);
        int height = Math.Min(desired.Height, workArea.Height);
        return new(Math.Clamp(desired.X, workArea.Left, workArea.Right - width),
            Math.Clamp(desired.Y, workArea.Top, workArea.Bottom - height), width, height);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (trackManager is null) return;
        if (fullscreen) Bounds = Screen.FromRectangle(Bounds).Bounds;
        UpdateSizes();
    }
}
