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
