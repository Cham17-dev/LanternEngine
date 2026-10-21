namespace BloodborneLauncher.Core
{
    /// <summary>A drop-down entry. Note is shown under the row while this entry is selected.</summary>
    public sealed record Choice(string Label, string Value, string? Note = null);

    public sealed record Effect(string Key, string Title, bool Default, string? Hint = null);

    /// <summary>Every list the launcher offers, in English, with the values bbport expects.</summary>
    public static class Choices
    {
        public static readonly Choice[] Upscalers =
        {
            new("FSR 4", "fsr4"),
            new("FSR 4.1.1", "fsr411"),
            new("FSR 3", "fsr3"),
            new("DLSS (NVIDIA RTX)", "dlss"),
            new("TAA (native anti-aliasing)", "taa"),
            new("Off", "off"),
        };

        /// <summary>frame_gen in bbport.ini: a generated frame between the game's own (the engine's
        /// vk_frame_generation.h). Off by default.</summary>
        public static readonly Choice[] FrameGeneration =
        {
            new("Off", "off"),
            new("FSR 3.1 (2x)", "fsr3", "Needs an upscaler. Not in menus, movies or with HDR output"),
        };

        public static readonly Choice[] Presets =
        {
            new("Native AA (no upscaling; DLAA with DLSS)", "0"),
            new("Quality (x1.5)", "1"),
            new("Balanced (x1.7)", "2"),
            new("Performance (x2)", "3"),
            new("Ultra Performance (x3)", "4"),
        };

        public static readonly Choice[] OutputResolutions =
        {
            new("1280 × 720 (Steam Deck)", "1280x720"),
            new("1600 × 900", "1600x900"),
            new("1920 × 1080", "1920x1080"),
            new("2560 × 1440", "2560x1440"),
            new("3840 × 2160", "3840x2160"),
            // Ultrawide: the engine widens the camera to the screen (Widescreen, on by default).
            new("2560 × 1080 (21:9)", "2560x1080"),
            new("3440 × 1440 (21:9)", "3440x1440"),
            new("3840 × 1600 (24:10)", "3840x1600"),
            new("5120 × 2160 (21:9)", "5120x2160"),
            new("3840 × 1080 (32:9)", "3840x1080"),
            new("5120 × 1440 (32:9)", "5120x1440"),
            new("7680 × 2160 (32:9)", "7680x2160"),
            // Narrower than 16:9: the camera and UI made taller to fill it (Widescreen).
            new("1280 × 1024 (5:4)", "1280x1024"),
            new("1680 × 1050 (16:10)", "1680x1050"),
            new("1920 × 1200 (16:10)", "1920x1200"),
            new("2560 × 1600 (16:10)", "2560x1600"),
            // Any other display: its own size, the camera and UI widened to fill it.
            new("Display (auto)", "auto", "Your display's own resolution, whatever its shape: the picture fills the screen"),
            new("Custom", Custom, "Any width and height: set them below"),
        };

        /// <summary>The Output resolution list's entry for a size it does not name.</summary>
        public const string Custom = "custom";

        /// <summary>A size the engine takes as a custom output: 640 × 360 to 16384 × 8640, even.</summary>
        public static bool ValidCustomSize(int w, int h) => w >= 640 && h >= 360 && w <= 16384 && h <= 8640;

        /// <summary>The size an output_res value names; "auto" is the display's own (1920 × 1080
        /// when it is unknown), as the engine reads it at start (scripts/run_windows.py).</summary>
        public static (int W, int H) OutputSize(string value)
        {
            if (value == "auto")
            {
                var hw = Hardware.Detect();
                return hw.ScreenWidth >= 640 && hw.ScreenHeight >= 360
                    ? (hw.ScreenWidth & ~1, hw.ScreenHeight & ~1)
                    : (1920, 1080);
            }
            var parts = value.Split('x');
            return parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h)
                ? (w, h)
                : (1920, 1080);
        }

        // Chromatic aberration, depth of field, motion blur and the game's own anti-aliasing start off:
        // the picture is clearer without them, and the upscalers anti-alias (players' advice).
        public static readonly Effect[] Effects =
        {
            new("effect_chromatic_aberration", "Chromatic aberration", false),
            new("effect_dof", "Depth of field", false),
            new("effect_motion_blur", "Motion blur", false),
            new("effect_ssao", "Ambient occlusion (SSAO)", true),
            new("effect_game_aa", "Game's own anti-aliasing", false),
            new("effect_dynamic_shadows", "Shadows from dynamic lights", true),
            new("effect_ssr", "Screen-space reflections", false, "Not in the original game"),
            new("skip_intro", "Skip intro videos", false),
            new("debug_camera", "Free camera", false, "Toggle with Cross + L3 / Space + Z"),
            new("debug_menu", "Debug menu", false,
                "Left touchpad / Tab. Needs the adhoc folder from Nexus mod #253 (adhoc/font) in the game's dvdroot_ps4 or as a mod; without it the patch is not applied"),
        };

        public static readonly Choice[] CameraTuning =
        {
            new("Original (the game's camera)", "original", "As the game has it, at any frame rate"),
            new("FPS++ retune", "fps++", "Kyo's 60 FPS++ camera: further back and slower to follow"),
        };

        public static readonly Choice[] ModelLod =
        {
            new("As in the game", "0"),
            new("Highest (-2)", "-2"),
            new("Lower (1)", "1"),
            new("Lowest (2)", "2"),
        };

        public static readonly Choice[] FpsModes =
        {
            new("60 FPS", "60"),
            new("90 FPS", "90", "Includes the sprint fix for high frame rates"),
            new("120 FPS", "120", "The 90 FPS patch made for 120"),
            new("144 FPS", "144", "The 90 FPS patch made for 144"),
            new("Uncapped", "uncap", "The community uncapped patch, no limit. It can freeze at the end of a boss intro (Vicar Amelia's); 120 and 144 use another patch"),
            new("30 FPS (as on PS4)", "30"),
        };

        /// <summary>The DLSS model (BB_DLSS_PRESET): NVIDIA's render presets.</summary>
        public static readonly Choice[] DlssModels =
        {
            new("Transformer (preset K)", "k", "The newer model: steadier thin and moving objects (a weapon on the back, railings)"),
            new("CNN (preset E)", "e", "The older model: a little faster on RTX 20 and 30 cards, softer and more ghosting in motion"),
            new("The DLL's default", "default", "Whatever the included DLSS version picks for each quality mode"),
        };

        /// <summary>What opens the port's in-game settings menu (BB_OVERLAY_KEYS).</summary>
        public static readonly Choice[] OverlayKeys =
        {
            new("Insert or L3 + R3", "both", "The keyboard's Insert key, or both sticks pressed on a controller"),
            new("Insert only", "insert", "Pressing both sticks in a fight no longer opens it"),
            new("Off", "off", "The menu never opens; settings stay in the launcher and the game's PC menus"),
        };

        /// <summary>The port's in-game settings menu (Insert / L3+R3).</summary>
        public static readonly Choice[] MenuLanguages =
        {
            new("English", "en"),
            new("Español", "es"),
            new("Português (Brasil)", "pt_BR"),
            new("Français", "fr"),
            new("Deutsch", "de"),
            new("Italiano", "it"),
            new("Polski", "pl"),
            new("Türkçe", "tr"),
            new("Русский", "ru"),
        };

        public static readonly Choice[] PresentModes =
        {
            new("Mailbox", "Mailbox", "Low latency without tearing"),
            new("FIFO (VSync)", "Fifo", "Classic vertical sync"),
            new("FIFO Relaxed", "FifoRelaxed", "VSync that tears instead of stuttering when a frame is late"),
            new("Immediate", "Immediate", "Lowest latency; may tear"),
        };

        public static readonly Choice[] DrawPipe =
        {
            new("Auto", "", "On with 8 or more CPU threads"),
            new("On", "1"),
            new("Off", "0", "More stable, but slower"),
        };

        public static readonly Choice[] GameLanguages =
        {
            new("English", "1"),
            new("Russian", "8"),
            new("Japanese", "0"),
            new("French", "2"),
            new("Spanish", "3"),
            new("German", "4"),
            new("Italian", "5"),
        };

        public static readonly Choice[] HdrPaperWhite =
        {
            new("100 nits", "100"), new("150 nits", "150"), new("203 nits (default)", "203"),
            new("250 nits", "250"), new("300 nits", "300"), new("400 nits", "400"),
        };

        public static readonly Choice[] HdrPeak =
        {
            new("400 nits", "400"), new("600 nits", "600"), new("800 nits", "800"), new("1000 nits (default)", "1000"),
            new("1500 nits", "1500"), new("2000 nits", "2000"), new("4000 nits", "4000"),
        };

        /// <summary>button_prompts: the game's button icons as keys while the keyboard or mouse is in use, always, or never.</summary>
        public static readonly Choice[] ButtonPrompts =
        {
            new("Automatic (default)", "auto"), new("Keyboard", "keyboard"), new("Controller", "controller"),
        };

        /// <summary>pad_icons: the game's controller icons as they are (PlayStation's) or as an Xbox controller's buttons; automatic follows the controller in use.</summary>
        public static readonly Choice[] DiscordStatus =
        {
            new("Off", "off"), new("Lantern Engine", "lantern"), new("Custom", "custom"),
        };

        public static readonly Choice[] PadIcons =
        {
            new("Automatic (default)", "auto"), new("PlayStation", "playstation"), new("Xbox", "xbox"),
        };

        public static readonly Choice[] HdrMode =
        {
            new("Native", "native"), new("Expanded", "expand"),
            new("PsychoV30 (16-bit, restart)", "psychov30",
                "PsychoV30 — RenoDX · © 2026 Carlos Lopez · MIT License. GLSL adaptation for bbport. github.com/clshortfuse/renodx"),
        };

        /// <summary>hdr_tonemapper with PsychoV30 (bbport_settings.h): 4 PsychoV-30 (as RenoDX's mods), 3 Hybrid (its shadows matched to the game's), 2 Vanilla.</summary>
        public static readonly Choice[] HdrToneMapper =
        {
            // The segmented buttons show the label up to " (".
            new("PsychoV-30 (default)", "4"), new("Hybrid (the game's shadows)", "3"), new("Vanilla (the game's own look)", "2"),
        };

        /// <summary>hdr_gamut: the color volume PsychoV30 fits the picture into (its gamut_compression_mode).</summary>
        public static readonly Choice[] HdrGamut =
        {
            new("BT.2020 (default)", "bt2020"), new("BT.709", "bt709"),
        };

        public static readonly Choice[] HdrBoost =
        {
            new("Off", "0"), new("Low (default)", "0.25"), new("Medium", "0.5"), new("High", "1"),
        };

        public static readonly Choice[] HdrUiBrightness =
        {
            new("Dimmer", "0.75"), new("Paper white (default)", "1"), new("Brighter", "1.25"), new("Much brighter", "1.5"),
        };

        public static readonly Choice[] HdrUiColor =
        {
            new("Exact (sRGB)", "0"), new("In between", "0.5"), new("Vivid (default)", "1"),
        };

        public static readonly Choice[] OnlineServers =
        {
            new("The Hunter's Dream", "huntersdream"),
        };

        public static readonly Choice[] SignTimeout =
        {
            new("30 seconds (default)", "30"), new("60 seconds", "60"), new("The game's own (180 seconds)", "0"),
        };

        public static readonly Choice[] LiveResolution =
        {
            new("Auto (by GPU)", "auto"),
            new("Off (faster)", "0"),
            new("On", "1"),
        };

        public static readonly Choice[] Readbacks =
        {
            new("Relaxed", "", "Default"),
            new("Off", "0"),
            new("Precise", "2"),
        };

        public static readonly Choice[] SoundOutput =
        {
            new("WASAPI", "", "Default"),
            new("DirectSound", "directsound"),
            new("DirectSound, larger buffer", "directsound_large"),
        };

        public static readonly Choice[] LaunchMethods =
        {
            new("Lantern Engine", "windows", "Prepares mods and patches, then starts the engine (out\\bb-probe.exe)"),
            new("Custom command", "custom", "Runs a program of your choice with the settings as environment variables"),
        };

        public static int IndexOf(IReadOnlyList<Choice> choices, string value)
        {
            for (var i = 0; i < choices.Count; i++)
                if (choices[i].Value == value)
                    return i;
            // A number written another way (the engine saves "1.00" for "1"): the same choice.
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                for (var i = 0; i < choices.Count; i++)
                    if (double.TryParse(choices[i].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var option) &&
                        Math.Abs(option - number) < 1e-6)
                        return i;
            return 0;
        }
    }
}
