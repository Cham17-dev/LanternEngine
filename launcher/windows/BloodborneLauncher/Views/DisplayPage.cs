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
                _monitor.Items.Add(new ComboBoxItem { Content = L.T("Default (Windows primary monitor)"), Tag = "" });
                foreach (var monitor in WindowsMonitors.All())
                    _monitor.Items.Add(new ComboBoxItem { Content = monitor.Label, Tag = monitor.Device,
                        ToolTip = $"{monitor.Width} × {monitor.Height}; {monitor.Device}" });
                var selected = _monitor.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, saved, StringComparison.OrdinalIgnoreCase));
                if (selected == null && saved.Length > 0)
                {
                    selected = new ComboBoxItem { Content = L.F($"Disconnected monitor ({saved})"), Tag = saved };
                    _monitor.Items.Add(selected);
                }
                _monitor.SelectedItem = selected ?? _monitor.Items[0];
            }
            finally { _refreshingMonitors = false; }
        }

        /// <summary>Output resolution: a named size, the display's own, or Custom with its width and
        /// height (the engine takes any even WxH from 640 × 360 to 16384 × 8640).</summary>
        private void OutputResolutionRows()
        {
            static bool Named(string v) => v == "auto" || Choices.OutputResolutions.Any(c => c.Value == v && c.Value != Choices.Custom);
            var widthBox = new Wpf.Ui.Controls.TextBox { Width = 90, PlaceholderText = L.T("Width"), PlaceholderEnabled = true };
            var heightBox = new Wpf.Ui.Controls.TextBox { Width = 90, PlaceholderText = L.T("Height"), PlaceholderEnabled = true };
            var (w0, h0) = Choices.OutputSize(Ini.Get("output_res", "1920x1080"));
            widthBox.Text = w0.ToString();
            heightBox.Text = h0.ToString();
            SettingRow customRow = null!;
            System.Windows.Controls.TextBlock customNote = null!;

            void ApplyCustom()
            {
                if (!int.TryParse(widthBox.Text.Trim(), out var w) || !int.TryParse(heightBox.Text.Trim(), out var h) ||
                    !Choices.ValidCustomSize(w, h))
                {
                    SetHint(customNote, L.T("Width 640 to 16384, height 360 to 8640"));
                    return;
                }
                w &= ~1;
                h &= ~1;
                widthBox.Text = w.ToString();
                heightBox.Text = h.ToString();
                Ini.Set("output_res", $"{w}x{h}");
                SetHint(customNote, null);
                Ws.SaveSoon();
            }

            ComboRow(SymbolRegular.ResizeLarge24, L.T("Output resolution"), L.T("The upscaler renders below this and fills it in"),
                Choices.OutputResolutions,
                () => { var v = Ini.Get("output_res", "1920x1080"); return Named(v) ? v : Choices.Custom; },
                v =>
                {
                    customRow.Visibility = v == Choices.Custom ? Visibility.Visible : Visibility.Collapsed;
                    if (v == Choices.Custom) ApplyCustom();
                    else Ini.Set("output_res", v);
                });

            foreach (var box in new[] { widthBox, heightBox })
            {
                box.LostFocus += (_, _) => ApplyCustom();
                box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) ApplyCustom(); };
            }
            customRow = Add(Card(SymbolRegular.Ruler24, L.T("Custom resolution"),
                L.T("Your own output size, any shape (5:4, 16:10, 32:9...): the camera and UI follow it with Widescreen on"),
                Row(widthBox, new System.Windows.Controls.TextBlock { Text = "×", Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }, heightBox),
                out customNote));
            customRow.Visibility = Named(Ini.Get("output_res", "1920x1080")) ? Visibility.Collapsed : Visibility.Visible;
        }

        public DisplayPage() : base(L.T("Display"), L.T("Resolution, window mode, frame rate and menu language. Applied when the game starts."))
        {
            _monitor.SetResourceReference(StyleProperty, "DefaultComboBoxStyle");
            ComboSearch.Attach(_monitor);
            Group(L.T("Screen"));
            Add(Card(SymbolRegular.Desktop24, L.T("Monitor"),
                L.T("The game opens here from the start, including fullscreen and HDR. If this monitor is disconnected, the Windows primary monitor is used. Applied on the next launch."),
                _monitor, out _));
            RefreshMonitors();
            _monitor.SelectionChanged += (_, _) =>
            {
                if (_refreshingMonitors || _monitor.SelectedItem is not ComboBoxItem item) return;
                S.Set("display_device", item.Tag as string ?? "");
                Ws.SaveSoon();
            };
            OutputResolutionRows();
            SettingToggle(SymbolRegular.TextFont24, L.T("Sharp UI"),
                L.T("Menus, HUD and text drawn at the output resolution instead of 1080p stretched up: much sharper at 1440p and 4K. New: on by default in betas while it is tested. Applied on the next launch"),
                "exp_sharp_ui");
            IniToggle(SymbolRegular.FullScreenMaximize24, L.T("Fullscreen"), L.T("The game covers the whole screen"), "fullscreen");
            SettingToggle(SymbolRegular.WindowNew24, L.T("Borderless fullscreen"),
                L.T("Keeps fullscreen a borderless window, so trophy notifications and other windows show on top. Off: the driver may switch to exclusive fullscreen (lowest latency, G-SYNC fullscreen-only mode), which hides them"),
                "borderless_fullscreen");
            ToggleRow(SymbolRegular.RatioOneToOne24, L.T("Widescreen"), L.T("Outputs that are not 16:9 (21:9, 32:9, 16:10, 5:4) with the right field of view and the UI kept in shape"),
                () => Ini.Get("widescreen", "1") != "0", v => Ini.Set("widescreen", v ? "1" : "0"));
            var screenMore = Advanced();
            IniCombo(SymbolRegular.ArrowSync24, L.T("Live resolution change"),
                L.T("Change the resolution in the game menu without restarting. Off is faster; turn on only if you switch resolutions in game"), Choices.LiveResolution, "live_resolution", into: screenMore);
            SettingCombo(SymbolRegular.Image24, L.T("Presentation mode"), null, Choices.PresentModes, "present_mode", into: screenMore);

            Group(L.T("Frame rate"));
            SettingCombo(SymbolRegular.Gauge24, L.T("Frame rate"), L.T("30, 60, 90, 120 and 144 are the game's frame-rate patches for that rate; Uncapped uses the community uncapped patch"), Choices.FpsModes, "fps_mode");

            Group("HDR");
            var hdrHint = WindowsHdr.AnyOn()
                ? L.T("The game's picture on an HDR screen, with brighter highlights. Applied when the game starts")
                : WindowsHdr.AnySupported()
                    ? L.T("Turn on Use HDR in Windows' display settings first (Settings › System › Display)")
                    : L.T("This display shows no HDR");
            IniToggle(SymbolRegular.Hdr24, L.T("HDR output"), hdrHint, "hdr");
            ComboRow(SymbolRegular.Lightbulb24, L.T("HDR source"),
                "PsychoV30: HDR tone mapping with dark tones matched to the game's SDR rendering. Native: highlights derived from the game's HDR scene. Expanded: bright SDR pixels expanded towards the peak. Entering or leaving PsychoV30 requires restarting the game.",
                Choices.HdrMode, () => Ini.Get("hdr_mode", "native"), v => Ini.Set("hdr_mode", v), changed: RefreshHdrRows);
            // Defaults, as the engine's: paper white 203 (BT.2408's reference white), the peak
            // Windows reports for the chosen display (in HDR).
            const double DefaultPaperWhite = 203;
            double DetectedPeak() => DisplayPeak.Default(S.Str("display_device"));
            var paperWhite = SliderRow(SymbolRegular.BrightnessHigh24, L.T("Paper white"), "How bright the game's white is, in nits. Default: 203. Range: 10–500",
                10, 500, 1, () => ParseDouble(Ini.Get("hdr_paper_white", ""), DefaultPaperWhite),
                v => Ini.Set("hdr_paper_white", v.ToString("0", System.Globalization.CultureInfo.InvariantCulture)),
                format: L.T("0' nits'"), valueWidth: 82);
            if (paperWhite.Parent is Panel paperWhiteRow)
                paperWhiteRow.Children.Add(IconButton(SymbolRegular.ArrowCounterclockwise24, "Reset to 203 nits",
                    (_, _) => paperWhite.Value = DefaultPaperWhite));
            var detected = DisplayPeak.Nits(S.Str("display_device"));
            var peakHint = detected > 0
                ? $"Your display's peak brightness, in nits. Windows reports {detected:0} nits for it: the default. Range: 10–4000"
                : "Your display's peak brightness, in nits. Windows reports none (is Windows HDR on?): 1000 by default. Range: 10–4000";
            var peak = SliderRow(SymbolRegular.WeatherSunny24, L.T("Peak brightness"), peakHint,
                10, 4000, 1, () => ParseDouble(Ini.Get("hdr_peak", ""), DetectedPeak()),
                v => Ini.Set("hdr_peak", v.ToString("0", System.Globalization.CultureInfo.InvariantCulture)),
                format: L.T("0' nits'"), valueWidth: 82);
            if (peak.Parent is Panel peakRow)
                peakRow.Children.Add(IconButton(SymbolRegular.ArrowCounterclockwise24,
                    "Reset to the peak Windows reports for this display (1000 when it reports none)",
                    (_, _) => peak.Value = Math.Clamp(DetectedPeak(), peak.Minimum, peak.Maximum)));
            ComboRow(SymbolRegular.Sparkle24, L.T("Highlight boost"), L.T("Off keeps the game's own look; higher makes lamps, fire and the sky brighter"),
                Choices.HdrBoost, () => Ini.Get("hdr_boost", "0.25"), v => Ini.Set("hdr_boost", v));
            _hdrBoostRow = (FrameworkElement)Body.Children[Body.Children.Count - 1];
            ComboRow(SymbolRegular.Lightbulb24, L.T("Tone mapper"),
                "PsychoV30 only. PsychoV-30: as RenoDX's mods, its own shadows. Hybrid: the shadows matched to the game's below mid grey; compare them in dark interiors and fog. Vanilla: the game's own look. Also in the in-game menu, live",
                Choices.HdrToneMapper, () => Ini.Get("hdr_tonemapper", "4"), v => Ini.Set("hdr_tonemapper", v));
            _hdrToneMapperRow = (FrameworkElement)Body.Children[Body.Children.Count - 1];
            ComboRow(SymbolRegular.Color24, L.T("Target gamut"),
                "PsychoV30 only: the color volume it fits the picture into. BT.2020 is RenoDX's HDR default; compare with BT.709 on saturated lights (lanterns, fire, blue lights). Also in the in-game menu, live",
                Choices.HdrGamut, () => Ini.Get("hdr_gamut", "bt2020"), v => Ini.Set("hdr_gamut", v));
            _hdrGamutRow = (FrameworkElement)Body.Children[Body.Children.Count - 1];
            var hdrMore = Advanced();
            ComboRow(SymbolRegular.TextFont24, L.T("UI brightness"), "Native and PsychoV30: how bright the HUD, menus and subtitles are, against paper white",
                Choices.HdrUiBrightness, () => Ini.Get("hdr_ui_brightness", "1"), v => Ini.Set("hdr_ui_brightness", v), hdrMore);
            _hdrBrightnessRow = (FrameworkElement)hdrMore.Children[hdrMore.Children.Count - 1];
            ComboRow(SymbolRegular.Color24, L.T("UI colour"), L.T("Native source only: Vivid shows the HUD and menus with the colours a wide-gamut monitor gives SDR; Exact keeps sRGB's"),
                Choices.HdrUiColor, () => Ini.Get("hdr_ui_color", "1"), v => Ini.Set("hdr_ui_color", v), hdrMore);
            _hdrColourRow = (FrameworkElement)hdrMore.Children[hdrMore.Children.Count - 1];
            RefreshHdrRows();

            Group(L.T("Language"));
            ComboRow(SymbolRegular.LocalLanguage24, L.T("In-game menu language"), L.T("The port's settings menu (Insert or L3 + R3)"),
                Choices.MenuLanguages, () => Ini.Get("menu_language", "en"), v => Ini.Set("menu_language", v));
            SettingCombo(SymbolRegular.Translate24, L.T("Game language"), L.T("The game's own text and voices"), Choices.GameLanguages, "language");
            SettingCombo(SymbolRegular.Keyboard24, L.T("In-game menu shortcut"),
                L.T("What opens the port's settings menu while playing. Applied on the next launch"), Choices.OverlayKeys, "overlay_keys");
        }
    }
}
