using System.Runtime.InteropServices;

namespace SnipLite;

internal sealed class DesktopTargets
{
    private readonly List<(IntPtr Handle, Rectangle Bounds)> _windows = new();
    public DesktopTargets()
    {
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || IsIconic(handle)) return true;
            // Floating overlays (including CEF overlays used by desktop tools)
            // can cover the entire monitor while allowing mouse input through.
            // They must not hide the real application in the snapshot's Z-order.
            if ((GetWindowLongPtr(handle, -20).ToInt64() & 0x20) != 0) return true;
            if (DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (!GetWindowRect(handle, out var rect)) return true;
            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (bounds.Width > 1 && bounds.Height > 1) _windows.Add((handle, bounds));
            return true;
        }, IntPtr.Zero);
    }
    public Rectangle At(Point point, bool elements, bool includeAutomation = true)
    {
        var window = _windows.FirstOrDefault(window => window.Bounds.Contains(point));
        if (window.Handle == IntPtr.Zero) return Rectangle.Empty;
        if (!elements) return window.Bounds;
        var native = NativeChildBounds(window.Handle, point, window.Bounds, out _);
        if (!includeAutomation) return native;
        var best = AccessibleAt(point);
        var automation = AutomationAt(point);
        return !automation.IsEmpty && (long)automation.Width * automation.Height < (long)best.Width * best.Height ? automation : best;
    }
    public Rectangle AccessibleAt(Point point)
    {
        var window = _windows.FirstOrDefault(window => window.Bounds.Contains(point));
        if (window.Handle == IntPtr.Zero) return Rectangle.Empty;
        var native = NativeChildBounds(window.Handle, point, window.Bounds, out var childHandle);
        var best = native.IsEmpty ? window.Bounds : native;
        // Some providers expose hit testing only from their own native content
        // HWND. Query it as well as the top-level window, without hitting the overlay.
        best = AccessibleBounds(childHandle, point, window.Bounds, best);
        if (childHandle != window.Handle) best = AccessibleBounds(window.Handle, point, window.Bounds, best);
        return best;
    }
    public Rectangle AutomationAt(Point point)
    {
        var window = _windows.FirstOrDefault(window => window.Bounds.Contains(point));
        if (window.Handle == IntPtr.Zero) return Rectangle.Empty;
        var best = NativeChildBounds(window.Handle, point, window.Bounds, out var childHandle);
        var automation = AutomationBounds(childHandle, point, window.Bounds);
        if (!automation.IsEmpty && (long)automation.Width * automation.Height < (long)best.Width * best.Height) best = automation;
        if (childHandle != window.Handle)
        {
            automation = AutomationBounds(window.Handle, point, window.Bounds);
            if (!automation.IsEmpty && (long)automation.Width * automation.Height < (long)best.Width * best.Height) best = automation;
        }
        return best;
    }
    internal string Describe(Point point) => string.Join("; ", _windows.Where(window => window.Bounds.Contains(point)).Take(6).Select(window =>
    {
        var name = new System.Text.StringBuilder(128); GetClassName(window.Handle, name, name.Capacity);
        GetWindowThreadProcessId(window.Handle, out var process);
        return $"{name} pid={process} bounds={window.Bounds} ex={GetWindowLongPtr(window.Handle, -20):X}";
    }));
    private static Rectangle AccessibleBounds(IntPtr handle, Point point, Rectangle window, Rectangle best)
    {
        try
        {
            var iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
            if (AccessibleObjectFromWindow(handle, 0xFFFFFFFC, ref iid, out var accessible) != 0 || accessible is null) return best;
            var visited = new List<object> { accessible };
            try
            {
                for (var i = 0; i < 16; i++)
                {
                    var hit = accessible.accHitTest(point.X, point.Y);
                    if (hit is Accessibility.IAccessible child && !ReferenceEquals(child, accessible))
                    { accessible = child; visited.Add(child); continue; }
                    accessible.accLocation(out var x, out var y, out var width, out var height, hit is int id ? id : 0);
                    var candidate = new Rectangle(x, y, width, height);
                    if (candidate.Contains(point) && candidate.Width > 1 && candidate.Height > 1)
                    {
                        candidate = Rectangle.Intersect(candidate, window);
                        if ((long)candidate.Width * candidate.Height < (long)best.Width * best.Height) best = candidate;
                    }
                    break;
                }
            }
            finally { foreach (var item in visited.Distinct()) if (Marshal.IsComObject(item)) Marshal.ReleaseComObject(item); }
        }
        catch { }
        return best;
    }
    private static Rectangle AutomationBounds(IntPtr handle, Point point, Rectangle window)
    {
        return NativeUiAutomation.Bounds(handle, point, window);
    }
    private static Rectangle NativeChildBounds(IntPtr root, Point point, Rectangle window, out IntPtr childHandle)
    {
        childHandle = root;
        var current = root; var best = window;
        for (var depth = 0; depth < 32; depth++)
        {
            var local = new POINT { X = point.X, Y = point.Y };
            if (!ScreenToClient(current, ref local)) break;
            // Disabled controls still make valid screenshot targets. Transparent
            // containers may own visible children, so descend through them too.
            var child = ChildWindowFromPointEx(current, local, 0x0001);
            if (child == IntPtr.Zero || child == current) break;
            if (!GetWindowRect(child, out var r)) break;
            var candidate = Rectangle.Intersect(window, Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
            if (!candidate.Contains(point) || candidate.Width <= 1 || candidate.Height <= 1) break;
            best = candidate; current = child; childHandle = child;
        }
        return best;
    }
    private delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref POINT point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPointEx(IntPtr window, POINT point, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible? accessible);
}

internal sealed class CaptureKeyboardHook : IDisposable
{
    private readonly AppSettings _settings;
    private readonly IntPtr _window;
    private readonly HookProc _callback;
    private readonly IntPtr _hook;
    public CaptureKeyboardHook(IntPtr window, AppSettings settings)
    {
        _window = window; _settings = settings; _callback = Filter;
        _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
    }
    private IntPtr Filter(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && GetForegroundWindow() == _window)
        {
            var key = Marshal.ReadInt32(data);
            var flags = Marshal.ReadInt32(data, 8);
            if (key == 0x5B && _settings.BlockLeftWindows || key == 0x5C && _settings.BlockRightWindows
                || key == 'C' && _settings.IgnoreInjectedCopy && (flags & 0x10) != 0 && (GetAsyncKeyState(0x11) & 0x8000) != 0)
                return new IntPtr(1);
        }
        return CallNextHookEx(_hook, code, message, data);
    }
    public void Dispose() { if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook); }
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
