using System.IO;
using System.IO.Compression;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// Shares the engine's compiled shaders (user\cache) between players, so one player's trip
    /// through the game spares the others its shader stutters. A cache only fits the shader
    /// compiler that made it (user\cache\engine.txt) and the kind of GPU it was made for (the
    /// game's profile.bin), and it is taken whole: a shader's variants are numbered in the order a
    /// player met them, so two caches never mix. The driver's own cache (.vkcache) stays out.
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

        /// <summary>Writes the cache to <paramref name="zip"/>; the number of pipelines in it.</summary>
        public static int Export(string zip)
        {
            var folder = Path.Combine(CacheDir, Game);
            var count = Pipelines(folder);
            if (count == 0) throw new InvalidOperationException("there is no shader cache yet: play for a while first");
            if (File.Exists(zip)) File.Delete(zip);
            using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
            var stamp = archive.CreateEntry("engine.txt");
            using (var writer = new StreamWriter(stamp.Open())) writer.Write(Identity() + "\n");
            foreach (var file in Directory.EnumerateFiles(folder).Where(f => Shared(f)))
                archive.CreateEntryFromFile(file, Game + "/" + Path.GetFileName(file), CompressionLevel.Optimal);
            return count;
        }

        /// <summary>Replaces the cache with the one in <paramref name="zip"/> (the old one kept as
        /// CUSA03173.bak); the number of pipelines taken. Throws with the reason when it doesn't fit.</summary>
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
