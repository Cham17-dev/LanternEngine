using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

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
        }

        private static bool Holds(string text)
        {
            try { return Clipboard.ContainsText() && Clipboard.GetText() == text; }
            catch (ExternalException) { return false; }
        }
    }
}
