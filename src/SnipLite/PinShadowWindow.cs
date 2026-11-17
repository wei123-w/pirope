using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnipLite;

/// <summary>A per-pixel alpha shadow, separate from the exported image.</summary>
internal sealed class PinShadowWindow : Form
{
    public PinShadowWindow()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual;
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080; return cp; }
    }
    public static Bitmap RenderShadow(Size size, Rectangle image, Color color, float scale = 1)
    {
        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        var pixels = new int[checked(size.Width * size.Height)];
        var sigma = Math.Max(2, 5 * scale);
        var shadow = image; shadow.Offset((int)(3 * scale), (int)(5 * scale));
        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                if (image.Contains(x, y)) { if (x == image.Left) x = Math.Min(size.Width - 1, image.Right - 1); continue; }
                var dx = Math.Max(shadow.Left - x, Math.Max(0, x - shadow.Right + 1));
                var dy = Math.Max(shadow.Top - y, Math.Max(0, y - shadow.Bottom + 1));
                var alpha = (int)Math.Round(68 * Math.Exp(-(dx * dx + dy * dy) / (2 * sigma * sigma)));
                pixels[y * size.Width + x] = alpha << 24 | color.R * alpha / 255 << 16 | color.G * alpha / 255 << 8 | color.B * alpha / 255;
            }
        }
        var data = bitmap.LockBits(new Rectangle(Point.Empty, size), ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try { for (var y = 0; y < size.Height; y++) Marshal.Copy(pixels, y * size.Width, data.Scan0 + y * data.Stride, size.Width); }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }
    public void UpdateShadow(Rectangle imageBounds, Color color, float scale)
    {
        var margin = Math.Max(8, (int)Math.Round(16 * scale));
        var bounds = Rectangle.Inflate(imageBounds, margin, margin);
        bounds.Intersect(SystemInformation.VirtualScreen);
        if (bounds.Width <= 0 || bounds.Height <= 0) { Hide(); return; }
        var local = imageBounds; local.Offset(-bounds.X, -bounds.Y);
        using var bitmap = RenderShadow(bounds.Size, local, color, scale);
        var screen = GetDC(IntPtr.Zero); var dc = CreateCompatibleDC(screen); var handle = bitmap.GetHbitmap(Color.FromArgb(0));
        var previous = SelectObject(dc, handle);
        try
        {
            var position = new POINT(bounds.X, bounds.Y); var size = new SIZE(bounds.Width, bounds.Height); var source = new POINT(0, 0);
            var blend = new BLENDFUNCTION { Alpha = 255, Format = 1 };
            UpdateLayeredWindow(Handle, screen, ref position, ref size, dc, ref source, 0, ref blend, 2);
        }
        finally { SelectObject(dc, previous); DeleteObject(handle); DeleteDC(dc); ReleaseDC(IntPtr.Zero, screen); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int Width, Height; public SIZE(int w, int h) { Width = w; Height = h; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLENDFUNCTION { public byte Operation, Flags, Alpha, Format; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref POINT position, ref SIZE size, IntPtr dc, ref POINT source, uint color, ref BLENDFUNCTION blend, uint flags);
}
