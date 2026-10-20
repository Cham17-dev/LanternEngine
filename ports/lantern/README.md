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

1. Make a separate copy of the matching Lantern Windows installation. Keep the original.
2. Extract the `lantern-psychov30-overlay` artifact into that separate copy, retaining `out/`.
3. Start the supplied `Bloodborne Launcher.exe`. It uses its own launcher settings folder
   (`bbport-launcher-psychov30`) and has update checks off by default. Select the copied engine
   folder and your existing game dump. Do not update this experimental engine from the upstream
   launcher; that would replace it with the stock engine.
4. Enable Windows HDR and HDR output. Select `PsychoV30 (16-bit, restart)` as HDR source, then
   restart the game. The in-game HDR page also offers this mode. Paper white and peak use
   Lantern's existing settings. Do not replace your complete INI with the example INI; the
   example lists only HDR settings.
5. Compare native resolution/upscaler off, DLAA, then DLSS Quality. Keep sharpening off for the
   initial comparison. Use the same camera, peak, paper white and grading settings.

The in-game mode provides Vanilla and PsychoV30, Exposure, Highlights, Shadows, Contrast,
Saturation, Bloom, Color Grade Strength, Black Floor and HUD brightness. Neutral values are
listed there. F9 cycles HDR / SDR reference / approximate split. Changing into or out of the
16-bit mode requires a restart; its grading and reference switches are live.

Paper white defaults to 203 nits in the engine and launcher. Explicit values already saved in
`bbport.ini` are preserved; change an existing 200-nit setting to 203 once if desired.
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
PsychoV30 uses a separate `-psychov30-v1` shader/driver cache. Cached composites rebuild their
HDR companion when loaded, so restarting does not drop the HDR pass.

## Validation still required

Test dark interiors, fog, fire, saturated blue lights, menus, loading transitions and camera
movement. Inspect HUD brightness, clipping, color order, borders and temporal stability. Capture
repeated JXR pairs for comparison with the reference mod; those captures are not in this repo.
No FPS or visual-quality improvement is claimed before those measurements.

## Sources and licenses

Lantern's engine and the combined port are GPL-3.0-or-later. Preserve the upstream package's
licenses when installing the overlay. The PsychoV30 port retains its RenoDX MIT notices; the
shader variant data and integration retain their bbport/shadPS4 notices. No game data is included.

