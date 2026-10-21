using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// settings.json, shared with the Windows port's own launcher (same folder, same keys).
    /// Unknown keys are kept, so neither launcher loses the other's settings.
    /// </summary>
    public sealed class LauncherSettings
    {
        public const string OnlineCommunity = "https://thehuntersdream.com";
        public const string Discord = "https://discord.gg/d3Uv3Cfr5x";
        public const string ShadnetServer = "srv.shadps4.net:31313";

        private JsonObject _data = new();

        private static Dictionary<string, JsonNode?> Defaults() => new()
        {
            ["ui_language"] = "en",
            ["game_dir"] = "",
            ["user_dir"] = "",
            ["dlc_dir"] = "",
            ["player_profile"] = "",
            ["player_profiles"] = "",
            ["mods_dir"] = "",
            ["mods_enabled"] = true,
            ["patches_dir"] = "",
            ["language"] = "1",
            ["fullscreen"] = false,
            ["display_device"] = "",
            ["present_mode"] = "Mailbox",
            ["fps_mode"] = "60",
            ["draw_pipe"] = "",
            ["readbacks"] = "",
            ["frame_stats"] = false,
            ["gpu_profile"] = false,
            ["vk_validation"] = false,
            ["extra_env"] = "",
            ["dlc_enabled"] = true,
            ["online_password"] = "",
            // This launcher.
            ["port_dir"] = "",
            ["launch_method"] = "windows",
            ["custom_command"] = "",
            ["custom_args"] = "",
            ["switch_to_output"] = true,
            ["check_updates"] = false,
            ["save_backups"] = true,
            ["discord_status"] = "off",
            ["discord_timer"] = true,
            ["safe_mode_next"] = false,
            ["memory_check_off"] = false,
            ["discord_hidden"] = false,
            ["hardware_offered"] = false,
            ["cheat_vials_amount"] = 20,
            ["cheat_bullets_amount"] = 20,
            ["update_channel"] = "", // empty: beta builds follow beta, others stable
            ["ui_layout"] = "modern",
            ["ui_theme"] = "default",
            ["ui_theme_colour"] = "#C41E3A",
            ["ui_background"] = "game", // game | custom | none
            ["ui_background_image"] = "",
            ["ui_background_blur"] = "strong", // strong | light | off
            ["app_icon"] = "auto", // auto | red | blue | j3loo (AppIcon)
            ["ui_window_buttons"] = "mac", // mac | windows
            ["ui_window_buttons_side"] = "usual", // usual (mac left, windows right) | left | right
            ["fsr4_balanced"] = false,
            ["exp_vram_cleanup"] = true,
            ["exp_frame_pacing"] = false,
            ["exp_thread_priority"] = false,
            ["exp_sleep_spin"] = false,
            ["exp_async_pipelines"] = true,
            ["exp_fast_loading"] = false,
            ["exp_hot_pages"] = false,
            ["exp_sfx_workaround"] = false,
            ["exp_camera_easing"] = false,
            ["borderless_fullscreen"] = false,
            // The new engine features (EnginePreset New): on since 0.9.34, stable builds too.
            // Beta 9.9: off by default (objects and floors popped in late or flickered).
            ["exp_occlusion"] = false,
            ["exp_fast_draws"] = true,
            ["exp_compile_skip"] = true,
            ["exp_vram_budget"] = true,
            // Still being tested: on in beta builds only.
            ["exp_sharp_ui"] = Updater.CurrentBeta > 0,
            ["audio_priority"] = false,
            ["sound_output"] = "",
            ["load_profile"] = false,
            ["occlusion_default_on"] = true,
            ["vram_budget_default_on"] = true,
            ["section_stats"] = false,
            ["shader_capture"] = false,
            ["overlay_keys"] = "both",
            ["dlss_preset"] = "k",
            ["legacy_shaders"] = true, // beta 9.8: the native compiler costs FPS in dense scenes on NVIDIA
            ["trophy_notifications"] = true,
            ["trophy_sound"] = true,
            ["trophy_volume"] = 70,
            ["trophy_position"] = "top-right",
        };

        public void Load()
        {
            _data = new JsonObject();
            foreach (var (key, value) in Defaults())
                _data[key] = value;

            try
            {
                if (File.Exists(Paths.ConfigFile) &&
                    JsonNode.Parse(File.ReadAllText(Paths.ConfigFile)) is JsonObject saved)
                {
                    foreach (var (key, value) in saved)
                        _data[key] = value?.DeepClone();
                    // Beta builds: textures kept up to 80% of the card (the 40% default evicted and
                    // reloaded hundreds a second on 6-8 GB cards); switched on once for older files.
                    // The marker is only ever written by a beta: "vram_budget_default_on" is in
                    // Defaults(), so a stable launcher saved it beside its own default (off) and a
                    // player moving from stable to a beta kept the 40% marks (RTX 4060 at 3.4-4.3
                    // GB of 2486 MiB: evictions, re-uploads and tiled write-backs every frame).
                    if (Updater.CurrentBeta > 0 && !saved.ContainsKey("vram_budget_beta_on"))
                        _data["exp_vram_budget"] = true;
                    // Beta 9.9: occlusion culling off once (9.8 switched it on for everyone; floors
                    // and objects appeared late or flickered). On again only by choice.
                    if (!saved.ContainsKey("occlusion_off_99"))
                        _data["exp_occlusion"] = false;
                    // Beta 9.8: the legacy shader compiler is the default again (the native one
                    // held 45-55 FPS where the legacy one held 60, Forbidden Woods, RTX 3090);
                    // switched on once, the native one stays a choice.
                    if (!saved.ContainsKey("legacy_shaders_default_98"))
                        _data["legacy_shaders"] = true;
                    // Older launchers kept the shadNet password in plain text: encrypted now, and
                    // the file rewritten without it.
                    if (Str("online_password") is { Length: > 0 } plain && !Secret.IsProtected(plain))
                    {
                        OnlinePassword = plain;
                        Save();
                    }
                }
            }
            catch
            {
                // A broken file falls back to the defaults; it is rewritten on the next save.
            }
            // Saved with the next change: from then on the player's own choice stays.
            if (Updater.CurrentBeta > 0)
                _data["vram_budget_beta_on"] = true;
            _data["legacy_shaders_default_98"] = true;
            _data["occlusion_off_99"] = true;
        }

        public void Save()
        {
            Directory.CreateDirectory(Paths.ConfigDir);
            File.WriteAllText(Paths.ConfigFile,
                _data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>The shadNet password, kept encrypted for this Windows user (Core/Secret.cs).</summary>
        public string OnlinePassword
        {
            get => Secret.Unprotect(Str("online_password"));
            // Should Windows refuse to encrypt it, it stays as typed rather than being lost.
            set => Set("online_password", Secret.Protect(value) is { Length: > 0 } encrypted ? encrypted : value);
        }

        /// <summary>The Hunter's Dream account token (its device link), encrypted like the password.</summary>
        public string HuntersDreamToken
        {
            get => Secret.Unprotect(Str("hd_token"));
            set
            {
                Set("hd_token", value.Length == 0 ? "" : Secret.Protect(value) is { Length: > 0 } encrypted ? encrypted : value);
                // A new sign-in (or none): a refusal was for the token before it.
                Set("hd_refused", "");
            }
        }

        /// <summary>Why The Hunter's Dream refused this PC's sign-in in a game (hd_refused, the server's
        /// reason), until the player signs in again; empty when it did not.</summary>
        public string HuntersDreamRefused => Str("hd_refused");

        public string Str(string key)
        {
            var node = _data[key];
            if (node is JsonValue v)
            {
                if (v.TryGetValue<string>(out var s)) return s;
                return v.ToJsonString().Trim('"');
            }
            return "";
        }

        public bool Bool(string key)
        {
            var node = _data[key];
            if (node is JsonValue v)
            {
                if (v.TryGetValue<bool>(out var b)) return b;
                if (v.TryGetValue<int>(out var i)) return i != 0;
                if (v.TryGetValue<string>(out var s)) return s is "1" or "true" or "True";
            }
            return false;
        }

        public int Int(string key)
        {
            var node = _data[key];
            if (node is JsonValue v)
            {
                if (v.TryGetValue<int>(out var i)) return i;
                if (v.TryGetValue<double>(out var d)) return (int)d;
                if (v.TryGetValue<string>(out var s) && int.TryParse(s, out i)) return i;
            }
            return 0;
        }

        /// <summary>A string-to-string object (e.g. key_bindings), empty when absent.</summary>
        public Dictionary<string, string> Map(string key)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_data[key] is JsonObject obj)
                foreach (var (k, v) in obj)
                    if (v is JsonValue value && value.TryGetValue<string>(out var s))
                        map[k] = s;
            return map;
        }

        public void SetMap(string key, IReadOnlyDictionary<string, string> map)
        {
            var obj = new JsonObject();
            foreach (var (k, v) in map.OrderBy(p => p.Key, StringComparer.Ordinal))
                obj[k] = v;
            _data[key] = obj;
        }

        public void Set(string key, string value) => _data[key] = value;

        /// <summary>Forgets a setting: it reads as absent ("" / false / 0) again.</summary>
        public void Remove(string key) => _data.Remove(key);

        /// <summary>Every setting as stored (shared settings).</summary>
        public IEnumerable<KeyValuePair<string, JsonNode?>> All => _data;

        public void SetNode(string key, JsonNode value) => _data[key] = value.DeepClone();
        public void Set(string key, bool value) => _data[key] = value;
        public void Set(string key, int value) => _data[key] = value;
    }
}
