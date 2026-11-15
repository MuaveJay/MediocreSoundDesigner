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
