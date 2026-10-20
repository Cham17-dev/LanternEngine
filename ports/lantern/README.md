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

The in-game menu (Insert / L3+R3), HDR mode PsychoV30, is laid out as a RenoDX mod's, like
bbport's: **Preset** (Off = the game's own look, #1 to #3 = the player's own settings, each saved
in `bbport.ini` as `hdr_preset1..3`; a file from before the presets becomes Preset #1) and
**Settings Mode** (Simple / Intermediate / Advanced) as named sliders, then Tone Mapping (Tone
Mapper Vanilla / PsychoV30 / PsychoV30 (pure), Target gamut BT.2020 / BT.709, Peak Brightness,
Game Brightness, UI Brightness), Color Grading
(Exposure, Highlights, Shadows, Contrast, Saturation; Intermediate: Color Grade Strength, Black
Floor), Effects (Bloom), Reset all, and in Advanced a Debug section (Compare, 16-bit probe, chain
trace). Every setting but Preset has RenoDX's reset button. Peak Brightness defaults to the peak Windows
reports for the display (DXGI MaxLuminance, as RenoDX), Game Brightness to RenoDX's reference
white for it (BT.2100 HLG, at most 203 nits); a value in `bbport.ini` is kept. F9 cycles HDR /
SDR reference / approximate split. Changing into or out of the 16-bit mode requires a restart;
its grading and reference switches are live.

Target gamut (`hdr_gamut` in `bbport.ini`, `bt2020` or `bt709`; live, kept per preset, also on
the launcher's Display page when PsychoV30 is selected) is PsychoV30's gamut_compression_mode, the
color volume it fits the picture into: BT.2020 by default, as RenoDX's HDR mods, or BT.709 (the
earlier builds of this port). It reaches the composite in `bb_hdr2b.z` (the value plus one, 0 not
set), and both PsychoV30 calls of the shadow match use it. The 16-bit chain after the composite
carries gamma-encoded BT.709: the channels a BT.2020 color has below 0 there are clipped right
after PsychoV30, so BT.2020 here is PsychoV30's BT.2020 compression followed by that clip, not a
wide-gamut output.

PsychoV30 (pure) (`hdr_tonemapper=4`; live, kept per preset, also on the launcher's Display
page as "Tone mapper" when PsychoV30 is selected) is PsychoV30 with the same automatic anchor (the
game's mid grey to 0.18), the same starting contrast (the game's slope there) and the same target
gamut, but without the match of the shadows to the game's luminance below mid grey: PsychoV30's
own shadows. It reaches the composite in `bb_hdr2b.w` (1 plus one; 0 not set or 1 for
PsychoV30, which stays exactly as before). The renderer otherwise treats it as PsychoV30 (scene).

With PsychoV30, the Picture section no longer shows Brightness, Contrast, Saturation and Bloom:
they duplicated the HDR grading (Exposure, Contrast, Saturation, Bloom) and both applied. The
engine leaves the game's own values as drawn in this mode (also when set in the game's PC Colour
menu); Hue, Fog, Vignette, ambient occlusion and shadow distance stay.

Paper white defaults to 203 nits in the engine and launcher. Explicit values already saved in
`bbport.ini` are preserved; change an existing 200-nit setting to 203 once if desired.
Paper white and peak brightness have editable numeric values beside their sliders: type a
value and press Enter or leave the field to apply it. Escape restores the current slider value.
The launcher hides Highlight boost and UI colour for PsychoV30 because they do not affect this
mode. HUD brightness remains available. Switching back to Native/Expand restores applicable rows.
Gamma 2.2 is fixed on, and HUD brightness is relative to paper white. The final HDR pass
(`hdr_output.frag`) sets every grading field it shares with bbport's final pass to a neutral
value, so a tone mapper other than PsychoV30 (scene) cannot read uninitialized values.

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

- Compare Target gamut BT.2020 and BT.709 on saturated lights (lanterns, fire, blue lights).
- Compare PsychoV30 and PsychoV30 (pure) in dark interiors and in fog.

## Sources and licenses

Lantern's engine and the combined port are GPL-3.0-or-later. Preserve the upstream package's
licenses when installing the overlay. The PsychoV30 port retains its RenoDX MIT notices; the
shader variant data and integration retain their bbport/shadPS4 notices. No game data is included.
The package includes `licenses/RenoDX-MIT.txt` with the upstream MIT license and copyright
notices. Selecting PsychoV30 also displays its RenoDX attribution in the launcher and game menu.

