using System.Drawing.Drawing2D;
namespace SnipLite;
internal static class MagnifierReticle
{
    public static void Draw(Graphics graphics, Rectangle box, RectangleF focus, float scale, Color? accent = null)
    {
        var state = graphics.Save();
        graphics.SetClip(box, CombineMode.Intersect);
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.PixelOffsetMode = PixelOffsetMode.None;
        graphics.CompositingMode = CompositingMode.SourceCopy;
        // Exactly three physical pixels at every DPI; match the capture theme.
        var cx = (int)Math.Floor(focus.Left + focus.Width / 2);
        var cy = (int)Math.Floor(focus.Top + focus.Height / 2);
        var pixel = Rectangle.FromLTRB((int)Math.Floor(focus.Left), (int)Math.Floor(focus.Top), (int)Math.Ceiling(focus.Right), (int)Math.Ceiling(focus.Bottom));
        var theme = accent ?? AnnotationBar.DefaultAccent;
        using var dark = new SolidBrush(Color.FromArgb(theme.R / 4, theme.G / 4, theme.B / 4));
        using var light = new SolidBrush(Color.FromArgb(255, theme));
        void Horizontal(int left, int right)
        {
            if (right <= left) return;
            graphics.FillRectangle(dark, left, cy - 1, right - left, 3);
            graphics.FillRectangle(light, left, cy, right - left, 1);
        }
        void Vertical(int top, int bottom)
        {
            if (bottom <= top) return;
            graphics.FillRectangle(dark, cx - 1, top, 3, bottom - top);
            graphics.FillRectangle(light, cx, top, 1, bottom - top);
        }
        Horizontal(box.Left, pixel.Left - 2); Horizontal(pixel.Right + 2, box.Right);
        Vertical(box.Top, pixel.Top - 2); Vertical(pixel.Bottom + 2, box.Bottom);
        void Ring(Rectangle rect, Brush brush)
        {
            graphics.FillRectangle(brush, rect.Left, rect.Top, rect.Width, 1);
            graphics.FillRectangle(brush, rect.Left, rect.Bottom - 1, rect.Width, 1);
            graphics.FillRectangle(brush, rect.Left, rect.Top, 1, rect.Height);
            graphics.FillRectangle(brush, rect.Right - 1, rect.Top, 1, rect.Height);
        }
        Ring(Rectangle.Inflate(pixel, 2, 2), dark); Ring(Rectangle.Inflate(pixel, 1, 1), light);
        graphics.Restore(state);
    }
}
