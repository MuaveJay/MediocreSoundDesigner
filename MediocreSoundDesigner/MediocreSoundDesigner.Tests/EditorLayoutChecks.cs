using MediocreSoundDesigner;
using NAudio.Wave;
using System.Reflection;

internal static partial class Program
{
    private static void RunEditorLayoutChecks()
    {
        Run("Editor toolbar follows valid edits and routes through existing history", EditorToolbarActions);
        Run("Top navigation, ruler, tracks and transport have separate bounds", EditorRegions);
        Run("Transport controls align horizontally at minimum and larger sizes", EditorTransportAlignment);
        Run("Track scrolling leaves navigation and playback controls fixed", EditorScrollIsolation);
        Run("Editor layout accommodates larger fonts without clipping essential controls", EditorLargeFont);
        Run("Theme defaults and toolbar states retain readable contrast", EditorTheme);
        Run("The editor presents an empty-state instruction and responsive zoom detail", EditorEmptyState);
        Run("Redesigned transport preserves play/pause, stop and selected volume behavior", EditorTransportActions);
        Run("Editor renders to a generated hidden-control canvas", EditorGeneratedPreview);
    }

    private static void ToolbarClick(Button button) => typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, new object[] { EventArgs.Empty });

    private static void EditorToolbarActions() => WithTimeline((form, manager, _) =>
    {
        Track source = manager.Tracks[0]; manager.SelectTrack(source); manager.SeekToFrame(1000); ClickHandler(form, "tmrUpdatePlaybackState_Tick");
        Button split = Field<Button>(form, "btnSplitToolbar"), trim = Field<Button>(form, "btnTrimToolbar");
        Assert(split.Enabled && !trim.Enabled && !Field<Button>(form, "btnUndoToolbar").Enabled, "Toolbar state does not reflect valid operations.");
        ToolbarClick(split); Assert(manager.Tracks.Count == 3 && manager.Tracks[^1].StartFrame == 1000, "Split toolbar bypassed the shared playhead.");
        ToolbarClick(Field<Button>(form, "btnUndoToolbar")); Assert(manager.Tracks.Count == 2 && Field<Button>(form, "btnRedoToolbar").Enabled, "Toolbar Undo failed.");
        ToolbarClick(Field<Button>(form, "btnRedoToolbar")); Assert(manager.Tracks.Count == 3, "Toolbar Redo failed.");
        manager.Undo(); manager.SetRangeSelection(source, 200, 700); ClickHandler(form, "tmrUpdatePlaybackState_Tick");
        ToolbarClick(trim); Assert(source.StartFrame == 200 && source.TimelineFrameCount == 500, "Trim toolbar lost timeline placement.");
    });

    private static void EditorRegions() => WithTimeline((form, manager, _) =>
    {
        foreach (Size size in new[] { new Size(650, 500), new Size(1100, 740), new Size(1920, 1080) })
        {
            form.Size = size; InvokeTimeline(form, "UpdateSizes"); CheckWindowControls(form);
            Control zoom = Field<Control>(form, "trkDrawTimescale"), ruler = Field<Control>(form, "timelineRuler");
            Control tracks = Field<Control>(form, "panTracks"), range = Field<Control>(form, "lblSelectionRange");
            Assert(Field<Button>(form, "btnImportAudio").Bottom < zoom.Top && zoom.Bottom < ruler.Top && ruler.Bottom < range.Top &&
                range.Bottom < tracks.Top && Field<Control>(form, "scrTimeline").Bottom < Field<Button>(form, "btnPlay").Top,
                "Editor regions overlap.");
            foreach (string name in new[] { "btnImportAudio", "btnSplitToolbar", "btnTrimToolbar", "btnUndoToolbar", "btnRedoToolbar", "btnFitWaveform", "lblZoomDetail", "lblEditorHint" })
                Assert(form.ClientRectangle.Contains(Field<Control>(form, name).Bounds), name + " is off-screen.");
        }
    });

    private static void EditorTransportAlignment() => WithTimeline((form, manager, _) =>
    {
        foreach (Size size in new[] { new Size(650, 500), new Size(1100, 740), new Size(1920, 1080) })
        {
            form.Size = size; InvokeTimeline(form, "UpdateSizes");
            Button play = Field<Button>(form, "btnPlay"), stop = Field<Button>(form, "btnStop");
            Label clock = Field<Label>(form, "lblPlaybackTime"), volume = Field<Label>(form, "lblSelectedVolume");
            Assert(play.Top == stop.Top && clock.Top == play.Top && volume.Top == play.Top &&
                play.Right < stop.Left && stop.Right < clock.Left && clock.Right < volume.Left,
                "Transport is not arranged in one horizontal bar.");
            Assert(Field<TrackBar>(form, "trkPlaybackPosition").Right < Field<TrackBar>(form, "trkSelectedVolume").Left,
                "Seek and selected-volume sliders overlap.");
            Assert(play.BackgroundImage is null && play.Text == "Play", "Legacy oversized image button is still used.");
        }
    });

    private static void EditorScrollIsolation() => WithTimeline((form, manager, _) =>
    {
        for (int i = 0; i < 12; i++) manager.AddTrack(new Track(Constant(1000)));
        Panel tracks = Field<Panel>(form, "panTracks");
        typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null)!.Invoke(tracks, new object[] { true });
        InvokeTimeline(form, "UpdateSizes");
        Rectangle play = Field<Button>(form, "btnPlay").Bounds, ruler = Field<TimelineRuler>(form, "timelineRuler").Bounds;
        tracks.AutoScrollPosition = new Point(0, 350); InvokeTimeline(form, "UpdateSizes");
        Assert(tracks.AutoScrollPosition.Y < 0 && Field<Button>(form, "btnPlay").Bounds == play && Field<TimelineRuler>(form, "timelineRuler").Bounds == ruler,
            "Scrolling moved fixed controls or reset scroll position.");
    });

    private static void EditorLargeFont() => WithTimeline((form, manager, _) =>
    {
        using Font large = new("Segoe UI", 13f); form.Font = large; form.ClientSize = new Size(1200, 850); InvokeTimeline(form, "UpdateSizes");
        CheckWindowControls(form);
        Assert(Field<Label>(form, "lblSelectedVolume").Height >= form.Font.Height &&
            manager.Tracks.All(track => track.TrackHeader.Height >= form.Font.Height + 6), "Large text is clipped in the header or transport.");
    });

    private static void EditorTheme() => WithTimeline((form, manager, _) =>
    {
        foreach (string name in new[] { "btnImportAudio", "btnPlay", "btnStop", "btnFitWaveform" })
        {
            Button button = Field<Button>(form, name);
            Assert(button.FlatStyle == FlatStyle.Flat && button.Cursor == Cursors.Hand &&
                TrackVisuals.Contrast(button.ForeColor, button.BackColor) >= 3 &&
                button.FlatAppearance.MouseOverBackColor != button.BackColor, "Toolbar lacks readable normal/hover states.");
        }
        Assert(Field<MenuStrip>(form, "menu").Items.OfType<ToolStripMenuItem>().Any(item => (item.Text ?? "").Replace("&", "") == "Generate") &&
            Field<MenuStrip>(form, "menu").Items.OfType<ToolStripMenuItem>().Any(item => item.Text == "Playback"), "Existing feature menus were lost.");
    });

    private static void EditorEmptyState() => WithTimeline((form, manager, _) =>
    {
        Label empty = Field<Label>(form, "lblEmptyProject");
        Assert(empty.Text.Contains("Generate") && empty.Parent == Field<Panel>(form, "panTracks") &&
            !Field<Button>(form, "btnSplitToolbar").Enabled, "Empty project lacks guidance or disables the wrong actions.");
        manager.AddTrack(new Track(Constant(1000))); manager.DrawTimescale = 0.125; ClickHandler(form, "tmrUpdatePlaybackState_Tick");
        Assert(Field<Label>(form, "lblZoomDetail").Text == "8 px / sample", "Sample zoom detail is incorrect.");
        manager.DrawTimescale = 441; ClickHandler(form, "tmrUpdatePlaybackState_Tick");
        Assert(Field<Label>(form, "lblZoomDetail").Text == "10 ms / px", "Overview zoom detail is incorrect.");
    }, addTracks: false);

    private static void EditorTransportActions() => WithTimeline((form, manager, devices) =>
    {
        manager.SelectAllTracks(); manager.SeekToFrame(1000); ClickHandler(form, "btnPlay_Click");
        Assert(Field<Button>(form, "btnPlay").Text == "Pause", "Playing button lacks pause label.");
        devices[0].AdvanceFrames(2000); ClickHandler(form, "btnPlay_Click");
        Assert(manager.State == PlaybackState.Paused && manager.GetPlayheadFrame() == 3000, "Redesigned pause changed position.");
        TrackBar volume = Field<TrackBar>(form, "trkSelectedVolume"); volume.Value = 60; ClickHandler(form, "trkSelectedVolume_Scroll");
        Assert(manager.SelectedTracks.All(track => track.Volume == 0.6f), "Redesigned volume ignored selected tracks.");
        ClickHandler(form, "btnPlay_Click"); Assert(manager.GetPlayheadFrame() == 3000, "Redesigned resume lost position.");
        ClickHandler(form, "btnStop_Click"); Assert(manager.GetPlayheadFrame() == 0 && Field<Button>(form, "btnPlay").Text == "Play", "Redesigned Stop failed.");
    });

    private static void EditorGeneratedPreview() => WithTimeline((form, manager, _) =>
    {
        form.ClientSize = new Size(1100, 720);
        manager.Tracks[0].ReplaceSamples(Enumerable.Range(0, 44100).SelectMany(i => Enumerable.Repeat((float)(Math.Sin(i * 0.061) * Math.Sin(i * 0.00023) * 0.7), 2)).ToArray());
        manager.Tracks[1].ReplaceSamples(Enumerable.Range(0, 11025).SelectMany(i => Enumerable.Repeat((float)(Math.Sin(i * 0.13) * Math.Exp(-i / 2400.0) * 0.85), 2)).ToArray());
        manager.Tracks[0].Rename("Atmosphere"); manager.Tracks[1].Rename("Accent");
        manager.SetRangeSelection(manager.Tracks[0], 4000, 13000); manager.SeekToFrame(16000);
        manager.SetViewport(0, 50, Field<Panel>(form, "panTracks").ClientSize.Width); InvokeTimeline(form, "UpdateSizes");
        using Bitmap image = new(form.Width, form.Height);
        // DrawToBitmap renders this hidden control tree. It does not capture the desktop.
        form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        using Graphics graphics = Graphics.FromImage(image);
        int border = (form.Width - form.ClientSize.Width) / 2;
        Point clientOrigin = new(border, form.Height - form.ClientSize.Height - border);
        // A hidden form suppresses child visibility. Render its controls individually
        // to the same off-screen canvas, never showing a native window.
        void Render(Control control, Point origin, Rectangle clip)
        {
            if (control.Width <= 0 || control.Height <= 0 || control.Name is "lblDebug" or "lblPlayhead" or "reorderMarker" or "lblEmptyProject") return;
            Point location = new(origin.X + control.Left, origin.Y + control.Top);
            using Bitmap child = new(control.Width, control.Height);
            control.DrawToBitmap(child, new Rectangle(Point.Empty, child.Size));
            var state = graphics.Save(); graphics.SetClip(clip);
            graphics.DrawImageUnscaled(child, location); graphics.Restore(state);
            Rectangle childClip = Rectangle.Intersect(clip, new Rectangle(location, control.ClientSize));
            foreach (Control nested in control.Controls) Render(nested, location, childClip);
        }
        Rectangle clientClip = new(clientOrigin, form.ClientSize);
        foreach (Control control in form.Controls) Render(control, clientOrigin, clientClip);
        string path = Path.Combine(AppContext.BaseDirectory, "editor-layout-preview.png"); image.Save(path);
        Assert(new FileInfo(path).Length > 1000, "Generated editor preview is empty.");
    });
}
