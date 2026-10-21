namespace BloodborneLauncher.Core
{
    /// <summary>
    /// Controller remapping. The engine reads pad.&lt;input&gt;=&lt;buttons&gt; from bbport.ini: each PS4
    /// input the game sees (cross, r1, …) is driven by the listed physical buttons, named as SDL
    /// names them (a b x y leftshoulder righttrigger dpup …). Assigning a button that another
    /// input uses swaps the two, so every button keeps doing exactly one thing.
    /// </summary>
    public static class PadMap
    {
        public sealed record Button(string Sdl, string Label);

        /// <summary>The physical buttons, PlayStation name first, Xbox name after the slash.</summary>
        public static readonly Button[] Buttons =
        {
            new("a", "Cross / A"), new("b", "Circle / B"), new("x", "Square / X"), new("y", "Triangle / Y"),
            new("leftshoulder", "L1 / LB"), new("rightshoulder", "R1 / RB"),
            new("lefttrigger", "L2 / LT"), new("righttrigger", "R2 / RT"),
            new("leftstick", "L3 (left stick click)"), new("rightstick", "R3 (right stick click)"),
            new("dpup", "D-pad up"), new("dpdown", "D-pad down"), new("dpleft", "D-pad left"), new("dpright", "D-pad right"),
            new("start", "Options / Start"), new("back", "Share / Back"), new("touchpad", "Touchpad click"),
            new("misc1", "Mute / Share (Xbox Series)"),
            new("paddle1", "Extra button 1 (Edge / Elite paddle, 8BitDo R4 or back button)"),
            new("paddle2", "Extra button 2 (Edge / Elite paddle, 8BitDo L4 or back button)"),
            new("paddle3", "Extra button 3 (Elite paddle, 8BitDo back button)"),
            new("paddle4", "Extra button 4 (Elite paddle, 8BitDo back button)"),
            new("", "Not bound"),
        };

        /// <summary>The game's input behind each controller label in the actions table.</summary>
        private static readonly Dictionary<string, string> InputOfLabel = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Cross"] = "cross", ["Circle"] = "circle", ["Square"] = "square", ["Triangle"] = "triangle",
            ["L1"] = "l1", ["R1"] = "r1", ["L2"] = "l2", ["R2"] = "r2", ["L3"] = "l3", ["R3"] = "r3",
            ["Options"] = "options", ["Touchpad (left)"] = "touchpad", ["Touchpad (right)"] = "touchpad_right",
            ["D-pad up"] = "up", ["D-pad down"] = "down", ["D-pad left"] = "left", ["D-pad right"] = "right",
        };

        /// <summary>The engine's defaults (runtime_pad.c bind_defaults).</summary>
        public static readonly Dictionary<string, string> Defaults = new()
        {
            ["cross"] = "a", ["circle"] = "b", ["square"] = "x", ["triangle"] = "y",
            ["l1"] = "leftshoulder", ["r1"] = "rightshoulder", ["l2"] = "lefttrigger", ["r2"] = "righttrigger",
            ["l3"] = "leftstick", ["r3"] = "rightstick", ["options"] = "start",
            ["touchpad"] = "back, touchpad", ["touchpad_right"] = "",
            ["up"] = "dpup", ["down"] = "dpdown", ["left"] = "dpleft", ["right"] = "dpright",
        };

        public static IEnumerable<string> Inputs => Defaults.Keys;

        public static string? InputFor(ControlDef control) =>
            control.Pad != null && InputOfLabel.TryGetValue(control.Pad, out var input) ? input : null;

        private static string Key(string input) => "pad." + input;

        public static List<string> Get(IniFile ini, string input) =>
            Split(ini.Raw(Key(input)) ?? Defaults[input]);

        /// <summary>The first (main) button of an input, "" when unbound.</summary>
        public static string Main(IniFile ini, string input) => Get(ini, input).FirstOrDefault() ?? "";

        public static bool IsDefault(IniFile ini, string input) =>
            Get(ini, input).SequenceEqual(Split(Defaults[input]));

        public static string Label(string sdl) =>
            Buttons.FirstOrDefault(b => b.Sdl.Equals(sdl, StringComparison.OrdinalIgnoreCase))?.Label ?? sdl;

        /// <summary>Puts <paramref name="button"/> on <paramref name="input"/>; the input that had it gets this one's old button.</summary>
        public static void Assign(IniFile ini, string input, string button)
        {
            var old = Main(ini, input);
            if (button.Length > 0)
                foreach (var other in Inputs.Where(i => i != input))
                {
                    var buttons = Get(ini, other);
                    if (!buttons.Remove(button)) continue;
                    if (old.Length > 0 && !buttons.Contains(old)) buttons.Insert(0, old);
                    Write(ini, other, buttons);
                }
            Write(ini, input, button.Length > 0 ? new List<string> { button } : new List<string>());
        }

        public static void Reset(IniFile ini, string input) => Write(ini, input, Split(Defaults[input]));

        public static void ResetAll(IniFile ini)
        {
            foreach (var input in Inputs) Reset(ini, input);
        }

        /// <summary>Default inputs are removed from the file, so the engine's defaults apply.</summary>
        private static void Write(IniFile ini, string input, List<string> buttons)
        {
            if (buttons.SequenceEqual(Split(Defaults[input])))
                ini.Set(Key(input), null);
            else
                ini.Set(Key(input), string.Join(", ", buttons));
        }

        public static Dictionary<string, string> Current(IniFile ini) =>
            Inputs.Where(i => !IsDefault(ini, i)).ToDictionary(i => i, i => string.Join(", ", Get(ini, i)));

        public static void Apply(IniFile ini, IReadOnlyDictionary<string, string>? map)
        {
            foreach (var input in Inputs)
                Write(ini, input, map != null && map.TryGetValue(input, out var v) ? Split(v) : Split(Defaults[input]));
        }

        private static List<string> Split(string value) =>
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => v.ToLowerInvariant()).Where(v => v != "none").ToList();
    }
}
