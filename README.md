# Lantern Engine + HDR16 (experimental)

HDR for Bloodborne on [Lantern Engine](https://github.com/Awakenchan/LanternEngine) by Awaken.
**HDR16** keeps the game's own light in 16 bits from the scene to the screen and tone maps it to
your display's peak, with a menu laid out like a RenoDX mod. Two tone mappers:

- **Vanilla+** (default), by **souperman9**, from his RenoDX Bloodborne mod: the game's own image,
  with brighter highlights up to your display's peak.
- **PsychoV-30**, RenoDX's tone mapper: softer highlights and its own shadows.

Lantern's own README and credits are in [README-Lantern.md](README-Lantern.md).

> **Unofficial, AI-assisted.** This is not a RenoDX mod and is not affiliated with or endorsed by
> RenoDX or its authors. It reuses RenoDX's MIT-licensed code (PsychoV-30, its grading and
> roll-off) and souperman9's Vanilla+, but the HDR integration (where the scene is captured, the
> game's color table, the output) is this port's own and does not follow RenoDX's mod
> guidelines. It was written largely with an AI coding assistant (Claude) and is tested by one
> player. Please do not report its issues to RenoDX or to souperman9. For RenoDX itself, see
> [clshortfuse/renodx](https://github.com/clshortfuse/renodx).

## Install

1. Download the package (`LanternEngine-HDR16-v<version>-win64.zip`) from this fork's
   [Releases](https://github.com/Cham17-dev/LanternEngine/releases) and extract it into a **new,
   empty folder**. It is a complete package: do not merge it into another Lantern release.
2. Start **Lantern Engine.exe** (or `Bloodborne.exe`, its shortcut). The window title is
   **Lantern Engine + HDR16**. Select your game dump (the folder with `eboot.bin`); game data is
   not included.
3. Turn on HDR in Windows (*Settings > System > Display > Use HDR*), then **HDR output** in the
   launcher's Display page. The HDR source is **HDR16** by default.
4. Your saves stay in your old installation: select your existing user folder in the launcher's
   profile settings, or copy the saves with the game closed (keep a backup). Do not copy an old
   engine, executables or a complete settings file into this package.

The launcher updates itself from this fork's releases. Its settings are in
`%APPDATA%\bbport-launcher-psychov30`; the game's settings are in `lantern.ini`, next to
`Lantern Engine.exe` (Lantern 0.9.37 renamed it: a `bbport.ini` from an earlier version is
copied to it once).

**Updating from an earlier version:** your settings keep the tone mapper saved in them
(PsychoV-30 before 0.9.36 beta 2). To get the new default, pick Vanilla+ in the in-game menu or
the launcher, or use *Reset all*.

## Tips

- **NVIDIA only for now.** On AMD and Intel GPUs, HDR16 does not start its tone mapping and the
  game stays in its SDR look (not tested on them; Native and Expanded are not affected).
- Keep the game's own **Brightness** option (in its in-game settings) at **5**. It still changes
  the whole image, shadows first.
- **Peak Brightness** follows what Windows reports for your display (the launcher's "Peak
  brightness from the display"). Moving the slider sets it by hand; its reset button goes back
  to the display's.
- Changing the HDR source (HDR16, Native, Expanded) needs a game restart; everything in the
  HDR16 menu applies live.
- Frame generation (FSR 3.1 and DLSS) turns itself off in HDR16: it needs an 8-bit display
  image, and HDR16's is 16-bit. Lantern's DLSS frame generation works in HDR with Native and
  Expanded.

## Settings

The in-game menu opens with **Insert** (or L3+R3). Every setting applies live and is saved in
`lantern.ini`. Each preset keeps its own settings, except Preset and Settings Mode themselves.

| Setting | Values (default) | What it does |
|---|---|---|
| Preset | Off, #1 to #3 (#1) | Off is the game's own look in HDR. Each numbered preset keeps its own settings. |
| Settings Mode | Simple, Intermediate, Advanced (Simple) | How many settings the menu shows. |
| **Tone Mapping** | | |
| Tone Mapper | Vanilla, Vanilla+, PsychoV-30 (Vanilla+) | See [Tone mappers](#tone-mappers). |
| Target gamut | BT.2020, BT.709 (BT.2020) | PsychoV-30 only: the color volume it fits the picture into. |
| Gamut Compression *(Intermediate)* | 0 to 1 (1) | PsychoV-30 only: how far colors outside the target gamut are brought into it. |
| Compression *(Intermediate)* | 0 to 2, 0 = auto (1) | PsychoV-30 only: how fast lights reach the peak (higher is brighter and harder). |
| Peak Brightness | 400 to 4000 nits (your display's) | The brightest your display can show: the display's while not set by hand, 1000 if Windows reports none. |
| Game Brightness | 80 to 500 nits (203) | The brightness of the game's white (the launcher's Paper white). |
| UI Brightness | 48 to 500 nits (203) | The HUD and menus' white, whatever Game Brightness. |
| **Color Grading** | | |
| Exposure, Highlights, Shadows, Contrast, Saturation | 1.00, 50, 50, 50, 50 | Vanilla+: RenoDX's grading. PsychoV-30: its own parameters. Neutral at these values. |
| Color Grade Strength *(Intermediate)* | 0 to 100 % (100) | The game's own color grading: 100 is the game's look. |
| Color Table Bridge *(Intermediate)* | Exact, RenoDX (Exact) | How the HDR image goes through the game's color table. Exact keeps the game's grade exactly up to white. |
| Black Floor *(Intermediate)* | 0 to 2 nits (0) | Takes the game's darkest greys down to true black (OLED). |
| **Effects** | | |
| Bloom | 0 to 100 (50) | The glow around lights: 50 is the game's. |
| Reset all | | Every setting of the preset back to its default. |
| Debug *(Advanced)* | | Compare (**F9**: HDR / the game's SDR image), test probes, chain log. |

The launcher's Display page also has Tone mapper, Target gamut, Peak brightness, Paper white and
UI brightness, with the same defaults and reset buttons. In HDR16, the game's own Brightness,
Contrast, Saturation and Bloom in the port's Picture section (and the game's PC Colour menu) are
left as the game draws them: the HDR settings above replace them.

### Tone mappers

- **Vanilla+** (default), by **souperman9**, from his RenoDX Bloodborne mod: the game's own curve
  up to mid grey, continued in a straight line above it, the bloom added, then rolled off smoothly
  to your display's peak (RenoDX's `CInfinityRollOff`). The game's shadows and mid tones, the
  brightest highlights.
- **PsychoV-30**: RenoDX's tone mapper, as RenoDX's mods set it: the game's mid grey and contrast,
  its own shadows, softer highlights than Vanilla+.
- **Vanilla**: the game's own SDR look shown in HDR, without HDR highlights.

## How it works

- **16-bit chain.** The game draws its final image in 8 bits, clipped at white. HDR16 replaces
  the game's final composite: the scene is tone mapped there (Vanilla+ or PsychoV-30), and every
  pass after it (the color table, the upscaler, the display copy) runs in 16-bit float, so the
  light above white reaches the screen. The final pass converts to HDR10 (or scRGB).
- **Gamma.** The chain decodes the game's image with a pure 2.2 power (no sRGB curve), as RenoDX's
  Gamma Correction 2.2: the shadows look as on an SDR TV, not grey.
- **Color table.** The game's color table only covers SDR colors. The HDR image goes through it
  with RenoDX's LUT bridge: colors outside it are brought inside, graded, then taken back, so
  highlights and saturated colors keep their hue and are graded too.
- **Wide colors.** With PsychoV-30 and Target gamut BT.2020, colors outside BT.709 are kept to
  the end and shown in BT.2020, as in RenoDX's mods.
- **HUD.** The HUD is told apart from the scene by comparing the frame with a copy taken just
  before the game draws its HUD; it is shown at UI Brightness, the scene in HDR.

## Build from source

This fork carries reproducible patches over Lantern 0.9.37: `ports/lantern/psychov30-engine.patch`
on Lantern's `engine/` (the engine's full source since 0.9.37) and `psychov30-launcher.patch` on
its launcher (this fork's `launcher/` already holds it). The GitHub workflow copies the pinned
Lantern's `engine/`, applies the patches, builds the engine and launcher, checks the HDR shaders
(against RenoDX's own code for the color bridge) and assembles the package with the official
0.9.37 runtime files (DLLs, models, shader catalogs). Pinned inputs:

- LanternEngine `d901fbd2ea4bea114b37c26a2a9278d1e97f7b46` (0.9.37).
- RenoDX `b6b32f47789ff9d0e640a0e0db6c9421b5397f34` (shader checks).

HDR16 needs a GPU with 176 bytes of Vulkan push constants (checked at startup) and uses the
legacy shader compiler (the engine turns native shaders off in this mode). Its composite reads
the game shader's inputs per vertex (fragment-shader barycentrics), which the recompiler only
emits on NVIDIA's driver; elsewhere the composite is not built ("inputs not interpolated per
vertex" in the log).

## Credits and licenses

- **Lantern Engine** by Awaken, on DarkIzuku's bloodborne_pc; GPL-3.0-or-later. Keep the
  package's licenses when sharing it.
- **Vanilla+** by **souperman9** (RenoDX Bloodborne mod).
- **RenoDX** by Carlos Lopez (ShortFuse) and contributors, MIT (`licenses/RenoDX-MIT.txt`):
  PsychoV-30, the user color grading, `CInfinityRollOff`, the gamut and LUT bridge, ported to GLSL.
- No game data is included.
