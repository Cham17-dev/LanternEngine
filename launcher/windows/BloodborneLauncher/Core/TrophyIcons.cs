using System.IO;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// The trophies' own pictures, from the player's game dump: sce_sys\trophy\trophy00.trp keeps
    /// them as plain PNGs (TROP000.PNG is trophy 0, GR001.PNG The Old Hunters; only its .ESFM lists
    /// are encrypted). Taken out once per dump into the launcher's settings folder; nothing of the
    /// game ships with the launcher.
    /// </summary>
    public static class TrophyIcons
    {
        private static string _game = "";
        private static string? _folder;

        /// <summary>Trophy <paramref name="id"/>'s picture, or null (no dump, or no trophy file in it).</summary>
        public static string? For(LauncherSettings s, int id)
        {
            var folder = Folder(GameEnvironment.GameDir(s));
            if (folder == null) return null;
            var file = Path.Combine(folder, $"TROP{id:000}.PNG");
            return File.Exists(file) ? file : null;
        }

        private static string? Folder(string game)
        {
            if (game == _game) return _folder;
            _game = game;
            _folder = null;
            try
            {
                var trp = new FileInfo(Path.Combine(game, "sce_sys", "trophy", "trophy00.trp"));
                if (game.Length == 0 || !trp.Exists) return null;
                var folder = Path.Combine(Paths.ConfigDir, "trophy-icons", $"{trp.Length:x}-{trp.LastWriteTimeUtc.Ticks:x}");
                if (!File.Exists(Path.Combine(folder, "TROP000.PNG")))
                {
                    var staging = folder + ".part";
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    Directory.CreateDirectory(staging);
                    if (Extract(File.ReadAllBytes(trp.FullName), staging) == 0) return null;
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                    Directory.Move(staging, folder);
                }
                _folder = folder;
            }
            catch (Exception)
            {
                // No pictures: the trophy cups stay.
            }
            return _folder;
        }

        /// <summary>The PNG entries of a PS4 .trp (big-endian: magic 0xDCA24D00, version, file size,
        /// entry count and size; the entries from 0x60: a 32-byte name, offset, size).</summary>
        private static int Extract(byte[] data, string into)
        {
            static ulong Be(byte[] b, int at, int bytes)
            {
                ulong v = 0;
                for (var i = 0; i < bytes; i++) v = v << 8 | b[at + i];
                return v;
            }
            if (data.Length < 0x60 || Be(data, 0, 4) != 0xDCA24D00) return 0;
            var count = (int)Be(data, 16, 4);
            var size = (int)Be(data, 20, 4);
            if (count <= 0 || count > 4096 || size < 48 || 0x60L + (long)count * size > data.Length) return 0;
            var written = 0;
            for (var i = 0; i < count; i++)
            {
                var entry = 0x60 + i * size;
                var end = Array.IndexOf(data, (byte)0, entry, 32);
                var name = System.Text.Encoding.ASCII.GetString(data, entry, (end < 0 ? entry + 32 : end) - entry);
                var offset = Be(data, entry + 32, 8);
                var length = Be(data, entry + 40, 8);
                if (!name.EndsWith(".PNG", StringComparison.OrdinalIgnoreCase) || offset + length > (ulong)data.Length ||
                    length < 8 || data[offset] != 0x89 || data[offset + 1] != (byte)'P' || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    continue;
                File.WriteAllBytes(Path.Combine(into, name), data.AsSpan((int)offset, (int)length).ToArray());
                written++;
            }
            return written;
        }
    }
}
