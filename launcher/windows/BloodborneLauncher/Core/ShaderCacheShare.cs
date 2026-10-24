using System.IO;
using System.IO.Compression;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// Shares the engine's shaders between players, so one player's trip through the game spares
    /// the others its pop-in and stutters. Since 0.9.37 beta 4 a file is a shader catalog
    /// (bb-shader-lab --export-catalog, engine\docs\pipeline_compiles.md): each shader's draw
    /// state, specialization and pipeline keys, without the game's code. Using one merges it into
    /// this cache (bb-shader-lab --seed-cache: the code from this PC's game files, compiled by this
    /// engine), so it fits any engine version that reads it and several players' catalogs add up.
    /// It needs a graphics card of the same kind (the GPU part of profile.bin). Older files (a whole
    /// cache, engine.txt beside it) still replace the cache, as before.
    /// </summary>
    public static class ShaderCacheShare
    {
        private const string Game = "CUSA03173";

        public static string CacheDir => Path.Combine(Paths.PortDir, "user", "cache");

        /// <summary>What this engine stamps its caches with (start\main.cpp ResetStaleGpuCache).</summary>
        private static string Identity()
        {
            var stamp = Path.Combine(CacheDir, "engine.txt");
            if (File.Exists(stamp)) return File.ReadAllText(stamp).Trim();
            var id = Path.Combine(Paths.PortDir, "engine", "out", "gpu-cache.id");
            return File.Exists(id) ? "gpu " + File.ReadAllText(id).Trim() : "";
        }

        private static bool Shared(string name) =>
            Path.GetExtension(name) is ".key" or ".meta" or ".spv" or ".bin";

        public static int Pipelines(string folder) =>
            Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.key").Count() : 0;

        private static string Lab => Path.Combine(Paths.EngineDir, "out", "bb-shader-lab.exe");

        /// <summary>Runs the shader lab hidden; its exit code and output.</summary>
        private static (int Code, string Output) RunLab(params string[] args)
        {
            if (!File.Exists(Lab)) throw new InvalidOperationException("the engine's shader lab is missing (engine\\out\\bb-shader-lab.exe)");
            var info = new System.Diagnostics.ProcessStartInfo(Lab)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("the shader lab did not start");
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output + error.Result);
        }

        /// <summary>The lab's last line starting with <paramref name="prefix"/>, without it.</summary>
        private static string Line(string output, string prefix)
        {
            var at = output.LastIndexOf(prefix, StringComparison.Ordinal);
            if (at < 0) return "";
            var end = output.IndexOf('\n', at);
            return output[(at + prefix.Length)..(end < 0 ? output.Length : end)].Trim();
        }

        /// <summary>Writes this cache as a shader catalog to <paramref name="zip"/>; the number of
        /// pipelines in it. Takes seconds: call it off the UI thread.</summary>
        public static int Export(string zip)
        {
            var folder = Path.Combine(CacheDir, Game);
            if (Pipelines(folder) == 0) throw new InvalidOperationException("there is no shader cache yet: play for a while first");
            var work = Path.Combine(Path.GetTempPath(), "lantern-catalog-" + Guid.NewGuid().ToString("N"));
            try
            {
                var catalog = Path.Combine(work, Game);
                var (code, output) = RunLab("--export-catalog", folder, catalog);
                var count = Pipelines(catalog);
                if (code != 0 || count == 0)
                    throw new InvalidOperationException("the shader lab could not write it: " + Line(output, "Shader catalog:"));
                if (File.Exists(zip)) File.Delete(zip);
                ZipFile.CreateFromDirectory(work, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
                return count;
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch (Exception) { }
            }
        }

        /// <summary>Whether <paramref name="zip"/> is a shader catalog (merged) rather than a whole
        /// cache from an older launcher (it replaces this one).</summary>
        public static bool IsCatalog(string zip)
        {
            using var archive = ZipFile.OpenRead(zip);
            return archive.GetEntry("engine.txt") == null &&
                   archive.Entries.Any(e => e.FullName.Replace('\\', '/') == Game + "/files.txt");
        }

        /// <summary>Merges the catalog in <paramref name="zip"/> into this cache, the shaders' code
        /// from the game in <paramref name="gameDir"/>; the pipelines it added. Takes seconds to a
        /// minute: call it off the UI thread. Throws with the reason when it doesn't fit.</summary>
        public static int Merge(string zip, string gameDir)
        {
            var folder = Path.Combine(CacheDir, Game);
            // The engine writes this GPU's profile at its first start: until then nothing says
            // which kind of graphics card the shaders are for.
            if (!File.Exists(Path.Combine(folder, "profile.bin")))
                throw new InvalidOperationException("start the game once first, so the engine knows your graphics card");
            var eboot = Path.Combine(Paths.EngineDir, "out", "eboot.elf");
            if (!File.Exists(eboot) || !Directory.Exists(Path.Combine(gameDir, "dvdroot_ps4", "shader")))
                throw new InvalidOperationException("the game folder is not set up yet: start the game once first");
            var work = Path.Combine(Path.GetTempPath(), "lantern-catalog-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var archive = ZipFile.OpenRead(zip))
                {
                    var root = Path.GetFullPath(work) + Path.DirectorySeparatorChar;
                    foreach (var entry in archive.Entries)
                    {
                        var name = entry.FullName.Replace('\\', '/');
                        if (!name.StartsWith(Game + "/", StringComparison.Ordinal) || entry.Name.Length == 0) continue;
                        var target = Path.GetFullPath(Path.Combine(work, name));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // outside the folder
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, overwrite: true);
                    }
                }
                var before = Pipelines(folder);
                var (code, output) = RunLab("--seed-cache", Path.Combine(work, Game), folder, gameDir, eboot);
                if (code == 5) throw new InvalidOperationException("it was made on another kind of graphics card, which compiles shaders differently");
                if (code != 0) throw new InvalidOperationException("the shader lab could not use it: " + Line(output, "Shader catalog:"));
                return Math.Max(0, Pipelines(folder) - before);
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch (Exception) { }
            }
        }

        /// <summary>Replaces the cache with the one in <paramref name="zip"/>, a whole cache saved by a
        /// launcher before 0.9.37 beta 4 (the old one kept as CUSA03173.bak); the number of pipelines
        /// taken. Throws with the reason when it doesn't fit.</summary>
        public static int Import(string zip)
        {
            using var archive = ZipFile.OpenRead(zip);
            var stamp = archive.GetEntry("engine.txt") ?? throw new InvalidOperationException("this is not a shared shader cache");
            string theirs;
            using (var reader = new StreamReader(stamp.Open())) theirs = reader.ReadToEnd().Trim();
            var ours = Identity();
            if (theirs.Length == 0 || theirs != ours)
                throw new InvalidOperationException("it was made by another engine version's shader compiler; both players need the same version");
            var entries = archive.Entries
                .Where(e => e.FullName.StartsWith(Game + "/", StringComparison.Ordinal) && e.Name.Length > 0 && Shared(e.Name))
                .ToList();
            var count = entries.Count(e => e.Name.EndsWith(".key", StringComparison.Ordinal));
            var profile = entries.FirstOrDefault(e => e.Name == "profile.bin");
            if (count == 0 || profile == null) throw new InvalidOperationException("the file has no shaders in it");
            var folder = Path.Combine(CacheDir, Game);
            var mine = Path.Combine(folder, "profile.bin");
            if (File.Exists(mine))
            {
                using var stream = profile.Open();
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                if (!copy.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(mine)))
                    throw new InvalidOperationException("it was made on another kind of graphics card or driver, which compiles shaders differently");
            }
            var backup = Path.Combine(CacheDir, Game + ".bak");
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            if (Directory.Exists(folder)) Directory.Move(folder, backup);
            Directory.CreateDirectory(folder);
            foreach (var entry in entries)
                entry.ExtractToFile(Path.Combine(folder, entry.Name), overwrite: true);
            File.WriteAllText(Path.Combine(CacheDir, "engine.txt"), ours + "\n");
            return count;
        }
    }
}
