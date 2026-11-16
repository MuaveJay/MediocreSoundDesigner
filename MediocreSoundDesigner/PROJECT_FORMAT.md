# Sound Designer project files

Use **File > Save Project** (Ctrl+S) or **Save Project As** (Ctrl+Shift+S). **Open Project** (Ctrl+O) replaces the current session after an unsaved-work confirmation. **New Project** (Ctrl+N) starts a blank session. **Import Audio** (Ctrl+I) adds a track; **Export Audio** (Ctrl+E) writes the mixed WAV separately.

A `.msdproj` file is a ZIP archive containing a versioned `project.json` manifest and embedded canonical 44.1 kHz stereo float audio for every track. Samples are stored losslessly as little-endian 32-bit floats, with a SHA-256 checksum for each audio entry. Original WAV/recording files are not needed to reopen the project.

The project preserves edited audio, track ordering, start frames, names, colors, volumes, mute states, the playhead, selected/active tracks, selection anchor/range, zoom/view origin, looping and follow preference. Opening restores playback stopped. Undo/Redo history and the track clipboard are session-only and start fresh after opening/New; audio edits themselves are saved in the embedded samples.

The title's `*` tracks audio/track/loop changes relative to the last successful save. Undo/Redo can return to that saved state. Navigating the view, selecting tracks and listening alone do not mark the project modified, but their current state is captured when saving. Saving/exporting does not restart the current audio device. New/Open/Close offer Yes (Save), No (Discard) and Cancel. A failed or canceled save prevents replacing/closing the session.

Saves build and flush a temporary archive in the destination directory, then atomically replace the target. File-write/replacement failures preserve the old target and the unsaved indicator. Loading validates metadata, archive layout, lengths, checksums and limits before replacing the session; archive paths are never extracted to disk.

Version 1 supports up to 4096 tracks, 1 MiB of metadata, and 1 GiB of total decoded audio (about 50 minutes of stereo audio summed across all tracks). Invalid or unsupported projects are rejected with an error. Large projects may take longer to save/open and require memory for the current session, loaded audio and Undo snapshots. This feature does not add autosave or recovery.
