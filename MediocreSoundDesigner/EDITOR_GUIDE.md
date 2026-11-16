# Sound Designer controls

Open the solution in Visual Studio, select the desired local branch or `main`, then build and run. All feature branches are in MuaveJay/MediocreSoundDesigner and the local clone. Stop the running application before a normal rebuild if Visual Studio reports that its executable is locked.

| Action | Control |
| --- | --- |
| All shortcuts | View > Keyboard Shortcuts... or F1 opens searchable keyboard and mouse help; Close or Escape exits |
| Add audio | Open Audio button or File menu; Generate creates test sounds or records audio |
| Select tracks | Click a header; Ctrl-click a header, waveform or :: grip to toggle tracks; Shift-click selects a range of rows; Ctrl+Shift-click adds a range |
| Move playhead | Click/drag the timestamp ruler, or right-click/right-drag a waveform |
| Play / pause | Play/Pause button or Space; resumes at the shared playhead |
| Stop | Stop button; resets the playhead to the beginning |
| Split | Split button or Ctrl+K; splits the active track at the playhead into two tracks |
| Trim | Left-drag a waveform range, then Trim or Ctrl+T; the kept audio stays at its original timeline position |
| Precise numeric edits | Ctrl+Shift+K for split time; Ctrl+Shift+T for trim times |
| Zoom | Top zoom slider or mouse wheel over the ruler/waveform; zoom far enough to see individual samples |
| Navigate | Fit shows all tracks in time; the horizontal scrollbar pans the zoomed timeline |
| Change volume | Bottom volume slider changes only selected tracks; Mixed means their levels differ |
| Name / color | F2 or double-click a header to rename; header context menu changes name/color |
| Reorder tracks | Drag the :: grip at the left of a header; the gold line marks the drop position. Edge dragging scrolls; Escape cancels |
| Undo / redo | Toolbar or Edit menu; structural edits and slider drags are grouped operations |
| Loop | Playback > Loop Playback |
| Maximize / fullscreen | Standard maximize control; View menu or F11 for fullscreen; Escape exits fullscreen |

With multiple selected tracks, volume and deletion affect the group. Split, trim and audio effects operate on the active track, identified in its header. Audio timing is independent of vertical row order and waveform zoom.

Names and colors belong to the current session and Undo history. Export saves the mixed audio; the app does not yet save/reopen an editable multitrack project.

## Before the presentation

- The split-boundary and right-drag rendering regressions (#35 and #26) are covered by exact-frame, quiet-boundary and byte-for-byte retained-versus-fresh repaint checks. When reviewing the presentation build, use the visible clip-edge marker to distinguish the true cut from quiet samples.
- Try a real audio file, Play/Pause, Stop, looping, selected volume, split and range trim; compare what you hear with the ruler.
- Try header renaming/color, reorder grips, edge scrolling, Ctrl/Shift selection, and Undo/Redo.
- Maximize and restore, repeat F11/Escape, and check any monitors with different scaling.

Automated tests use hidden forms, generated audio/canvases, repository WAV fixtures and fake audio output. They cover the reported editing/drag workflows without desktop capture; physical audio devices and native dialogs still need ordinary hands-on checking.
