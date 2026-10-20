using System.IO;
using BloodborneLauncher.Build;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// Where things live. The engine folder holds out\bb-probe.exe, scripts\run_windows.py and a
    /// bundled Python; bbport.ini, mods.json and patches.json live in its data folder (BB_DATA_DIR,
    /// by default the engine folder itself), as run_windows.py expects them.
    /// Launcher settings are per-user, in %APPDATA%\bbport-launcher\settings.json.
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
                : Path.Combine(DataDir, "bbport.ini");

        /// <summary>The engine and its DLLs.</summary>
        public static string BinariesDir => Path.Combine(PortDir, "out");

        /// <summary>The Bloodborne PC engine: scripts\run_windows.py and out\bb-probe.exe.</summary>
        public static bool IsPortDir(string? dir) =>
            !string.IsNullOrWhiteSpace(dir) &&
            File.Exists(Path.Combine(dir, "scripts", "run_windows.py")) &&
            File.Exists(Path.Combine(dir, "out", "bb-probe.exe"));

        public static string Python => Path.Combine(PortDir, "tools", "python", "python.exe");

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
                        .Select(b => new FileInfo(Path.Combine(dir, b, "bb-probe.exe")))
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
