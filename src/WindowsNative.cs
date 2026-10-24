using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace ScreenLine
{
    internal static class WindowsNative
    {
        public const int ShowMessage = 0x8001;
        public const int ExitMessage = 0x8002;
        public const string MessageWindow = "ScreenLine.Windows.Messages";
        public static readonly IntPtr MessageParent = new IntPtr(-3);

        [StructLayout(LayoutKind.Sequential)]
        public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct Rectangle { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rectangle rectangle);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr window, string name, IntPtr value);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid folder, uint flags, IntPtr token, out IntPtr path);
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string className, string title);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr process);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint thread, uint target, bool attach);

        public static void ActivateEditor(IntPtr window)
        {
            uint current = GetCurrentThreadId(), foreground = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            bool attached = foreground != 0 && foreground != current && AttachThreadInput(current, foreground, true);
            try { SetForegroundWindow(window); }
            finally { if (attached) AttachThreadInput(current, foreground, false); }
        }

        public static Point Cursor() { Point point; GetCursorPos(out point); return point; }

        public static string WindowTitle(IntPtr window)
        {
            var text = new StringBuilder(1024);
            GetWindowText(window, text, text.Capacity);
            return text.ToString();
        }

        public static Forms.Screen ScreenAtPointer()
        {
            var point = Cursor();
            return Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y));
        }

        public static double MonitorScale(Forms.Screen screen)
        {
            var point = new Point { X = screen.Bounds.Left + screen.Bounds.Width / 2, Y = screen.Bounds.Top + screen.Bounds.Height / 2 };
            uint x, y;
            return GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out x, out y) == 0 ? x / 96.0 : 1;
        }

        public static void PrepareOverlay(IntPtr window)
        {
            // 透明动效覆盖屏幕时仍是叠加层，避免任务栏将其当作全屏应用。
            SetProp(window, "NonRudeHWND", new IntPtr(1));
            SetWindowLong(window, -20, GetWindowLong(window, -20) | 0x08000000 | 0x80);
        }

        public static void PassMouse(IntPtr window, bool pass)
        {
            int before = GetWindowLong(window, -20);
            int after = pass ? before | 0x20 : before & ~0x20;
            if (after != before) SetWindowLong(window, -20, after);
        }

        public static bool FullScreen(Forms.Screen screen, IntPtr overlay)
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == overlay || foreground == GetShellWindow()) return false;
            var name = new StringBuilder(128);
            GetClassName(foreground, name, name.Capacity);
            string type = name.ToString();
            if (type == "WorkerW" || type == "Progman" || type.StartsWith("Shell_")) return false;
            Rectangle rectangle;
            if (!GetWindowRect(foreground, out rectangle)) return false;
            var bounds = screen.Bounds;
            return rectangle.Left <= bounds.Left && rectangle.Top <= bounds.Top && rectangle.Right >= bounds.Right && rectangle.Bottom >= bounds.Bottom;
        }

        public static string ScreenshotFolder()
        {
            IntPtr path;
            if (SHGetKnownFolderPath(new Guid("b7bede81-df94-4682-a7d8-57a52620b86f"), 0, IntPtr.Zero, out path) == 0)
            {
                try { return Marshal.PtrToStringUni(path); }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }

        public static void ShowExisting()
        {
            PostMessage(FindWindowEx(MessageParent, IntPtr.Zero, null, MessageWindow), ShowMessage, IntPtr.Zero, IntPtr.Zero);
        }

        public static void RequestExit()
        {
            PostMessage(FindWindowEx(MessageParent, IntPtr.Zero, null, MessageWindow), ExitMessage, IntPtr.Zero, IntPtr.Zero);
        }
    }

    internal sealed class WindowsEvents : IDisposable
    {
        readonly HwndSource source;
        readonly Dictionary<int, string> registered = new Dictionary<int, string>();
        public bool ShortcutAvailable { get { return registered.ContainsKey(1); } }
        public string InitialShortcutError { get; private set; }
        public event Action ClipboardChanged;
        public event Action ToggleRequested;
        public event Action ExitRequested;
        public event Action<int> HotkeyRequested;

        public WindowsEvents(SavedState settings = null)
        {
            source = new HwndSource(new HwndSourceParameters(WindowsNative.MessageWindow) { ParentWindow = WindowsNative.MessageParent });
            source.AddHook(Receive);
            if (!WindowsNative.AddClipboardFormatListener(source.Handle)) throw new System.ComponentModel.Win32Exception();
            string ignored;
            var configured = settings ?? new SavedState();
            if (!ApplyShortcuts(configured, out ignored))
            {
                InitialShortcutError = ignored;
                var defaults = new SavedState();
                configured.ToggleShortcut = defaults.ToggleShortcut; configured.RegionShortcut = defaults.RegionShortcut;
                configured.FullScreenShortcut = defaults.FullScreenShortcut; configured.WindowShortcut = defaults.WindowShortcut; configured.DelayShortcut = defaults.DelayShortcut;
                configured.TranslateShortcut = defaults.TranslateShortcut; configured.ExplainShortcut = defaults.ExplainShortcut; configured.TextExtractShortcut = defaults.TextExtractShortcut;
                if (!ApplyShortcuts(configured, out ignored))
                {
                    configured.RegionShortcut = configured.FullScreenShortcut = configured.WindowShortcut = configured.DelayShortcut = null;
                    ApplyShortcuts(configured, out ignored);
                }
            }
        }

        public bool ApplyShortcuts(SavedState state, out string error)
        {
            var candidates = new Dictionary<int, string> { { 1, state.ToggleShortcut }, { 2, state.RegionShortcut }, { 3, state.FullScreenShortcut }, { 4, state.WindowShortcut }, { 5, state.DelayShortcut }, { 6, state.TranslateShortcut }, { 7, state.ExplainShortcut }, { 8, state.TextExtractShortcut } };
            var previous = registered.ToDictionary(item => item.Key, item => item.Value);
            foreach (int id in registered.Keys.ToArray()) WindowsNative.UnregisterHotKey(source.Handle, id);
            registered.Clear();
            foreach (var item in candidates)
            {
                if (string.IsNullOrWhiteSpace(item.Value)) continue;
                uint modifiers, key;
                if (!ParseShortcut(item.Value, out modifiers, out key) || !WindowsNative.RegisterHotKey(source.Handle, item.Key, modifiers | 0x4000, key))
                {
                    foreach (int id in registered.Keys.ToArray()) WindowsNative.UnregisterHotKey(source.Handle, id);
                    registered.Clear();
                    foreach (var old in previous)
                    {
                        uint oldModifiers, oldKey;
                        if (ParseShortcut(old.Value, out oldModifiers, out oldKey) && WindowsNative.RegisterHotKey(source.Handle, old.Key, oldModifiers | 0x4000, oldKey)) registered[old.Key] = old.Value;
                    }
                    error = "快捷键“" + item.Value + "”已被系统或其他程序占用，原快捷键已恢复。";
                    return false;
                }
                registered[item.Key] = item.Value;
            }
            error = null;
            return true;
        }

        static bool ParseShortcut(string value, out uint modifiers, out uint key)
        {
            modifiers = 0; key = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var parts = value.Split('+');
            if (parts.Length < 2) return false;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": modifiers |= 0x2; break;
                    case "alt": modifiers |= 0x1; break;
                    case "shift": modifiers |= 0x4; break;
                    case "win": modifiers |= 0x8; break;
                    default: return false;
                }
            }
            if (modifiers == 0) return false;
            string name = parts[parts.Length - 1].Trim();
            if (name.Length == 1 && char.IsLetterOrDigit(name[0])) { key = char.ToUpperInvariant(name[0]); return true; }
            System.Windows.Input.Key parsed;
            if (!Enum.TryParse(name, true, out parsed)) return false;
            key = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(parsed);
            return key != 0;
        }

        IntPtr Receive(IntPtr window, int message, IntPtr w, IntPtr l, ref bool handled)
        {
            if (message == 0x031D && ClipboardChanged != null) ClipboardChanged();
            if (message == 0x0312)
            {
                int id = w.ToInt32();
                if (id == 1 && ToggleRequested != null) ToggleRequested();
                if (HotkeyRequested != null) HotkeyRequested(id);
            }
            if (message == WindowsNative.ShowMessage && ToggleRequested != null) ToggleRequested();
            if (message == WindowsNative.ExitMessage && ExitRequested != null) ExitRequested();
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            WindowsNative.RemoveClipboardFormatListener(source.Handle);
            foreach (int id in registered.Keys.ToArray()) WindowsNative.UnregisterHotKey(source.Handle, id);
            source.Dispose();
        }
    }
}
