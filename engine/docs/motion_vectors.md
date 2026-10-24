# Motion vectors: rework after the 2026-09-26 video

Source material: `video_2026-09-26_04-19-53.mp4`, FSR 3.1 Performance, 960×540 render → 1920×1080
output, and `out/dev/t71.log`. This describes the working tree's changes at the time, not a
confirmation of the quality in the game.

A pass over all G-buffer draws turned out too expensive: Performance ran at about 49 FPS with it and
77–78 FPS without. Object vectors are now on by default only for shaders with a pose/bone buffer
(640–16384 bytes, except the shared scene buffer of 864 bytes; a 16-byte stride). In a trial run
with a narrower threshold of 1024 bytes this cut the draws handled to about 140 a frame, removed
the history overflow and gave about 81–87 FPS. The other draws keep the cheap camera vectors.
`BB_OBJECT_MOTION=0` turns the whole feature off, `BB_OBJECT_MOTION_ALL=1` brings back the pass over
everything for A/B. The auxiliary target's alpha channel holds depth: if a later ordinary draw
covered an object, its vector is not used. The quality of the character and weapons in motion after
the selection still has to be checked visually.

## What was found

- At the end of the original log, over 600 frames: 257400 G-buffer draws, 256200 declared moving,
  only 30000 got previous positions (~11.7% of all draws). This is draw coverage, not a share of
  pixels. The old counter did not tell the reasons for each miss apart.
- The history reserved the size of a whole vertex buffer times the number of instances **including
  firstInstance**. For submeshes of shared buffers these are extra positions; the budget was 2097152
  positions a frame.
- The history had no barrier of its own between writes and reads by vertex shaders; reusing the ring
  of host parameters did not wait for the GPU.
- Disabled writes of all vertices went to one scratch element.
- `CameraMotion::OnDisplayPass` reset Depth() before computing the jitter phases: Performance got 8
  phases instead of 32. Reconstructing the position from depth also ignored the viewport offset.

## Changes

- The history's range comes from the min/max of the actual indices, with baseVertex and primitive
  restart. FirstInstance is subtracted in the shader instead of using memory.
- Every suitable direct G-buffer draw is saved, from its first appearance. The heuristic of changed
  constants was removed: it called almost every draw moving and missed the start of motion. A read
  is possible only when it matches the previous frame.
- The key includes every vertex stream description, the shader, the address and a hash of the index
  contents, the vertex range, the instances and the number among identical draws. A topology change
  or a skipped frame resets that history.
- The budget is 4194304 positions a frame; the two arrays together take 128 MiB instead of 64 MiB.
  When memory runs out, camera vectors remain. The log shows saves, matches, missing history, lack
  of memory and invalid ranges separately.
- Vertex-to-vertex barriers were added, a wait for the GPU before reusing host parameters, coherent
  host memory and separate tracking of the motion image's layout. Reading the image again does not
  clear the flag that vectors exist.
- Disabled accesses are skipped by branching. Repeated invocations of one indexed vertex use atomic
  writes of the position's components. Positions behind the camera and non-numeric or infinite
  vectors are not used.
- Performance gets 32 jitter phases after the FSR context is created. Camera vectors are
  reconstructed from unjittered coordinates; object vectors already came from clip positions before
  the viewport offset.

## Checks

Build and CPU tests:

```bash
cd native_probe
nix-shell shell.nix --run 'bash build.sh && ninja -C out/gpu motion-history-test motion-shader-test && out/gpu/motion-history-test && out/gpu/motion-shader-test out/motion-shaders'
```

`test_motion_history.cpp` checks u16/u32 ranges, restart, a negative baseVertex, firstInstance,
repeated draws, a topology change, a skipped frame, an exhausted budget, overflow and the number of
phases. A run with AddressSanitizer/UBSan passed as well.

`test_motion_shaders.cpp` uses the real SPIR-V backend and creates VS/PS with and without vectors.
All four results passed `spirv-val --target-env vulkan1.3`. Repeating it needs `spirv-val` from
SPIRV-Tools:

```bash
for shader in out/motion-shaders/*.spv; do
    spirv-val --target-env vulkan1.3 "$shader" || exit
done
```

Build and test logs: `out/dev/motion-validation.log`. The checking environment had no access to
`/dev/dri`: the quality on an RX 7800 XT, the FPS and the coverage after the changes were not
checked yet. Passing SPIR-V validation does not prove the picture is right.

## The next run in the game

From `game_files`:

```bash
BB_FRAME_STATS=1 BB_TOGGLE_FILE=out/dev/motion-toggles bash native_probe/run.sh 2>&1 | tee native_probe/out/dev/motion-run.log
```

Bit 29 in the temporary toggle file turns off only the history writes. A fair performance
comparison runs the game with `BB_OBJECT_MOTION=0` and without it: the pipeline choice and the extra
target are set when the process starts. After switching, give FSR a few frames to refresh its
history. Compare one scene: the character and weapon while running, a camera turn, a fight, a still
camera. After ~600 frames check `Object motion:`: `with history`, `capacity skips`, `unmatched`.

Remaining limits: identical meshes are matched by draw order, which can change when objects are
culled; dynamic vertex buffers changing address lose the match. Indirect draws,
tessellation/geometry shaders, MSAA and passes outside the G-buffer stay on camera vectors.
Transparency and particles, the blurry UI and moving the scaling into the renderer are not solved by
this change. The cost of scanning indices and of the atomic writes has to be measured in a real run.

## September 29: weapons and small skeletons (ghosting of the spinning saw)

Video `video_2026-09-29_23-08-26.mp4`: the character stands still, the saw disc spins and leaves a
trail. The log of `BB_MOTION_SELECT_LOG=1` showed why: the vertex shader's second buffer is a bone
palette of 48 bytes each (3x4), and for weapons and props it is 96–384 bytes (2–8 bones). The rule
"640 bytes and up" left such draws out, so the disc got only camera vectors and was still for FSR.

Changes:

- `Motion::ClassifyBuffer` (`motion_history.h`): 864 is the scene, ≥640 a character skeleton,
  96..639 in multiples of 48 a small skeleton. Pipelines with any skeleton get the variant with
  vectors.
- There are about a thousand small skeletons a frame, most of them static parts of the map. So they
  are gated: the palette's hash is compared with the previous frame by a cheap key
  (`History::Moving`) before the indices are scanned. A match: the camera vector is exact, nothing is
  written. A change: the positions are saved, and from the next frame the object's vector is used.
  Hashing the 416-byte buffer does not work: it changes with the camera for almost all of them
  (checked: history overflow and FPS dropping to ~40).
- Menu: "Show motion vectors (debug)". Red/green are |x|/|y|, blue means the pixel got an object
  vector. `BB_MOTION_SELECT_LOG=1` prints the G-buffer shaders with their buffer sizes and the
  decision.

Measured in `out/motion-gated.log`: ~225 saved draws and ~600 "still" ones a frame, no overflow,
58–80 FPS in game scenes. Whether the trail on the saw is gone still needs a visual check.
