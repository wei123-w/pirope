using System.Diagnostics;
using System.IO;

namespace ScreenLine
{
    internal static class WindowsShell
    {
        public static void OpenInbox(StateStore store)
        {
            Directory.CreateDirectory(store.Inbox);
            Process.Start(new ProcessStartInfo(store.Inbox) { UseShellExecute = true, Verb = "open" });
        }
    }
}
