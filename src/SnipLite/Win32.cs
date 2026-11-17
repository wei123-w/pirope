using System.Runtime.InteropServices;

namespace SnipLite;

internal static class Win32
{
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Raw cursor position in physical screen pixels, no DPI translation.</summary>
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    public static Point CursorPosition()
    {
        GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    [StructLayout(LayoutKind.Sequential)] private struct CURSORINFO { public int Size, Flags; public IntPtr Icon; public POINT Position; }
    [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { [MarshalAs(UnmanagedType.Bool)] public bool Icon; public uint XHotspot, YHotspot; public IntPtr Mask, Color; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    public static void DrawCursor(Graphics graphics, Point screenOrigin)
    {
        var cursor = new CURSORINFO { Size = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref cursor) || cursor.Flags != 1 || !GetIconInfo(cursor.Icon, out var icon)) return;
        var dc = graphics.GetHdc();
        try { DrawIconEx(dc, cursor.Position.X - screenOrigin.X - (int)icon.XHotspot, cursor.Position.Y - screenOrigin.Y - (int)icon.YHotspot, cursor.Icon, 0, 0, 0, IntPtr.Zero, 3); }
        finally { graphics.ReleaseHdc(dc); if (icon.Mask != IntPtr.Zero) DeleteObject(icon.Mask); if (icon.Color != IntPtr.Zero) DeleteObject(icon.Color); }
    }
}
