# The native Bloodborne port: what was done and what comes next (September 2026)

> Historical (bbport, before Lantern Engine). The current plan is
> [EMULATION_REMOVAL_PLAN.md](../EMULATION_REMOVAL_PLAN.md); who plays and the release rules are in
> [COMPATIBILITY.md](../COMPATIBILITY.md).

The main text is the state on 2026-09-26; the first section below is what changed by 30.09. The most
detailed snapshot: [CURRENT_STATUS_2026-09-29.md](CURRENT_STATUS_2026-09-29.md). The report on the
work of 30.09–01.10 (GPU pipeline, upscaler, a crash, launcher, AppImage):
[SESSION_2026-09-30.md](SESSION_2026-09-30.md). Details on single topics:
[parallel_gpu.md](../parallel_gpu.md) (GPU multithreading, hitches while streaming),
[upscaler.md](../upscaler.md) (analysis of the game's frame, the scene constants).

## Update of 30.09

- Done after 26.09: FSR 4 INT8 (v07), object motion vectors, preset changes without a restart
  (reduced scene targets; D32S8 depth copied by a shader), LTO and PGO, draw preparation scaled to
  the number of threads (Steam Deck too).
- Problems from "Known problems" below: item 3 (restart on a preset change) is solved; item 1
  (object vectors) is implemented, FSR 4 still ghosts on the character at Ultra Performance; item 2
  (blurry interface): the UI is drawn at 1920×1080.
- Limitation: the reduced scene render (`SceneTargets::Eligible`) needs a 1920×1080 target, so it
  works only with 1080p output.
- Parallelizing the GPU command thread (step 2 of [parallel_gpu.md](../parallel_gpu.md)): textures on
  a separate thread in parallel with buffers work correctly but give no gain: each draw's data moves
  between cores. The bottleneck is the GPU command thread: 91% on the CPU, ~9% waiting for small
  guest memory copies before a fence. Next: a two-stage pipeline (a second thread owns the texture
  cache and draw recording, with a copy of the registers from the scanner's deltas) or memoizing
  texture bindings across frames; see "What would scale" in parallel_gpu.md.
- **Two-stage draw pipeline** (`vk_draw_pipe.h`): the GPU command thread decodes PM4 and selects the
  pipeline; a second thread (`bb:DrawRec`) with its own copy of the registers binds resources and
  records the draw. +19% FPS on 16 threads, +18% on 8 (like the Steam Deck). On by default with 8+
  threads (`BB_DRAW_PIPE=0/1`). The bottleneck is now the second stage; details and the plan are in
  parallel_gpu.md.
- Presets and GPU load: the scene costs little on an RX 7800 XT, so the preset changes GPU time by
  only ~0.5 ms, while FSR 4 itself at 1080p output costs ~1.4 ms (FSR 4 Ultra Performance is more
  expensive than native rendering without an upscaler). Fixed: the lighting passes stayed at
  1920×1080 with any preset (the check looked at other target slots); reduced targets are now
  sampled directly, without resampling to native (the shader cache is rebuilt once). Next for the
  Steam Deck: the guest copy of the memory of render target `3d5ebf4e` (see parallel_gpu.md).
- **Fixed a crash with the pipeline** (guest heap corruption, guest offset `0x263b8e7`, after 13–17
  minutes): the "GPU idle" interrupt went out before the EOP labels deferred to the Vulkan
  recording thread, and the game freed objects with those labels. Now the pipeline and the deferred
  labels are waited for before it; 2 runs of 30 minutes without a crash (before the fix: 7 crashes
  in 7).
- **1440p/2160p output** (the "Output resolution" menu, lantern.ini `output_res`, after a restart):
  the scene is drawn at the preset's resolution of the output, the upscaler (FSR 3 or FSR 4)
  completes the frame, the interface is rasterized at the output resolution. The game's effects
  (aberration, DoF, blur, SSAO, the game's anti-aliasing, dynamic shadows, SSR, cutscenes, LOD) are
  there too.
- **Launcher** (`launcher/bb-launcher.sh`, GTK4/libadwaita) and an **AppImage** for the Steam Deck
  (`bash packaging/appimage.sh` → `dist/`; data in `~/.local/share/bbport`, `--play` starts without
  the launcher window).
- Tools: `BB_PAD_FILE` (scripted gamepad input), `ab.sh` (A/B of `BB_TOGGLE_FILE` bits in the
  running game); idle and blocking of the GPU thread in `Frame stats`.

## How the port works

- The original eboot (CUSA03173 v01.09) runs directly on x86-64. The PS4's system libraries are
  replaced by an HLE runtime of its own in C (`runtime_*.c`, `probe.c`).
- Graphics: shadPS4's video core and shader recompiler (GPL), built into `libbbgpu.so` (`gpu/`),
  with changes of its own. The shadPS4 emulator as a whole is not used.
- The game's patches (FPS, resolution and others) are the community's XML (`patches/Bloodborne.xml`),
  compiled by `patches.py` and applied by the loader at the start.

## Done

### Performance and smoothness

| What | Result |
|---|---|
| Vulkan command recording on its own thread, texture/buffer caches, an rwlock instead of a mutex | 26 → 64 FPS in one scene |
| Draw call preparation on 4 threads (`bb:DrawPrep`), checked on the GPU thread | +11.5% |
| A 256 KiB unprotect window when the game writes memory (fewer page faults) | +22% |
| Texture description cache, a fast path for the same render targets | up to ~94 FPS outdoors |
| Guest memory copies on a thread pool, a staging buffer pool | streaming freezes 60–170 ms → 40–50 ms |
| Waiting for copies before fences the game sees | interface flicker gone |
| "Readbacks Relaxed" mode | no "vertex explosions" (FaceGen) |
| Vblank 480 Hz + a frame limit of min(monitor, 120), output right at the flip | even frame pacing; running does not slow down above 120 FPS |

Everything switches during play through `BB_TOGGLE_FILE` (a bit mask) for A/B comparisons.
Statistics: `BB_FRAME_STATS=1`.

### Sound, input

- Sound output at the right tempo (it used to click every 10.7 ms).
- Gamepad through SDL3, a fallback keyboard layout.

### Frame analysis

- `BB_CAPTURE_TRIGGER=<file>` records one frame: passes, targets, shaders, textures, constants,
  blend modes.
- The structure of Bloodborne's frame was worked out (G-buffer, lighting, RGBA16F scene colour, fog,
  post-processing, tone mapping, the game's anti-aliasing, interface), and the 864-byte scene
  constants (view and projection matrices).

### Temporal upscaler (FSR 3.1)

- **Camera motion vectors**: from the scene depth and the current and previous frame's matrices
  (`vk_camera_motion.cpp`, `camera_motion.comp`). Checked by reprojecting the previous frame: static
  geometry matches.
- **Jitter**: a subpixel offset of the scene geometry through the viewport, a Halton(2,3) sequence;
  8·(output/render)² phases. The interface and post-processing are not offset.
- **FSR 3.1** (FireBurn/FSR-Vulkan, native Vulkan, submodule `gpu/third_party/fsr-vulkan`):
  - *Native AA*: on the scene's HDR colour before post-processing, 1:1.
  - *Quality/Balanced/Performance/Ultra Performance presets* (x1.5/1.7/2/3): the game renders at a
    lower resolution (a resolution patch that `patches.py` derives from the community's 720p patch),
    FSR scales the finished frame up to 1920x1080 before the first interface pass. The interface
    and the output pass are redirected to full-size images, which the presenter shows instead of the
    game's buffer.
- **Reactive mask** (a snapshot of the scene before transparent draws, compared before the fog is
  applied): works, but jitters on Bloodborne's haze; off by default.
- **In-game menu** (Dear ImGui, Insert or L3+R3): upscaler, preset, sharpening, jitter, mask, FPS
  counter. Settings in `lantern.ini`. A preset change: the "Apply and restart the game" button.

## Known problems

The state of the last commit. The working tree had a move of the presets to reduced render targets
while the guest buffers, post-processing and UI stay at 1920×1080. Switching between presets and
copying colour/depth/stencil passed a Vulkan test on lavapipe. The RX 7800 XT does not support
blitting D32S8, so the new path left FSR's input at 1920×1080 and lowered FPS. For such GPUs the
start now picks the proven path with the game's resolution patch and a 1920×1080 UI automatically; a
preset change needs a restart again. On GPUs with the needed support, switching without a restart
stays. Items 2–3 below refer to the last commit, before these working-tree changes.

A pass of object motion vectors over everything was too expensive: Performance gave about 49 FPS
with it and 77–78 FPS without. Now the extra target and shaders are used only for G-buffer draws
with a pose/bone buffer; the rest use the camera vectors. In a trial run the selective path held
about 81–87 FPS, with no misses for lack of history capacity. Object vectors are on by default;
`BB_OBJECT_MOTION=0` turns them off, and `BB_OBJECT_MOTION_ALL=1` brings back the old path over
everything for comparison. Choosing by buffer size is a heuristic for now: the quality of weapons
and clothes in motion needs checking in the game.

1. **Moving objects "crumble"** (the weapon on the back, the character, cloth, enemies): there are
   only camera motion vectors. The third-person camera follows the character, so the camera vectors
   are wrong for it, and the upscaler takes history from the wrong place. This affects every
   temporal upscaler (FSR 3/4, DLSS, XeSS).
2. **The interface is blurry in presets**: with the resolution patch the game thinks it is a 540p
   game and apparently prepares fonts and interface elements for that resolution. Menus (title,
   pause) do not go through the upscaler at all.
3. **A preset change needs a restart**: the resolution is set by a code patch at the start, and the
   game allocates its buffers once.

## Plan

### 1. Object motion vectors

All upscalers and frame generation need them. The game does not compute them, so we do.

How:
- For each draw into the G-buffer (passes with 5+ targets) the recompiler makes a second variant of
  the vertex shader that outputs the position twice: with the current constants and with the
  previous frame's. The difference of the projections is the motion vector, written to an auxiliary
  RG16F target as an extra pixel shader output.
- The previous frame's constants: for each draw, remember the contents of the constant buffers (the
  object matrix, skeleton bones) by the key "shader + draw number in the pass" or by the data's
  address. To find out: how the game keeps skeleton matrices, in buffers overwritten every frame
  (then the previous version is copied) or in a ring buffer (then the previous frame's address is
  enough).
- Where an object is not found (the first frame, a new object): camera vectors, as now.
- Check: a debug view of the vectors (`BB_DEBUG_MOTION`), reprojecting the previous frame by the full
  vectors, and visually the saw on the back while running.

### 2. Scaling in the renderer (instead of the resolution patch)

The game runs at 1920x1080 (or the monitor's resolution), and only the 3D scene's passes are drawn at
the lower resolution. The interface and menus stay sharp, and the preset changes on the fly.

How:
- The texture cache allocates a reduced-size copy for the scene's targets (G-buffer, depth,
  lighting, scene colour, effects); the guest memory and the game's descriptions do not change.
- The viewport and scissor of passes into these targets are scaled (as already done for the
  interface, including passes with clipping off).
- Shaders that work in pixel coordinates (`gl_FragCoord`, `texelFetch`, per-pixel compute shaders)
  get a scale factor through the recompiler's push constants. Samples by normalized coordinates work
  unchanged.
- FSR goes where Native AA is: the scene colour before post-processing (HDR, the best quality);
  post-processing and the interface run at full resolution.
- Risks: passes that read the scene's targets in pixel coordinates in post-processing, and reads of
  these targets from guest memory (they must be forbidden or scaled back).

### 3. Other upscalers

All use the same inputs (colour, depth, motion vectors, jitter, masks).

- **FSR 4 (INT8)**: FSR-Vulkan has an experimental FSR 4 v07 INT8 provider: add it as a menu choice
  and compare with FSR 3.1 after step 1.
- **DLSS**: the native Linux Vulkan SDK (NGX). It does not work on an RX 7800 XT, but NVIDIA users
  need it.
- **XeSS / XeFG and OptiScaler**: Intel ships them only as Windows DLLs. Options: a Windows DLL (PE)
  loader inside the process, or running the whole build under Proton with OptiScaler, given the
  inputs through the DLSS interface. Checked on 29.09: libxess.dll (the same in DOOM: The Dark Ages,
  PRAGMATA and others) has a Vulkan path (`xessVKCreateContext`, `xessVKExecute`), but imports ~300
  functions: MSVCP140 (iostream), VCRUNTIME140 (C++ exceptions), UCRT, kernel32, setupapi, dxgi.
  Postponed; the most realistic path is a helper under Wine with shared Vulkan memory.

### 4. Frame generation

- FSR 3.1 Frame Generation (in FSR-Vulkan): an intermediate frame between two finished ones, from the
  same motion vectors; the interface must be drawn on top separately (the interface redirection
  exists: it can be rendered to a layer of its own).
- XeFG: after a solution for the Windows DLLs (see above).
- Needed: frame delivery through the presenter (the queue and VRR already work).

### 5. Other

- Steam Deck: a portable build (patchelf, without Nix), checking the presets at 1280x800.
- Further offloading of the GPU thread (step 2 of [parallel_gpu.md](../parallel_gpu.md)).
