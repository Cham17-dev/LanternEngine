using System.Runtime.InteropServices;
using System.Text;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// Secrets kept in settings.json encrypted for this Windows user (DPAPI): another user or PC
    /// cannot read them, and the file can be shared without them. Stored as "dpapi:" + base64; a
    /// value without the prefix is plain text from an older launcher, which reads as it is.
    /// </summary>
    public static class Secret
    {
        private const string Prefix = "dpapi:";
        private const int UiForbidden = 1; // CRYPTPROTECT_UI_FORBIDDEN

        [StructLayout(LayoutKind.Sequential)]
        private struct Blob { public int Size; public IntPtr Data; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, int flags, out Blob output);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, int flags, out Blob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

#if AVALONIA
        public static bool IsProtected(string stored) =>
            stored.StartsWith(Prefix, StringComparison.Ordinal) || stored.StartsWith(LocalPrefix, StringComparison.Ordinal);

        /// <summary>Linux has no DPAPI: "local:" + base64 of AES-GCM (nonce, tag, text) under a key made once
        /// for this user, in a file only they can read (secret.key beside settings.json).</summary>
        private const string LocalPrefix = "local:";

        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        private static byte[] LocalKey()
        {
            var file = Path.Combine(Paths.ConfigDir, "secret.key");
            if (File.Exists(file) && File.ReadAllBytes(file) is { Length: 32 } saved) return saved;
            var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            Directory.CreateDirectory(Paths.ConfigDir);
            using (var stream = new FileStream(file, new FileStreamOptions
                   {
                       Mode = FileMode.Create, Access = FileAccess.Write,
                       UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                   }))
                stream.Write(key);
            return key;
        }

        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        private static string ProtectLocal(string plain)
        {
            var text = Encoding.UTF8.GetBytes(plain);
            var sealed_ = new byte[12 + 16 + text.Length];
            System.Security.Cryptography.RandomNumberGenerator.Fill(sealed_.AsSpan(0, 12));
            using var aes = new System.Security.Cryptography.AesGcm(LocalKey(), 16);
            aes.Encrypt(sealed_.AsSpan(0, 12), text, sealed_.AsSpan(28), sealed_.AsSpan(12, 16));
            return LocalPrefix + Convert.ToBase64String(sealed_);
        }

        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        private static string UnprotectLocal(string stored)
        {
            try
            {
                var sealed_ = Convert.FromBase64String(stored[LocalPrefix.Length..]);
                if (sealed_.Length < 28) return "";
                var text = new byte[sealed_.Length - 28];
                using var aes = new System.Security.Cryptography.AesGcm(LocalKey(), 16);
                aes.Decrypt(sealed_.AsSpan(0, 12), sealed_.AsSpan(28), sealed_.AsSpan(12, 16), text);
                return Encoding.UTF8.GetString(text);
            }
            catch (Exception)
            {
                return ""; // another user's or another PC's: sign in again
            }
        }
#else
        public static bool IsProtected(string stored) => stored.StartsWith(Prefix, StringComparison.Ordinal);
#endif

        public static string Protect(string plain)
        {
            if (plain.Length == 0) return "";
#if AVALONIA
            if (!OperatingSystem.IsWindows()) return ProtectLocal(plain);
#endif
            var bytes = Transform(Encoding.UTF8.GetBytes(plain), encrypt: true);
            return bytes == null ? "" : Prefix + Convert.ToBase64String(bytes);
        }

        /// <summary>The plain text; empty when it cannot be read (another Windows user's).</summary>
        public static string Unprotect(string stored)
        {
            if (!IsProtected(stored)) return stored;
#if AVALONIA
            if (!OperatingSystem.IsWindows())
                // A Windows user's DPAPI secret (a settings.json brought over) can't be read here.
                return stored.StartsWith(LocalPrefix, StringComparison.Ordinal) ? UnprotectLocal(stored) : "";
            if (stored.StartsWith(LocalPrefix, StringComparison.Ordinal)) return "";
#endif
            try
            {
                var bytes = Transform(Convert.FromBase64String(stored[Prefix.Length..]), encrypt: false);
                return bytes == null ? "" : Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                return "";
            }
        }

        private static byte[]? Transform(byte[] bytes, bool encrypt)
        {
            var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1)) };
            Blob output = default;
            try
            {
                Marshal.Copy(bytes, 0, input.Data, bytes.Length);
                var ok = encrypt
                    ? CryptProtectData(ref input, "Bloodborne launcher", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
                if (!ok || output.Size < 0 || output.Size > 1 << 16) return null;
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally
            {
                Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length);
                Marshal.FreeHGlobal(input.Data);
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }
    }
}
