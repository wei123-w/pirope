using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnipLite;

internal enum ToolKind
{
    None,
    Rectangle,
    Ellipse,
    Arrow,
    Pen,
    Marker,
    Text,
    Mosaic,
    Eraser
}

internal enum ArrowStyle { Solid, Open, Outline, Double, Line, TaperedSolid, TaperedOutline }

internal abstract class Shape
{
    public Color Color = Color.FromArgb(229, 57, 53);
    public float Width = 3f;

    /// <summary>Solid fill instead of an outline (Snipaste's 实心 / 空心).</summary>
    public bool Fill;

    public abstract void Render(Graphics g, Bitmap screen);
    public abstract void DragTo(Point p);
    public virtual void SetStart(Point p) { }
    public virtual bool IsEmpty => false;
    public virtual Shape Clone() => (Shape)MemberwiseClone();

    public static Shape Create(ToolKind tool, Point start, Color color, float width)
    {
        Shape shape = tool switch
        {
            ToolKind.Rectangle => new RectShape(),
            ToolKind.Ellipse => new EllipseShape(),
            ToolKind.Arrow => new ArrowShape(),
            ToolKind.Pen => new PenShape(),
            ToolKind.Marker => new MarkerShape(),
            ToolKind.Mosaic => new MosaicShape(),
            ToolKind.Eraser => new EraserShape(),
            _ => new PenShape()
        };

        shape.Color = color;
        shape.Width = width;
        shape.SetStart(start);
        shape.DragTo(start);
        return shape;
    }
}

internal sealed class RectShape : Shape
{
    public Rectangle Rect;
    private Point _start;

    public override bool IsEmpty => Rect.Width < 2 || Rect.Height < 2;

    public override void SetStart(Point p) { _start = p; Rect = new Rectangle(p, Size.Empty); }

    public override void DragTo(Point p) => Rect = Normalize(_start, p);

    public override void Render(Graphics g, Bitmap screen)
    {
        if (Fill)
        {
            using var brush = new SolidBrush(Color);
            g.FillRectangle(brush, Rect);
            return;
        }

        using var pen = new Pen(Color, Width) { LineJoin = LineJoin.Round };
        g.DrawRectangle(pen, Rect);
    }

    internal static Rectangle Normalize(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

internal sealed class EllipseShape : Shape
{
    public Rectangle Rect;
    private Point _start;

    public override bool IsEmpty => Rect.Width < 2 || Rect.Height < 2;

    public override void SetStart(Point p) { _start = p; Rect = new Rectangle(p, Size.Empty); }

    public override void DragTo(Point p) => Rect = RectShape.Normalize(_start, p);

    public override void Render(Graphics g, Bitmap screen)
    {
        if (Fill)
        {
            using var brush = new SolidBrush(Color);
            g.FillEllipse(brush, Rect);
            return;
        }

        using var pen = new Pen(Color, Width);
        g.DrawEllipse(pen, Rect);
    }
}

internal sealed class ArrowShape : Shape
{
    public Point Start;
    public Point End;
    public ArrowStyle Style;

    public override bool IsEmpty => Math.Abs(End.X - Start.X) < 3 && Math.Abs(End.Y - Start.Y) < 3;

    public override void SetStart(Point p)
    {
        Start = p;
        End = p;
    }

    public override void DragTo(Point p) => End = p;

    public override void Render(Graphics g, Bitmap screen)
    {
        var dx = (float)(End.X - Start.X);
        var dy = (float)(End.Y - Start.Y);
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1f) return;

        var ux = dx / length;
        var uy = dy / length;

        if (Style is ArrowStyle.TaperedSolid or ArrowStyle.TaperedOutline)
        {
            var headLength = Math.Min(length * .35f, Math.Max(16, Width * 5));
            var headHalf = Math.Min(length * .14f, Math.Max(8, Width * 3));
            var neckHalf = Math.Min(headHalf * .35f, Math.Max(1, Width * .65f));
            var bx = End.X - ux * headLength; var by = End.Y - uy * headLength;
            var sideX = -uy; var sideY = ux;
            var neckX = bx + ux * headLength * .18f; var neckY = by + uy * headLength * .18f;
            var points = new[]
            {
                new PointF(Start.X, Start.Y),
                new PointF(neckX + sideX * neckHalf, neckY + sideY * neckHalf),
                new PointF(bx + sideX * headHalf, by + sideY * headHalf),
                new PointF(End.X, End.Y),
                new PointF(bx - sideX * headHalf, by - sideY * headHalf),
                new PointF(neckX - sideX * neckHalf, neckY - sideY * neckHalf)
            };
            if (Style == ArrowStyle.TaperedSolid) { using var fill = new SolidBrush(Color); g.FillPolygon(fill, points); }
            else { using var outline = new Pen(Color, Math.Max(1, Width * .4f)) { LineJoin = LineJoin.Round }; g.DrawPolygon(outline, points); }
            return;
        }

        using var shaft = new Pen(Color, Width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (Style == ArrowStyle.Line) { g.DrawLine(shaft, Start, End); return; }
        if (Style == ArrowStyle.Open)
        {
            g.DrawLine(shaft, Start, End);
            var h = Math.Min(length, Math.Max(12, Width * 4));
            g.DrawLines(shaft, new[]
            {
                new PointF(End.X - ux * h - uy * h * .5f, End.Y - uy * h + ux * h * .5f),
                new PointF(End.X, End.Y),
                new PointF(End.X - ux * h + uy * h * .5f, End.Y - uy * h - ux * h * .5f)
            });
            return;
        }

        var head = Math.Min(length / (Style == ArrowStyle.Double ? 2 : 1), Math.Max(14f, Width * 5f));
        var baseX = End.X - ux * head;
        var baseY = End.Y - uy * head;

        using (var pen = new Pen(Color, Width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(pen, Style == ArrowStyle.Double ? new PointF(Start.X + ux * head, Start.Y + uy * head) : Start, new PointF(baseX, baseY));

        var nx = -uy;
        var ny = ux;

        using var brush = new SolidBrush(Color);
        var polygon = new[]
        {
            new PointF(End.X, End.Y),
            new PointF(baseX + nx * head * 0.42f, baseY + ny * head * 0.42f),
            new PointF(baseX - nx * head * 0.42f, baseY - ny * head * 0.42f)
        };
        if (Style == ArrowStyle.Outline) g.DrawPolygon(shaft, polygon);
        else g.FillPolygon(brush, polygon);
        if (Style == ArrowStyle.Double)
            g.FillPolygon(brush, new[]
            {
                new PointF(Start.X, Start.Y),
                new PointF(Start.X + ux * head + nx * head * .42f, Start.Y + uy * head + ny * head * .42f),
                new PointF(Start.X + ux * head - nx * head * .42f, Start.Y + uy * head - ny * head * .42f)
            });
    }
}

internal sealed class PenShape : Shape
{
    public List<Point> Points = new();
    public override Shape Clone() { var copy = (PenShape)base.Clone(); copy.Points = new(Points); return copy; }

    public override bool IsEmpty => Points.Count < 2;

    public override void SetStart(Point p)
    {
        Points.Clear();
        Points.Add(p);
    }

    public override void DragTo(Point p)
    {
        if (Points.Count > 0 && Math.Abs(Points[^1].X - p.X) < 2 && Math.Abs(Points[^1].Y - p.Y) < 2)
            return;

        Points.Add(p);
    }

    public override void Render(Graphics g, Bitmap screen)
    {
        if (Points.Count < 2) return;

        using var pen = new Pen(Color, Width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        g.DrawLines(pen, Points.ToArray());
    }
}

internal sealed class TextShape : Shape
{
    public Point At;
    public string Text = string.Empty;
    public float FontSize = 14f;
    public string FontFamily = "Microsoft YaHei UI";
    public bool Bold;
    public bool Italic;

    public override bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    public override void SetStart(Point p) => At = p;

    public override void DragTo(Point p)
    {
        At = p;
    }

    public RectangleF Bounds(Graphics g)
    {
        using var font = CreateFont(FontFamily, FontSize, StyleOf(Bold, Italic));
        var size = g.MeasureString(Text, font, PointF.Empty, StringFormat.GenericTypographic);
        return new RectangleF(At, size);
    }

    public static FontStyle StyleOf(bool bold, bool italic) =>
        (bold ? FontStyle.Bold : FontStyle.Regular) | (italic ? FontStyle.Italic : FontStyle.Regular);
    public static Font CreateFont(string family, float size, FontStyle style)
    {
        size = float.IsFinite(size) ? Math.Clamp(size, 4, 200) : 16;
        try { return new Font(family, size, style, GraphicsUnit.Point); }
        catch (ArgumentException) { return new Font("Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Point); }
    }

    public override void Render(Graphics g, Bitmap screen)
    {
        if (IsEmpty) return;

        using var font = CreateFont(FontFamily, FontSize, StyleOf(Bold, Italic));
        using var brush = new SolidBrush(Color);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.DrawString(Text, font, brush, At, StringFormat.GenericTypographic);
    }
}

/// <summary>Pixelates whatever is underneath; sampled from the frozen screenshot so undo is free.</summary>
internal sealed class MosaicShape : StrokeAreaShape
{
    public int Block = 12;

    public override void Render(Graphics g, Bitmap screen)
    {
        using var path = AreaPath();
        var state = g.Save();
        g.SetClip(path, CombineMode.Intersect);
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.None;
        var bounds = Rectangular ? Rect : Rectangle.Ceiling(path.GetBounds());
        var block = Math.Clamp(Block, 2, 256);
        // Keep the grid fixed in screenshot coordinates, including between
        // overlapping brush strokes. Each tile uses its area's average colour.
        var area = Rectangle.Intersect(Rectangle.FromLTRB(
            (int)Math.Floor(bounds.Left / (double)block) * block,
            (int)Math.Floor(bounds.Top / (double)block) * block,
            (int)Math.Ceiling(bounds.Right / (double)block) * block,
            (int)Math.Ceiling(bounds.Bottom / (double)block) * block),
            new Rectangle(0, 0, screen.Width, screen.Height));
        if (area.Width <= 0 || area.Height <= 0) { g.Restore(state); return; }

        var data = screen.LockBits(area, ImageLockMode.ReadOnly, screen.PixelFormat);

        try
        {
            using var brush = new SolidBrush(Color.Black);
            var pixels = new int[area.Width * area.Height];
            for (var row = 0; row < area.Height; row++)
                Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * area.Width, area.Width);

            for (var y = area.Top; y < area.Bottom; y += block)
            {
                for (var x = area.Left; x < area.Right; x += block)
                {
                    var w = Math.Min(block, area.Right - x);
                    var h = Math.Min(block, area.Bottom - y);
                    var r = 0; var gr = 0; var b = 0;
                    for (var yy = y; yy < y + h; yy++)
                    for (var xx = x; xx < x + w; xx++)
                    {
                        var value = pixels[(yy - area.Top) * area.Width + xx - area.Left];
                        b += value & 0xFF; gr += (value >> 8) & 0xFF; r += (value >> 16) & 0xFF;
                    }
                    var count = w * h;
                    brush.Color = Color.FromArgb(255, r / count, gr / count, b / count);
                    g.FillRectangle(brush, x, y, w, h);
                }
            }
        }
        finally
        {
            screen.UnlockBits(data);
            g.Restore(state);
        }
    }
}

/// <summary>Shared physical-pixel geometry for brush and rectangular effects.</summary>
internal abstract class StrokeAreaShape : Shape
{
    public Rectangle Rect;
    public bool Rectangular = true;
    public List<Point> Points = new();
    public override bool IsEmpty => Rectangular ? Rect.Width < 2 || Rect.Height < 2 : Points.Count == 0;
    public override void SetStart(Point p) { Points.Clear(); Points.Add(p); Rect = new Rectangle(p, Size.Empty); }
    public override void DragTo(Point p)
    {
        Rect = RectShape.Normalize(Points[0], p);
        if (Points[^1] != p) Points.Add(p);
    }
    public override Shape Clone() { var copy = (StrokeAreaShape)base.Clone(); copy.Points = new(Points); return copy; }
    public GraphicsPath AreaPath()
    {
        var path = new GraphicsPath();
        if (Rectangular) path.AddRectangle(Rect);
        else if (Points.Count < 2)
        {
            var p = Points.Count == 0 ? Point.Empty : Points[0];
            path.AddEllipse(p.X - Width / 2, p.Y - Width / 2, Width, Width);
        }
        else
        {
            path.AddLines(Points.ToArray());
            using var pen = new Pen(System.Drawing.Color.Black, Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            path.Widen(pen);
        }
        return path;
    }
}

internal sealed class EraserShape : StrokeAreaShape
{
    public override void Render(Graphics g, Bitmap screen)
    {
        using var path = AreaPath();
        var state = g.Save();
        // This graphics target is the transparent annotation layer. Clearing
        // alpha reveals the frozen screenshot without modifying its pixels.
        g.CompositingMode = CompositingMode.SourceCopy;
        using var clear = new SolidBrush(System.Drawing.Color.Transparent);
        g.FillPath(clear, path);
        g.Restore(state);
    }
}

internal sealed class MarkerShape : StrokeAreaShape
{
    public MarkerShape() { Rectangular = false; }
    public int Opacity = 50;
    public override void Render(Graphics g, Bitmap screen)
    {
        var color = System.Drawing.Color.FromArgb((int)Math.Round(255 * Math.Clamp(Opacity, 1, 100) / 100d), Color);
        using var path = AreaPath();
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }
}

internal static class AnnotationRenderer
{
    public static void RenderLayer(Bitmap layer, Rectangle crop, Bitmap screenshot, IEnumerable<Shape> shapes, Shape? current = null)
    {
        using var g = Graphics.FromImage(layer);
        g.Clear(System.Drawing.Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(-crop.X, -crop.Y);
        g.SetClip(crop);
        foreach (var shape in shapes) shape.Render(g, screenshot);
        current?.Render(g, screenshot);
    }
}
