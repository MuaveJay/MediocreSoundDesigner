# Audio timeline regression checks

From the repository root, run:

```powershell
dotnet run --project MediocreSoundDesigner/MediocreSoundDesigner.Tests
```

The checks use generated audio, temporary WAV fixtures, and a fake output device.
They do not record the microphone or play audio through the speakers. A failing
check produces a nonzero exit code. No additional test-framework packages are needed.

Coverage includes zoom-invariant placement, mono/stereo and sample-rate conversion,
import/export, split/trim timing, existing audio edits, and playback cache invalidation
while preserving the paused position. Real device playback and microphone capture
still need a manual smoke test in Visual Studio.


Project-wide history checks also cover complete split/paste/delete operations,
cross-track edit order, grouping, no-op edits, failed-transaction rollback, and
restoring waveform rows and menu state. The control-tree tests construct forms
without showing them; no screen capture or physical audio device is used.

Track edits attached to a project automatically join its history. Wrap a group
of changes in `TrackManager.ExecuteEdit` to make it one Undo step. Add/remove and
track ordering operations participate in the same project history. The optional
`FrmEditMaster.EditExecutor` groups a dialog's Apply operation.


Transport checks cover shared cursor position, stopped/paused/playing seeks,
device reuse on unchanged resume, fresh audio after edits, natural completion,
looping, explicit Stop, end/empty-project handling, and hidden-form control wiring.
The bottom playback-position slider and time readout use the full mixed timeline;
the waveform zoom slider continues to control only the visible waveform scale.
Space toggles Play/Pause. At natural completion the cursor stays at the end;
explicit Play at the end starts another pass. Stop returns it to zero.


The fixed timeline ruler displays sample-aligned timestamps above all waveforms.
Click or drag the ruler with either mouse button, or right-click/right-drag a
waveform or the playhead line, to seek. Left-clicking a waveform still selects
its track. Seeking preserves the stopped/paused/playing state and clamps at the
project boundaries. The ruler uses the waveform zoom scale; the bottom position
slider spans the entire project. The selected track is outlined without shifting
its client origin. Vertical scrolling leaves the ruler fixed.

Timeline checks include pointer events on hidden forms, ruler tick/sample mapping,
selection and scroll alignment, drag release/capture cancellation, and allocation
bounds for repeated seeks. Any offscreen bitmap used to exercise custom painting
is created from generated test data and is never a screen capture.


Edit > Split at Playhead (Ctrl+K) cuts the selected track at the shared project's
exact sample frame and creates a second track at that absolute position. It is
disabled at either edge or outside that track. The whole split is one Undo step.
Edit > Split at Time... (Ctrl+Shift+K) retains optional numeric entry, measured in
seconds from the selected track's own start, initialized from the current cursor
when it is inside the track. Numeric entry rounds once to the common frame grid.
Tests verify sample-exact boundaries, moved/repeated splits, channel/settings
preservation, Undo/Redo, playback states, optional numeric entry, and UI alignment.


Left-drag a waveform in either direction to select the range to keep. The readout
shows absolute start/end and duration; Ctrl+T (Edit > Trim to Selection) removes
the outside audio while preserving its original project placement. A whole-track
or empty selection makes no edit. Escape clears the range. Changing tracks clears
the old range; Undo/Redo of an audio edit restores its associated range.
Right-drag still seeks. Ctrl+Shift+T (Trim at Times...) keeps optional numeric
track-relative input. Visual trim checks use exact integer frames and hidden
forms with synthetic mouse events; no screen/audio capture or playback occurs.


Track headers support click (one track), Ctrl-click (toggle), Shift-click
(contiguous range), and Ctrl+Shift-click (add a range). Ctrl+A selects all tracks;
Ctrl+Shift+A deselects. Selected rows are outlined, and the active row is marked
in gold and labeled active. Split/trim/effects and Copy Active Track use that
active track. Delete confirms the number of selected tracks and removes them as
one Undo step. Right-drag timeline seeking and zoom preserve a multiple selection.
Left-drag waveform range selection deliberately selects one track. Selection
membership, active track, and Shift anchor restore with project edit Undo/Redo.


The bottom Volume slider sets only the selected tracks to 0-200 percent. Mixed
means their current levels differ; moving the slider applies the displayed
absolute level to each. It is disabled with no selection. One mouse drag forms
one Undo step, including all selected tracks; Escape cancels an active drag.
Keyboard/wheel adjustments form individual edits. Waveforms and playing audio
refresh during adjustment; paused audio rebuilds at its cursor on resume.


Waveform view: exponential slider or mouse wheel over a waveform/ruler zooms;
wheel over the zoom control anchors at the cursor when visible. Fit shows all
tracks. The horizontal scrollbar navigates the zoomed timeline. Zoom spans
overview to eight pixels per sample. Overview drawing queries cached min/max
peaks; high zoom connects sample points and displays sample dots. Clip-edge
markers distinguish an actual boundary from a quiet first/last sample. The shared
playhead is painted on double-buffered waveforms rather than moved as an overlay
window; repainting clears its old background. These tests use generated canvases,
not screen captures. Hands-on reproduction of reported streaks remains pending.


Track identities: imported tracks use the filename; raw/generated/recorded tracks
receive numbered defaults. Copies and split tails retain the source color and get
a Copy/Split name suffix. Names/colors remain attached when rows are reordered.
Double-click a header or press F2 to rename the active track; the header context
menu offers Rename and Change Color. Metadata changes are undoable. Selected
outlines and the active marker remain distinct from each track's color, and
waveform/text colors are adjusted for readable contrast against the row.

Track reorder checks use hidden forms and synthetic handle gestures, with no screen capture. They cover drop preview, first/middle/last moves, stable identities/timing/selection, grouped history, no-op/cancel paths, edge autoscroll, and uninterrupted fake-device playback. Drag the :: grip at the left of a header; waveform left/right gestures retain their existing editing behavior.

Window mode checks use hidden forms to verify standard window controls, F11 fullscreen state, repeated normal/maximized restoration, removed-monitor clamping, layout bounds, and fake-device playback preservation. Escape exits fullscreen. Native caption behavior and mixed-monitor DPI transitions still need hands-on verification.

Editor layout checks verify toolbar routing/state, separate navigation/track/transport areas, minimum and large windows, larger fonts, scroll isolation, theme contrast, empty state and fake-device transport. A generated hidden-control bitmap is rendered for layout inspection without screen capture. See ../EDITOR_GUIDE.md for controls and hands-on verification.

Overview rendering also checks flat positive, negative and silent audio: each remains a visible one-pixel trace inside its clip bounds rather than a zero-length GDI line.

Modifier selection checks exercise complete mouse-down/up/click sequences across headers, waveforms and reorder grips. GetKeyboardState/SetKeyboardState temporarily change only the test thread's keyboard table so the production Control.ModifierKeys lookup is exercised and then restored. No SendInput, global key presses, screen capture or visible forms are used. The checks cover toggle/range anchors, releasing modifiers before mouse-up, double-click mute suppression, normal range/scrub behavior, grouped volume and uninterrupted fake-device playback/history.

Shortcut help checks compare menu assignments against the catalog, verify editor/mouse coverage and context-sensitive descriptions, exercise search/no-results/reset, and inspect the read-only/resizable dialog at minimum size. Hidden-form/fake-device checks ensure help creation and searching preserve the project, selection, playback and Redo. Native F1 modal opening/closing remains a hands-on check.

Reported rendering checks exercise the 250,000-frame Gradient Noise workflow at frame 58962 (00:01.337), three repository WAV fixtures, quiet fractional-pixel boundaries, moved rows, scrolling and split history. Hidden native controls emit real invalidation rectangles; applying those rectangles to retained canvases must match a full fresh render byte-for-byte across split rows, sample-detail zoom, slider/wheel changes, bottom-position dragging and fake-device playing/paused seeking. No screen capture or visible application is used.

Playhead-follow checks verify elapsed-time easing, threshold/latch behavior, extreme zoom and project bounds, Pause/Resume/Stop/looping, option state, manual-pan suspension/resumption, gesture deferral, zoom anchors, shared ruler/scrollbar state and byte-identical moving-view repainting. Fake output ensures camera motion does not change audio, timing, selection, devices or Undo/Redo.
