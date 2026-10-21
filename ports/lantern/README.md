# Lantern Engine + PsychoV30 (experimental)

HDR for Bloodborne on Lantern Engine, with RenoDX's PsychoV30 tone mapper and a menu laid out
like a RenoDX mod. This branch carries a reproducible patch over Lantern, not a copy of its
engine. A successful build is not an in-game validation: see [Validation](#validation-still-required).

> **Unofficial, AI-assisted.** This is not a RenoDX mod and is not affiliated with or endorsed by
> RenoDX or its authors. It reuses RenoDX's MIT-licensed PsychoV30 tone mapper, ported to GLSL,
> but the HDR integration (where the scene is captured, the game's color table, the output) is
> this port's own and does not follow RenoDX's mod guidelines. It was written largely with an AI
> coding assistant (Claude) and is tested by one player. Please do not report its issues to
> RenoDX. For RenoDX itself, see [clshortfuse/renodx](https://github.com/clshortfuse/renodx).

## Install

1. Download `lantern-psychov30-package` and extract it into a **new, empty folder**. It is a
   complete package: do not merge it into another Lantern release or a previous overlay.
2. Start **Lantern Engine.exe** (or `Bloodborne.exe`, its shortcut). The window title is
   **Lantern Engine + PsychoV30**. Select your game dump (the folder with `eboot.bin`); game
   data is not included.
3. Turn Windows HDR on, then HDR output in the launcher. The HDR source defaults to
   **PsychoV30 (16-bit, restart)**. Restart the game after changing the HDR source.
4. Saves stay in your old installation. Select your existing user/save folder in the
   launcher's profile settings, or copy the saves with the game closed (keep a backup). Do not
   copy an old engine, executables or a complete INI into this package.

The launcher keeps its settings in `%APPDATA%\bbport-launcher-psychov30`, and its updates point
at this fork. Binaries and resources are under `engine/`; player data and `bbport.ini` are at
the root.

## Settings

The in-game menu opens with **Insert** (or L3+R3). Every setting is live and saved in
`bbport.ini`; each preset keeps its own, except Preset and Settings Mode themselves. The Debug
tools last for the session only.

| Setting | Values (default) | What it does |
|---|---|---|
| Preset | Off, #1 to #3 (#1) | Off is the game's own look. Each numbered preset keeps its own settings. |
| Settings Mode | Simple, Intermediate, Advanced (Simple) | How many settings the menu shows. |
| **Tone Mapping** | | |
| Tone Mapper | PsychoV-30, Hybrid, Vanilla (PsychoV-30) | See [Tone mappers](#tone-mappers). |
| Target gamut | BT.2020, BT.709 (BT.2020) | The color volume PsychoV30 fits the picture into. |
| Gamut Compression *(Intermediate)* | 0 to 1 (1) | How far colors outside the target gamut are brought into it. |
| Compression *(Intermediate)* | 0 to 2, 0 = auto (1) | How fast lights reach the peak: higher is brighter and harder, lower softer. |
| Peak Brightness | 400 to 4000 nits (your display's) | The peak Windows reports for the display, 1000 if it reports none. |
| Game Brightness | 80 to 500 nits (203) | The brightness of the game's white (the launcher's Paper white). |
| UI Brightness | ratio (1.00 x) | HUD and menus, times Game Brightness. |
| **Color Grading** | | |
| Exposure, Highlights, Shadows, Contrast, Saturation | (1.00, 50, 50, 50, 50) | PsychoV30's own grading, as in RenoDX's mods. |
| Color Grade Strength *(Intermediate)* | 0 to 100 % (100) | The game's own color table: 100 is the game's look. |
| Black Floor *(Intermediate)* | 0 to 2 nits (0) | Takes the game's darkest grey down to true black (OLED). |
| **Effects** | | |
| Bloom | 0 to 100 (50) | The glow around lights: 50 is the game's. |
| Reset all | | Every setting of the preset back to its default. |
| Debug *(Advanced)* | | Compare (F9: HDR / SDR / split), 16-bit chain probes, chain log. |

The launcher's Display page shows Tone mapper, Target gamut and Peak brightness, with the same
defaults; its Peak brightness and Paper white follow the monitor chosen there and each have a
reset button.
In PsychoV30 mode, the game's own Brightness, Contrast, Saturation and Bloom (Picture section, and
the game's PC Colour menu) are left as the game draws them: the HDR grading above replaces them.
Hue, Fog, Vignette, ambient occlusion and shadow distance stay. Gamma 2.2 is always on.

### Tone mappers

All PsychoV30 modes replace the game's tone curve on the scene itself, before the game's color
table, and bring the lights up to your display's peak.

- **PsychoV-30** (`hdr_tonemapper=4`, default): PsychoV30 as RenoDX's mods set it (Order of the
  Sinking Star's Midgray Reference and Slope Reference): the game's mid grey kept at 0.18, the
  game's contrast there, PsychoV30's own shadows.
- **Hybrid** (`hdr_tonemapper=3`): the same, with the shadows below mid grey matched to the
  game's. An addition of this port; compare the two in dark interiors and fog.
- **Vanilla** (`hdr_tonemapper=2`): the game's own look in HDR10, without HDR highlights.

## Technical notes

### Build

Pinned inputs:

- LanternEngine `69018af24313407684cea267b107d2fb3fe86d5f` (0.9.34 beta 9.8).
- Its starting point, DarkIzuku/bloodborne_pc `8ecba9a4abdd54747e31d7fee54982e422b5f8e2`
  (`v0.4.0-darkizuku-preview.1`).
- The mode 5 / PsychoV30 implementation from Cham17-dev/bloodborne_pc `hdr` at `0dce9f4`.

`prepare.py` rebuilds the baseline (DarkIzuku plus Lantern's `engine/bbhost-changes.patch`),
then applies `psychov30-engine.patch`; the launcher is built from `launcher/`
(`psychov30-launcher.patch` is its diff against Lantern). The Windows workflow builds the engine
and launcher and packages them; its manual `baseline_check` option compiles stock Lantern first.
The package reuses the official beta 9.8 runtime resources, DLLs, notices and defaults with
SHA-256 checks. The game starts through `bb-start.exe` (built here, as Lantern 9.8); the embedded
Python 3.12.10 and the preparation scripts stay as a fallback. CI checks both
folder layouts and runs the shipped Python through imports and game detection.

### Rendering

- **16-bit chain.** After the composite, the game's chain runs in RGBA16F (gamma 2.2, BT.709),
  so values above white survive to the final pass (`hdr_output.frag`), which converts to HDR10 or
  scRGB. Native, Expand and mode 4 are unchanged.
- **PsychoV30** runs in the composite (`bb_composite_hdr.frag`) on scene-linear color, anchored
  at the game's mid grey with the game's slope there as contrast. RenoDX takes that slope at the
  curve's inflection; for Bloodborne's curve it gives 1.1369 instead of 1.1656 in interiors
  (inflection 0.258, mid grey 0.195) and 1.0452 instead of 1.0459 outside.
- **Color table.** Above white, the game's table is applied with RenoDX's max-channel
  normalization, so highlights keep their hue.
- **BT.2020.** A color outside BT.709 has BT.709 channels below 0. They are kept, signed, through
  every pass to the final one, which converts to BT.2020 and only then drops what is still
  negative, as RenoDX's mods:
  - composite: no clip after PsychoV30 (BT.709 still clips), signed gamma encoding;
  - color grading: the table grades the positive part, the negative part is added back ungraded;
  - display copy: the brightness table is continued point-symmetric below 0;
  - temporal reconstruction (DLSS / FSR / TAA, `upscale_transfer.glsl`): decoded to linear
    BT.2020, where those colors are positive, and encoded back (exact inverses inside BT.2020,
    checked by `docs/hdr-tools/upscale_transfer_check`);
  - final pass: the negative part is decoded with its sign and added after Black Floor.
  Debug probe 6 shows magenta where a channel reaches the final pass below 0. At neutral
  settings the game produces almost no colors outside BT.709; they appear with more Saturation.
- **Frame generation.** Lantern's FSR 3.1 frame generation turns itself off while HDR output is
  on (8-bit display buffers only), and so with PsychoV30.
- **Accuracy.** The GLSL PsychoV30 matches RenoDX's test30 within 5.3e-4 relative with every
  setting this port uses (`docs/hdr-tools/psychov30_check`).

### Engine details

- Lantern's push constants are kept; mode 5 adds its HDR parameters after the original 128
  bytes and needs **176 bytes** of Vulkan push constants (checked at startup). In the composite,
  `bb_hdr2a` holds peak, exposure, highlights and shadows, `bb_hdr2b` contrast, saturation, the
  gamut with the tone mapper, and the two compressions (see the shader for the encoding).
- Lantern's point-sampler fallback to an 8-bit guest image is disabled in mode 5, so HDR values
  are not clipped; this may change some effects at reduced presets. Composite pipelines with an
  HDR companion compile together.
- PsychoV30 uses its own shader cache, `CUSA03173-psychov30-<tag>` in `%APPDATA%\shadPS4\cache`.
  The tag is a hash of the replacement shaders, so a build with other shaders starts a new cache
  instead of loading stale ones (a stale cache gave a black screen in the first build).
- The display copy reads its brightness table as a 1D array: Lantern's recompiler keeps 1D
  images, unlike shadPS4 0.20.1. Reading it as a 2D array gave 0 and a black frame.
- Peak detection uses DXGI (`IDXGIOutput6::GetDesc1` MaxLuminance), in the engine for the game's
  display and in the launcher for the monitor chosen there. A value saved in `bbport.ini` wins.
- With a file from before the presets, its settings become Preset #1.

## Validation still required

- Dark interiors, fog, fire, saturated blue lights, menus, loading transitions and camera
  movement: HUD brightness, clipping, color order, borders and temporal stability.
- PsychoV-30 against Hybrid in dark interiors and fog.
- Compression 0.5 / 2 / auto on lanterns and fire; Gamut Compression 0 with Saturation 100.
- Target gamut BT.2020 against BT.709 on saturated lights, with probe 6, with and without an
  upscaler.
- Native resolution / upscaler off, DLAA, then DLSS Quality, sharpening off, with the same camera
  and settings. Repeated JXR captures against the reference mod (not in this repo).

No FPS or image-quality improvement is claimed before those measurements.

## Sources and licenses

Lantern's engine and the combined port are GPL-3.0-or-later; keep the upstream package's licenses
when installing. The PsychoV30 port keeps its RenoDX MIT notices
(`licenses/RenoDX-MIT.txt`); the shader variants and integration keep their bbport / shadPS4
notices. No game data is included. Selecting PsychoV30 shows its RenoDX attribution in the
launcher and the game menu.
