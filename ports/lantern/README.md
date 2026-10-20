# Lantern Engine + PsychoV30 (experimental port)

This branch carries a reproducible HDR patch, not a copy of Lantern's entire engine.

Pinned inputs:

- LanternEngine `6c2fbf48d0a84dd6e3fc88fee240e326b66a4e4f`.
- Its documented starting point, DarkIzuku/bloodborne_pc
  `8ecba9a4abdd54747e31d7fee54982e422b5f8e2` (`v0.4.0-darkizuku-preview.1`).
- The mode 5/PsychoV30 implementation from Cham17-dev/bloodborne_pc `hdr` at `0dce9f4`.

The Windows workflow reconstructs Lantern from those sources, applies the HDR patch, and builds
the engine and launcher. Its manual `baseline_check` option also compiles stock Lantern first;
that stock build was verified during the port. A successful build is not an in-game validation.

## Install for testing

1. Download `lantern-psychov30-package` and extract it into a **new, empty folder**.
   This is now a complete package, rather than an overlay for an upstream installation.
   Do not merge it into a newer Lantern release or reuse the previous overlay.
2. Start **Lantern Engine.exe** from that new folder. Its title is
   **Lantern Engine + PsychoV30**. Select your existing game dump (the folder with eboot.bin).
   Game data is not included or copied into the package.
3. Enable Windows HDR, then HDR output in the launcher. The HDR source defaults to
   `PsychoV30 (16-bit, restart)`. Start the game. Restart the game after changing HDR source.
4. Existing saves stay in the old installation. To use them here, select your existing user/save
   folder under the launcher's profile/settings controls, or copy the saves with the game closed.
   Keep a backup. Do not copy the old engine, executables or complete INI into this package.
5. Compare native resolution/upscaler off, DLAA, then DLSS Quality. Keep sharpening off for the
   initial comparison. Use the same camera, peak, paper white and grading settings.

The launcher uses its own `bbport-launcher-psychov30` settings folder. Updates point at this
fork, rather than installing stock upstream releases over PsychoV30. The log prints the full
path of the HDR engine. `Bloodborne.exe` is the packaged shortcut to this launcher.

The full package keeps binaries and supporting resources under `engine/`, with player data
and bbport.ini at the root. It includes embedded Python 3.12.10 and the preparation scripts
from the same public source snapshot as the compiled engine. The official beta 9.3 archive
already uses a native start executable and does not contain those scripts or Python. Its
released engine differs from that public source snapshot; only runtime resources, DLLs,
notices and defaults are reused, with SHA-256 checks. Our build supplies the engine, helper
programs, launcher and preparation scripts. This is not a port of all later upstream changes.
CI checks both folder layouts and runs the shipped Python through imports and game detection;
a successful smoke check still does not validate an actual game session.

The in-game mode provides Vanilla and PsychoV30, Exposure, Highlights, Shadows, Contrast,
Saturation, Bloom, Color Grade Strength, Black Floor and HUD brightness. Neutral values are
listed there. F9 cycles HDR / SDR reference / approximate split. Changing into or out of the
16-bit mode requires a restart; its grading and reference switches are live.

Paper white defaults to 203 nits in the engine and launcher. Explicit values already saved in
`bbport.ini` are preserved; change an existing 200-nit setting to 203 once if desired.
Paper white and peak brightness have editable numeric values beside their sliders: type a
value and press Enter or leave the field to apply it. Escape restores the current slider value.
The launcher hides Highlight boost and UI colour for PsychoV30 because they do not affect this
mode. HUD brightness remains available. Switching back to Native/Expand restores applicable rows.
The in-game menu exposes the source PsychoV30 scene controls, with their original neutral values;
its layout is simplified. Gamma 2.2 is fixed on, and HUD brightness is relative to paper white.

## Rendering contract

- PsychoV30 runs in the composite on scene-linear color. It retains the Bloodborne mid-grey
  anchor and the existing Vanilla-shadow match. The LUT uses max-channel normalization.
- The post-composite chain, UI, display redirection and presentation intermediates retain
  values above white in RGBA16F. Temporal reconstruction decodes to linear light and re-encodes.
- This mode bypasses Lantern's native gain map and expansion. The final pass converts to HDR10
  or scRGB, with the original mode 5 peak and black-floor behavior.
- A pre-HUD snapshot belongs to its display buffer and follows that frame through presentation.
- The legacy Native/Expand paths retain their original settings and formats.

Lantern's user-data push constants remain intact. Mode 5 adds HDR parameters after the original
128 bytes and requires at least **176 bytes** of Vulkan push-constant capacity. A startup check
reports the requirement on unsupported GPUs; Native/Expand use their original 128-byte range.
Lantern's point-sampler fallback to an 8-bit guest image is disabled in mode 5 to avoid clipping
HDR values. This may affect the appearance/cost of some effects at reduced presets. Composite
pipelines with an HDR companion compile together; other pipeline compilation remains asynchronous.
PsychoV30 uses a separate `-psychov30-<tag>` shader/driver cache (in `%APPDATA%\shadPS4\cache`),
where the tag is a hash of bbport's replacement shaders: the cache stores them already swapped in,
so a build with other versions of them starts a new cache instead of loading stale ones (a cache
from the first build of this port gave a black screen). Cached composites rebuild their HDR
companion when loaded, so restarting does not drop the HDR pass.

The display copy reads its brightness table as a 1D array: Lantern's recompiler keeps 1D image
types, unlike shadPS4 0.20.1 (bbport), which binds it as a 2D array view. The 2D array version
read 0 there and the whole frame was black.

## Validation still required

Test dark interiors, fog, fire, saturated blue lights, menus, loading transitions and camera
movement. Inspect HUD brightness, clipping, color order, borders and temporal stability. Capture
repeated JXR pairs for comparison with the reference mod; those captures are not in this repo.
No FPS or visual-quality improvement is claimed before those measurements.

## Sources and licenses

Lantern's engine and the combined port are GPL-3.0-or-later. Preserve the upstream package's
licenses when installing the overlay. The PsychoV30 port retains its RenoDX MIT notices; the
shader variant data and integration retain their bbport/shadPS4 notices. No game data is included.
The package includes `licenses/RenoDX-MIT.txt` with the upstream MIT license and copyright
notices. Selecting PsychoV30 also displays its RenoDX attribution in the launcher and game menu.

