# Rendering and split-boundary investigation

The two reports predate the exact-frame split command and waveform renderer overhaul. The earlier split command opened a separate numeric dialog with two displayed decimal places and a default time halfway through the selected track. It did not cut at the shared playhead. Earlier drawing sampled one frame per pixel and used a moving native playhead overlay. These paths were replaced in PRs #36 and #40; PR #45 also keeps flat waveform sections visible.

The 00:05.668 duration in the screenshots matches the current Gradient Noise generator's 250,000 mono frames at 44.1 kHz. The original random seed and numeric input are unavailable, so that match identifies a representative workflow, not proof of the exact original sample sequence or the precise cause of every screenshot pixel.

The current implementation was investigated with the actual generator, deterministic gradient noise, and the repository's beat-it.wav, silence.wav and So-Tired-forward.wav fixtures. Tests cut at integer frame 58962, displayed 00:01.337, and verify exact source/tail boundaries, sample-identical recombination, quiet starts, fractional view origins, overview/sample-level scales, ruler/playhead/edge pixels, moved rows, scrolling, and Undo/Redo.

For the streak report, hidden native controls emit actual invalidation rectangles. Tests apply only those regions to retained waveform canvases, then compare every byte with full fresh rendering. Coverage includes all split rows, forward/backward right-dragging, adjacent cursor positions, high zoom, the top zoom slider, wheel zoom, the bottom playback-position slider, and fake-output playing/paused seeking. No stale stripes, trails, timing changes or sample changes remain in these workflows.

The existing production fixes pass this expanded coverage; no further production-audio changes are needed. This follow-up adds regression tests and records the evidence supporting completion of #26 and #35. It does not claim desktop capture, exact-original-random-seed reproduction or physical speaker/microphone verification.
