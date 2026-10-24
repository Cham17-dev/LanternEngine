using System.IO;
using BloodborneLauncher.Build;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// Where things live. The package folder (PortDir) holds the launcher, Bloodborne.exe and the
    /// player's files - lantern.ini, mods.json, patches.json, user, mods, logs (its data folder,
    /// BB_DATA_DIR) - and the engine in engine\: out\bb-probe.exe, scripts\run_windows.py and a
    /// bundled Python. Older packages had the engine at the top (EngineDirOf handles both).
    /// Launcher settings are per-user, in %APPDATA%\bbport-launcher-psychov30\settings.json: this
    /// port's own, apart from Lantern's (%APPDATA%\Lantern Engine), so neither changes the other's.
    /// </summary>
    public static class Paths
    {
        public static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "bbport-launcher-psychov30");

        public static readonly string ConfigFile = Path.Combine(ConfigDir, "settings.json");

        /// <summary>The port folder in use: the "port_dir" setting, else one found by ResolvePort.</summary>
        public static string PortDir { get; set; } = "";

        public static string DataDir =>
            Environment.GetEnvironmentVariable("BB_DATA_DIR") is { Length: > 0 } dir ? dir : PortDir;

        public static string IniPath =>
            Environment.GetEnvironmentVariable("BB_CONFIG") is { Length: > 0 } path
                ? path
                : Migrated(DataDir);

        /// <summary>Settings were bbport.ini before 0.9.37 beta 2: copied once per folder (the old
        /// file stays for an older launcher pointed at this folder). Without either, lantern.ini starts
        /// as the shipped settings (engine\lantern.default.ini).
        /// The 0.9.37 beta 2 package carried a lantern.ini with the shipped settings, and launchers
        /// before it keep only bbport.ini when they update: the update put that lantern.ini beside the
        /// player's bbport.ini and their settings looked reset. A lantern.ini that still holds exactly
        /// the shipped settings while bbport.ini holds others is replaced by bbport.ini.</summary>
        private static string Migrated(string dir)
        {
            var path = Path.Combine(dir, "lantern.ini");
            if (dir.Length != 0 && MigratedDirs.Add(dir))
            {
                try
                {
                    var old = Path.Combine(dir, "bbport.ini");
                    var shipped = Path.Combine(EngineDirOf(dir), "lantern.default.ini");
                    if (!File.Exists(path))
                    {
                        if (File.Exists(old)) File.Copy(old, path);
                        else if (File.Exists(shipped)) File.Copy(shipped, path);
                    }
                    else if (File.Exists(old) && File.Exists(shipped))
                    {
                        var current = ReadSettings(path);
                        if (SameSettings(current, ReadSettings(shipped)) && !SameSettings(current, ReadSettings(old)))
                            File.Copy(old, path, overwrite: true);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return path;
        }

        /// <summary>An ini's key=value pairs (comments, blank lines and the order left out).</summary>
        private static Dictionary<string, string> ReadSettings(string path)
        {
            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(path))
            {
                var text = line.Trim();
                var at = text.IndexOf('=');
                if (text.Length == 0 || text[0] == '#' || text[0] == ';' || at < 0) continue;
                settings[text[..at].Trim()] = text[(at + 1)..].Trim();
            }
            return settings;
        }

        private static bool SameSettings(Dictionary<string, string> a, Dictionary<string, string> b) =>
            a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && value == pair.Value);

        private static readonly HashSet<string> MigratedDirs = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>An engine program's file name: name.exe, or name alone in the Linux package
        /// (the Avalonia launcher there).</summary>
        public static string Program(string name) =>
#if AVALONIA
            OperatingSystem.IsWindows() ? name + ".exe" : name;
#else
            name + ".exe";
#endif

        /// <summary>A package's engine folder: its engine\, or the package itself (older layout).</summary>
        public static string EngineDirOf(string dir) =>
            File.Exists(Path.Combine(dir, "engine", "out", Program("bb-probe"))) ||
            File.Exists(Path.Combine(dir, "engine", "scripts", "run_windows.py"))
                ? Path.Combine(dir, "engine")
                : dir;

        /// <summary>The engine in use: out\bb-probe.exe, scripts, patches, shaders and Python.</summary>
        public static string EngineDir => EngineDirOf(PortDir);

        /// <summary>The engine and its DLLs.</summary>
        public static string BinariesDir => Path.Combine(EngineDir, "out");

        /// <summary>NVIDIA's DLSS runtime beside the engine (nvngx_dlss.dll; on Linux
        /// libnvidia-ngx-dlss.so.&lt;version&gt;), or null when the package has none.</summary>
        public static string? DlssRuntime
        {
            get
            {
                if (OperatingSystem.IsWindows())
                {
                    var dll = Path.Combine(BinariesDir, "nvngx_dlss.dll");
                    return File.Exists(dll) ? dll : null;
                }
                try
                {
                    return Directory.Exists(BinariesDir)
                        ? Directory.GetFiles(BinariesDir, "libnvidia-ngx-dlss.so.*").OrderBy(f => f, StringComparer.Ordinal).LastOrDefault()
                        : null;
                }
                catch (IOException)
                {
                    return null;
                }
            }
        }

        /// <summary>The DLSS runtime's version: the DLL's file version, or the end of the .so's name.</summary>
        public static string DlssVersion(string runtime) => OperatingSystem.IsWindows()
            ? System.Diagnostics.FileVersionInfo.GetVersionInfo(runtime).FileVersion ?? ""
            : Path.GetFileName(runtime)["libnvidia-ngx-dlss.so.".Length..];

        /// <summary>The Bloodborne PC engine: out\bb-probe.exe and what starts it (out\bb-start.exe, or
        /// scripts\run_windows.py before 0.9.32), in the folder or its engine\.</summary>
        public static bool IsPortDir(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            var engine = EngineDirOf(dir);
            return File.Exists(Path.Combine(engine, "out", Program("bb-probe"))) &&
                   (File.Exists(Path.Combine(engine, "out", Program("bb-start"))) ||
                    File.Exists(Path.Combine(engine, "scripts", "run_windows.py")));
        }

        /// <summary>The native start (0.9.32 and later): the boot image, mods, patches, then the game.</summary>
        public static string Start => Path.Combine(EngineDir, "out", Program("bb-start"));

        /// <summary>The Python an older engine started with (scripts\run_windows.py).</summary>
        public static string Python => Path.Combine(EngineDir, "tools", "python", "python.exe");

        /// <summary>The port next to the game folder (its usual layout: &lt;port&gt;\CUSA03173).</summary>
        public static string? PortBesideGame(string gameDir)
        {
            try
            {
                var parent = string.IsNullOrWhiteSpace(gameDir) ? null : Directory.GetParent(gameDir)?.FullName;
                return IsPortDir(parent) ? parent : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>A port among the folders beside the game folder (E:\games\CUSA03173 next to
        /// E:\games\bbport-windows), the one with the newest bb-probe.exe.</summary>
        public static string? PortNearGame(string gameDir)
        {
            try
            {
                var parent = string.IsNullOrWhiteSpace(gameDir) ? null : Directory.GetParent(gameDir)?.FullName;
                if (parent == null) return null;
                return Directory.GetDirectories(parent)
                    .Where(IsPortDir)
                    .OrderByDescending(dir => new[] { "bin", "out" }
                        .Select(b => new FileInfo(Path.Combine(EngineDirOf(dir), b, Program("bb-probe"))))
                        .Where(f => f.Exists).Select(f => f.LastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max())
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>A port folder above this exe (the launcher copied into the port).</summary>
        public static string? DetectPortDir()
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                    if (IsPortDir(dir.FullName))
                        return dir.FullName.TrimEnd(Path.DirectorySeparatorChar);
            return null;
        }

        /// <summary>launcher/windows in the source checkout, when this copy was built from it.</summary>
        public static string? SourceRoot => SourceStamp.LocateSources(AppContext.BaseDirectory);

        public static string Expand(string path) =>
            Environment.ExpandEnvironmentVariables(path.StartsWith("~")
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..]
                : path);

        public static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
#if AVALONIA
                if (!OperatingSystem.IsWindows())
                {
                    // The desktop's file manager.
                    var open = new System.Diagnostics.ProcessStartInfo("xdg-open") { UseShellExecute = false };
                    open.ArgumentList.Add(path);
                    System.Diagnostics.Process.Start(open)?.Dispose();
                    return;
                }
#endif
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"")
                    { UseShellExecute = true });
            }
            catch
            {
                // Nothing useful to do if Explorer will not open.
            }
        }
    }
}
