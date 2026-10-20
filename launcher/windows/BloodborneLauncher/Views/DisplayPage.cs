using BloodborneLauncher.Core;
using Wpf.Ui.Controls;
using System.Windows;
using System.Windows.Controls;

namespace BloodborneLauncher.Views
{
    /// <summary>Screen, frame rate and the language of the port's in-game menu.</summary>
    public sealed class DisplayPage : LauncherPage
    {
        private readonly ComboBox _monitor = new() { Width = 360, VerticalAlignment = VerticalAlignment.Center };
        private bool _refreshingMonitors;
        private FrameworkElement? _hdrBoostRow;
        private FrameworkElement? _hdrBrightnessRow;
        private FrameworkElement? _hdrColourRow;
        private FrameworkElement? _hdrGamutRow;
        private FrameworkElement? _hdrToneMapperRow;

        public override void OnShown()
        {
            RefreshMonitors();
            RefreshHdrRows();
        }

        private void RefreshHdrRows()
        {
            var mode = Ini.Get("hdr_mode", "native");
            var psycho = mode == "psychov30";
            var native = mode == "native";
            if (_hdrBoostRow != null)
                _hdrBoostRow.Visibility = psycho ? Visibility.Collapsed : Visibility.Visible;
            if (_hdrBrightnessRow != null)
                _hdrBrightnessRow.Visibility = native || psycho ? Visibility.Visible : Visibility.Collapsed;
            if (_hdrColourRow != null)
                _hdrColourRow.Visibility = native ? Visibility.Visible : Visibility.Collapsed;
            if (_hdrGamutRow != null)
                _hdrGamutRow.Visibility = psycho ? Visibility.Visible : Visibility.Collapsed;
            if (_hdrToneMapperRow != null)
                _hdrToneMapperRow.Visibility = psycho ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshMonitors()
        {
            _refreshingMonitors = true;
            try
            {
                var saved = S.Str("display_device");
                _monitor.Items.Clear();
                _monitor.Items.Add(new ComboBoxItem { Content = "Default (Windows primary monitor)", Tag = "" });
                foreach (var monitor in WindowsMonitors.All())
                    _monitor.Items.Add(new ComboBoxItem { Content = monitor.Label, Tag = monitor.Device,
                        ToolTip = $"{monitor.Width} × {monitor.Height}; {monitor.Device}" });
                var selected = _monitor.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, saved, StringComparison.OrdinalIgnoreCase));
                if (selected == null && saved.Length > 0)
                {
                    selected = new ComboBoxItem { Content = $"Disconnected monitor ({saved})", Tag = saved };
                    _monitor.Items.Add(selected);
                }
                _monitor.SelectedItem = selected ?? _monitor.Items[0];
            }
            finally { _refreshingMonitors = false; }
        }

        public DisplayPage() : base("Display", "Resolution, window mode, frame rate and menu language. Applied when the game starts.")
        {
            _monitor.SetResourceReference(StyleProperty, "DefaultComboBoxStyle");
            Group("Language");
            ComboRow(SymbolRegular.LocalLanguage24, "In-game menu language", "The port's settings menu (Insert or L3 + R3)",
                Choices.MenuLanguages, () => Ini.Get("menu_language", "en"), v => Ini.Set("menu_language", v));
            SettingCombo(SymbolRegular.Translate24, "Game language", "The game's own text and voices", Choices.GameLanguages, "language");

            Group("Screen");
            Add(Card(SymbolRegular.Desktop24, "Monitor",
                "The game opens here from the start, including fullscreen and HDR. If this monitor is disconnected, the Windows primary monitor is used. Applied on the next launch.",
                _monitor, out _));
            RefreshMonitors();
            _monitor.SelectionChanged += (_, _) =>
            {
                if (_refreshingMonitors || _monitor.SelectedItem is not ComboBoxItem item) return;
                S.Set("display_device", item.Tag as string ?? "");
                Ws.SaveSoon();
            };
            IniCombo(SymbolRegular.ResizeLarge24, "Output resolution",
                "The upscaler renders below this and fills it in", Choices.OutputResolutions, "output_res");
            IniCombo(SymbolRegular.ArrowSync24, "Live resolution change",
                "Change the resolution in the game menu without restarting. Off is faster; turn on only if you switch resolutions in game", Choices.LiveResolution, "live_resolution");
            IniToggle(SymbolRegular.FullScreenMaximize24, "Fullscreen", "Borderless, so trophy notifications show on top", "fullscreen");
            ToggleRow(SymbolRegular.RatioOneToOne24, "Widescreen", "21:9 and 32:9 outputs (3440 × 1440, 5120 × 1440) with the right field of view",
                () => Ini.Get("widescreen", "1") != "0", v => Ini.Set("widescreen", v ? "1" : "0"));
            SettingCombo(SymbolRegular.Image24, "Presentation mode", null, Choices.PresentModes, "present_mode");

            Group("HDR");
            var hdrHint = WindowsHdr.AnyOn()
                ? "The game's picture on an HDR screen, with brighter highlights. Applied when the game starts"
                : WindowsHdr.AnySupported()
                    ? "Turn on Use HDR in Windows' display settings first (Settings › System › Display)"
                    : "This display shows no HDR";
            IniToggle(SymbolRegular.Hdr24, "HDR output", hdrHint, "hdr");
            ComboRow(SymbolRegular.Lightbulb24, "HDR source",
                "PsychoV30: HDR tone mapping with dark tones matched to the game's SDR rendering. Native: highlights derived from the game's HDR scene. Expanded: bright SDR pixels expanded towards the peak. Entering or leaving PsychoV30 requires restarting the game.",
                Choices.HdrMode, () => Ini.Get("hdr_mode", "native"), v => Ini.Set("hdr_mode", v), changed: RefreshHdrRows);
            SliderRow(SymbolRegular.BrightnessHigh24, "Paper white", "How bright the game's white is, in nits. Default: 203. Range: 10–500",
                10, 500, 1, () => ParseDouble(Ini.Get("hdr_paper_white", "203"), 203),
                v => Ini.Set("hdr_paper_white", v.ToString("0", System.Globalization.CultureInfo.InvariantCulture)),
                format: "0' nits'", valueWidth: 82, inputUnit: "nits");
            // Default: the peak Windows reports for the chosen display (in HDR), as the engine's.
            double DetectedPeak() => DisplayPeak.Default(S.Str("display_device"));
            var detected = DisplayPeak.Nits(S.Str("display_device"));
            var peakHint = detected > 0
                ? $"Your display's peak brightness, in nits. Windows reports {detected:0} nits for it: the default. Range: 10–4000"
                : "Your display's peak brightness, in nits. Windows reports none (is Windows HDR on?): 1000 by default. Range: 10–4000";
            var peak = SliderRow(SymbolRegular.WeatherSunny24, "Peak brightness", peakHint,
                10, 4000, 1, () => ParseDouble(Ini.Get("hdr_peak", ""), DetectedPeak()),
                v => Ini.Set("hdr_peak", v.ToString("0", System.Globalization.CultureInfo.InvariantCulture)),
                format: "0' nits'", valueWidth: 82, inputUnit: "nits");
            if (peak.Parent is Panel peakRow)
                peakRow.Children.Add(IconButton(SymbolRegular.ArrowCounterclockwise24,
                    "Reset to the peak Windows reports for this display (1000 when it reports none)",
                    (_, _) => peak.Value = Math.Clamp(DetectedPeak(), peak.Minimum, peak.Maximum)));
            ComboRow(SymbolRegular.Sparkle24, "Highlight boost", "Off keeps the game's own look; higher makes lamps, fire and the sky brighter",
                Choices.HdrBoost, () => Ini.Get("hdr_boost", "0.25"), v => Ini.Set("hdr_boost", v));
            _hdrBoostRow = (FrameworkElement)Body.Children[Body.Children.Count - 1];
            ComboRow(SymbolRegular.TextFont24, "UI brightness", "Native and PsychoV30: HUD brightness relative to paper white. Paper white is 1x; Brighter is 1.25x; Much brighter is 1.5x.",
                Choices.HdrUiBrightness, () => Ini.Get("hdr_ui_brightness", "1"), v => Ini.Set("hdr_ui_brightness", v));
            _hdrBrightnessRow = (FrameworkElement)Body.Children[Body.Children.Count - 1];
            ComboRow(SymbolRegular.Color24, "UI colour", "Native source only: Vivid shows the HUD and menus with the colours a wide-gamut monitor gives SDR; Exact keeps sRGB's",
                Choices.HdrUiColor, () => Ini.Get("hdr_ui_color", "1"), v => Ini.Set("hdr_ui_color", v));
            _hdrColourRow = (FrameworkElement)Body.Children[Body.Children.Count - 1];
            ComboRow(SymbolRegular.Lightbulb24, "Tone mapper",
                "PsychoV30 only. PsychoV30 Matched: its shadows matched to the game's below mid grey. PsychoV30 Pure: PsychoV30's own shadows; compare them in dark interiors and fog. Vanilla: the game's own look. Also in the in-game menu, live",
                Choices.HdrToneMapper, () => Ini.Get("hdr_tonemapper", "3"), v => Ini.Set("hdr_tonemapper", v));
            _hdrToneMapperRow = (FrameworkElement)Body.Children[Body.Children.Count - 1];
            ComboRow(SymbolRegular.Color24, "Target gamut",
                "PsychoV30 only: the color volume it fits the picture into. BT.2020 is RenoDX's HDR default; compare with BT.709 on saturated lights (lanterns, fire, blue lights). Also in the in-game menu, live",
                Choices.HdrGamut, () => Ini.Get("hdr_gamut", "bt2020"), v => Ini.Set("hdr_gamut", v));
            _hdrGamutRow = (FrameworkElement)Body.Children[Body.Children.Count - 1];
            RefreshHdrRows();

            Group("Frame rate");
            SettingCombo(SymbolRegular.Gauge24, "Frame rate", "30, 60, 90, 120 and 144 are the game's frame-rate patches for that rate; Uncapped uses the community uncapped patch", Choices.FpsModes, "fps_mode");
        }
    }
}
