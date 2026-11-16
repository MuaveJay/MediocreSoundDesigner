namespace MediocreSoundDesigner;

public partial class FrmMain
{
    private readonly Button btnImportAudio = new();
    private readonly Button btnSplitToolbar = new();
    private readonly Button btnTrimToolbar = new();
    private readonly Button btnUndoToolbar = new();
    private readonly Button btnRedoToolbar = new();
    private readonly Label lblZoomTitle = new();
    private readonly Label lblZoomDetail = new();
    private readonly Label lblTrackCount = new();
    private readonly Label lblEditorHint = new();
    private readonly Label lblEmptyProject = new();
    private bool editorLayoutReady;
    private int bottomBarTop;
    private int Ui(int value) => (int)Math.Ceiling(value * DeviceDpi / 96.0);

    private void SetupEditorLayout()
    {
        Font = new Font("Segoe UI", 9f);
        MinimumSize = new Size(Ui(650), Ui(500));
        ConfigureToolbarButton(btnImportAudio, "Import Audio...", "Import audio as a new track (Ctrl+I)", (sender, e) => menuFileOpen_Click(sender, e));
        ConfigureToolbarButton(btnSplitToolbar, "Split", "Split the active track at the playhead (Ctrl+K)", (sender, e) => menuEditSplit.PerformClick());
        ConfigureToolbarButton(btnTrimToolbar, "Trim", "Keep the selected audio range (Ctrl+T)", (sender, e) => menuEditTrim.PerformClick());
        ConfigureToolbarButton(btnUndoToolbar, "Undo", "Undo the last edit (Ctrl+Z)", (sender, e) => menuEditUndo.PerformClick());
        ConfigureToolbarButton(btnRedoToolbar, "Redo", "Redo the last edit (Ctrl+Y)", (sender, e) => menuEditRedo.PerformClick());
        lblZoomTitle.Text = "Zoom";
        lblZoomTitle.TextAlign = ContentAlignment.MiddleLeft;
        lblZoomDetail.TextAlign = ContentAlignment.MiddleRight;
        lblZoomDetail.AccessibleName = "Waveform zoom detail";
        lblTrackCount.TextAlign = ContentAlignment.MiddleRight;
        lblTrackCount.AutoEllipsis = true;
        lblEditorHint.AutoEllipsis = true;
        lblEmptyProject.Name = "lblEmptyProject";
        lblEmptyProject.Text = "Start with a sound\nImport Audio or use the Generate menu to create a track.";
        lblEmptyProject.TextAlign = ContentAlignment.MiddleCenter;
        lblEmptyProject.AutoEllipsis = true;
        panTracks.Controls.Add(lblEmptyProject);
        Controls.AddRange(new Control[] { lblZoomTitle, lblZoomDetail, lblTrackCount, lblEditorHint });
        btnPlay.AutoSize = false;
        btnPlay.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        lblPlaybackTime.AutoSize = false;
        lblPlaybackTime.TextAlign = ContentAlignment.MiddleLeft;
        lblPlaybackTime.AutoEllipsis = true;
        lblPlaybackTime.Font = new Font("Consolas", 10f);
        trkDrawTimescale.AutoSize = false;
        trkPlaybackPosition.AutoSize = false;
        trkSelectedVolume.AutoSize = false;
        ToolTip help = new(components);
        help.SetToolTip(trkDrawTimescale, "Zoom smoothly. Mouse wheel also zooms on the ruler and waveforms; Fit shows the whole project.");
        help.SetToolTip(btnPlay, "Play or pause at the current position (Space)");
        help.SetToolTip(btnStop, "Stop and return to the beginning");
        help.SetToolTip(lblSelectionRange, "Drag a waveform range to keep. Ctrl+T trims it at its original timeline position.");
        editorLayoutReady = true;
    }

    private void ConfigureToolbarButton(Button button, string text, string helpText, EventHandler click)
    {
        button.Text = text;
        button.AccessibleName = helpText;
        button.Click += click;
        Controls.Add(button);
        ToolTip help = new(components);
        help.SetToolTip(button, helpText);
    }

    private static Color Surface(Color color, int amount) => Color.FromArgb(
        Math.Min(255, color.R + amount), Math.Min(255, color.G + amount), Math.Min(255, color.B + amount));

    private void ApplyEditorTheme()
    {
        Color surface = Surface(Settings.Color.WinBg, 14);
        Color foreground = TrackVisuals.Readable(Settings.Color.WinFg, surface);
        menu.BackColor = surface;
        menu.ForeColor = foreground;
        void ThemeMenu(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.ForeColor = foreground;
                if (item is ToolStripMenuItem submenu)
                {
                    submenu.DropDown.BackColor = surface;
                    ThemeMenu(submenu.DropDownItems);
                }
            }
        }
        ThemeMenu(menu.Items);
        menu.Renderer = new ToolStripProfessionalRenderer(new EditorMenuColors(surface));
        foreach (Button button in new[] { btnImportAudio, btnSplitToolbar, btnTrimToolbar, btnUndoToolbar, btnRedoToolbar, btnPlay, btnStop, btnFitWaveform })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.UseVisualStyleBackColor = false;
            button.BackColor = surface;
            button.ForeColor = foreground;
            button.FlatAppearance.BorderColor = Surface(Settings.Color.WinBg, 65);
            button.FlatAppearance.MouseOverBackColor = Surface(Settings.Color.WinBg, 38);
            button.FlatAppearance.MouseDownBackColor = Surface(Settings.Color.WinBg, 58);
            button.Cursor = Cursors.Hand;
        }
        foreach (Label label in new[] { lblZoomTitle, lblZoomDetail, lblTrackCount, lblEditorHint })
        {
            label.BackColor = surface;
            label.ForeColor = foreground;
        }
        lblPlaybackTime.BackColor = surface;
        lblSelectedVolume.BackColor = surface;
        lblPlaybackTime.ForeColor = foreground;
        lblSelectedVolume.ForeColor = foreground;
        trkDrawTimescale.BackColor = surface;
        trkPlaybackPosition.BackColor = surface;
        trkSelectedVolume.BackColor = surface;
        lblEmptyProject.BackColor = panTracks.BackColor;
        lblEmptyProject.ForeColor = TrackVisuals.Readable(Settings.Color.WinFg, panTracks.BackColor);
        Invalidate();
    }

    private void LayoutEditor()
    {
        if (!editorLayoutReady) return;
        SuspendLayout();
        try
        {
            int pad = Ui(12), gap = Ui(8), row = Math.Max(Ui(30), Font.Height + Ui(12));
            int toolbarY = menu.Bottom + gap, x = pad;
            foreach (var item in new[] { (btnImportAudio, 108), (btnSplitToolbar, 60), (btnTrimToolbar, 60), (btnUndoToolbar, 58), (btnRedoToolbar, 58) })
            {
                item.Item1.SetBounds(x, toolbarY, Ui(item.Item2), row);
                x = item.Item1.Right + gap;
            }
            lblTrackCount.SetBounds(x, toolbarY, Math.Max(0, ClientSize.Width - x - pad), row);
            int zoomY = toolbarY + row + gap;
            lblZoomTitle.SetBounds(pad, zoomY, Ui(46), row);
            btnFitWaveform.SetBounds(ClientSize.Width - pad - Ui(52), zoomY, Ui(52), row);
            lblZoomDetail.SetBounds(btnFitWaveform.Left - gap - Ui(116), zoomY, Ui(116), row);
            trkDrawTimescale.SetBounds(lblZoomTitle.Right, zoomY, Math.Max(Ui(100), lblZoomDetail.Left - lblZoomTitle.Right - gap), row);
            bottomBarTop = ClientSize.Height - Math.Max(Ui(96), Font.Height * 2 + Ui(66));
            panTracks.Left = pad;
            panTracks.Width = Math.Max(Ui(100), ClientSize.Width - pad * 2);
            LayoutTimelineRuler();
            LayoutRangeReadout();
            panTracks.Top = lblSelectionRange.Bottom + Ui(6);
            panTracks.Height = Math.Max(Ui(80), bottomBarTop - panTracks.Top - SystemInformation.HorizontalScrollBarHeight - Ui(12));
            LayoutTimelineTracks();
            LayoutWaveformNavigation();
            LayoutTransportControls();
            lblEditorHint.SetBounds(pad, ClientSize.Height - Font.Height - Ui(8), ClientSize.Width - pad * 2, Font.Height + Ui(2));
            lblEmptyProject.SetBounds(0, 0, panTracks.ClientSize.Width, panTracks.ClientSize.Height);
        }
        finally { ResumeLayout(false); }
        Invalidate();
    }

    private void LayoutEditorTransport()
    {
        int top = bottomBarTop + Ui(10), height = Math.Max(Ui(56), Font.Height + Ui(42));
        btnPlay.SetBounds(Ui(12), top, Ui(72), height);
        btnStop.SetBounds(btnPlay.Right + Ui(8), top, Ui(60), height);
        LayoutSelectedVolume();
        int left = btnStop.Right + Ui(12);
        lblPlaybackTime.SetBounds(left, top, Math.Max(Ui(140), lblSelectedVolume.Left - left - Ui(12)), Font.Height + Ui(8));
        trkPlaybackPosition.SetBounds(left - Ui(4), lblPlaybackTime.Bottom + Ui(2),
            Math.Max(Ui(140), trkSelectedVolume.Left - left - Ui(8)), Ui(30));
        foreach (Control control in new Control[] { btnPlay, btnStop, lblPlaybackTime, trkPlaybackPosition, lblSelectedVolume, trkSelectedVolume }) control.BringToFront();
    }

    private void UpdateEditorControls()
    {
        if (!editorLayoutReady) return;
        btnSplitToolbar.Enabled = menuEditSplit.Enabled;
        btnTrimToolbar.Enabled = menuEditTrim.Enabled;
        btnUndoToolbar.Enabled = trackManager.CanUndo;
        btnRedoToolbar.Enabled = trackManager.CanRedo;
        lblTrackCount.Text = $"{trackManager.Tracks.Count} tracks";
        double scale = trackManager.DrawTimescale;
        lblZoomDetail.Text = scale < 1 ? $"{1 / scale:0.#} px / sample" : $"{scale * 1000 / AudioData.SampleRate:0.###} ms / px";
        lblEmptyProject.Visible = trackManager.Tracks.Count == 0;
        lblEditorHint.Text = ClientSize.Width < Ui(850)
            ? "Space: play/pause   |   Right-drag: seek   |   Wheel: zoom   |   F11: fullscreen"
            : "Space: play/pause   |   Right-drag: seek   |   Left-drag: select range   |   Wheel: zoom   |   F2: rename   |   F11: fullscreen";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!editorLayoutReady) return;
        using Brush surface = new SolidBrush(Surface(Settings.Color.WinBg, 14));
        e.Graphics.FillRectangle(surface, 0, menu.Bottom, ClientSize.Width, Math.Max(0, trkDrawTimescale.Bottom + Ui(4) - menu.Bottom));
        e.Graphics.FillRectangle(surface, 0, bottomBarTop, ClientSize.Width, Math.Max(0, ClientSize.Height - bottomBarTop));
        using Pen separator = new(Surface(Settings.Color.WinBg, 50));
        e.Graphics.DrawLine(separator, 0, bottomBarTop, ClientSize.Width, bottomBarTop);
    }

    private sealed class EditorMenuColors(Color surface) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => surface;
        public override Color ImageMarginGradientBegin => surface;
        public override Color ImageMarginGradientMiddle => surface;
        public override Color ImageMarginGradientEnd => surface;
        public override Color MenuItemSelected => Surface(surface, 24);
        public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override Color MenuItemPressedGradientBegin => MenuItemSelected;
        public override Color MenuItemPressedGradientEnd => MenuItemSelected;
        public override Color MenuBorder => Surface(surface, 50);
    }
}
