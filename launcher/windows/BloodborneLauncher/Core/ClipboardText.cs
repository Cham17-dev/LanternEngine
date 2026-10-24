using System.Runtime.InteropServices;
using System.Threading;
#if !AVALONIA
using System.Windows;
#endif

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// Clipboard.SetText throws CLIPBRD_E_CANT_OPEN while another program (Discord, a clipboard
    /// manager, a remote desktop) has the clipboard open, sometimes after the text already went in.
    /// Set tries a few times and only reports a failure when the text really isn't there.
    /// </summary>
    public static class ClipboardText
    {
        public static bool Set(string text)
        {
#if AVALONIA
            // Avalonia's clipboard belongs to a window and is asynchronous: the copy is started.
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow?.Clipboard is not { } clipboard) return false;
            _ = clipboard.SetTextAsync(text);
            return true;
#else
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch (ExternalException) // COMException too
                {
                    if (Holds(text)) return true;
                    Thread.Sleep(50);
                }
            }
            return Holds(text);
#endif
        }
#if !AVALONIA

        private static bool Holds(string text)
        {
            try { return Clipboard.ContainsText() && Clipboard.GetText() == text; }
            catch (ExternalException) { return false; }
        }
#endif
    }
}
