using System.Globalization;

namespace SnipLite;

/// <summary>Shared glyph measurements for mouse hit testing, selection and the insertion caret.</summary>
internal static class TextEditLayout
{
    private static StringFormat Format()
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        return format;
    }

    private static List<(int Start, int Length)> Lines(string text)
    {
        var lines = new List<(int, int)>(); var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            lines.Add((start, i - start));
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        lines.Add((start, text.Length - start));
        return lines;
    }

    private static float Advance(Graphics graphics, Font font, string text, StringFormat format) =>
        text.Length == 0 ? 0 : graphics.MeasureString(text, font, PointF.Empty, format).Width;

    public static Rectangle Bounds(Graphics graphics, Font font, string text, Point anchor)
    {
        using var format = Format(); var lines = Lines(text);
        var width = lines.Max(line => Advance(graphics, font, text.Substring(line.Start, line.Length), format));
        return new Rectangle(anchor, new Size(Math.Max(8, (int)Math.Ceiling(width)), (int)Math.Ceiling(lines.Count * font.GetHeight(graphics))));
    }

    public static Rectangle Caret(Graphics graphics, Font font, string text, Point anchor, int index)
    {
        using var format = Format(); var lines = Lines(text);
        index = Math.Clamp(index, 0, text.Length);
        var line = lines.FindLastIndex(line => line.Start <= index);
        var (start, length) = lines[Math.Max(0, line)];
        var x = Advance(graphics, font, text.Substring(start, Math.Min(index - start, length)), format);
        var height = font.GetHeight(graphics);
        return new Rectangle(anchor.X + (int)Math.Round(x), anchor.Y + (int)Math.Round(line * height), 1, (int)Math.Ceiling(height));
    }

    public static int HitTest(Graphics graphics, Font font, string text, Point anchor, Point point)
    {
        using var format = Format(); var lines = Lines(text);
        var line = Math.Clamp((int)Math.Floor((point.Y - anchor.Y) / font.GetHeight(graphics)), 0, lines.Count - 1);
        var (start, length) = lines[line]; var content = text.Substring(start, length);
        // Snap to grapheme boundaries, never between surrogate pairs or a base
        // character and its combining marks. Binary search keeps long pastes responsive.
        var stops = StringInfo.ParseCombiningCharacters(content).Append(length).ToArray();
        var left = 0; var right = stops.Length - 1; var x = point.X - anchor.X;
        while (left < right)
        {
            var middle = (left + right) / 2;
            var boundary = (Advance(graphics, font, content[..stops[middle]], format)
                + Advance(graphics, font, content[..stops[middle + 1]], format)) / 2;
            if (x < boundary) right = middle; else left = middle + 1;
        }
        return start + stops[left];
    }

    public static IEnumerable<RectangleF> Selection(Graphics graphics, Font font, string text, Point anchor, int start, int length)
    {
        using var format = Format(); var height = font.GetHeight(graphics); var lineNumber = 0;
        foreach (var line in Lines(text))
        {
            var from = Math.Max(start, line.Start); var to = Math.Min(start + length, line.Start + line.Length);
            if (to > from)
            {
                var left = Advance(graphics, font, text.Substring(line.Start, from - line.Start), format);
                var right = Advance(graphics, font, text.Substring(line.Start, to - line.Start), format);
                yield return new RectangleF(anchor.X + left, anchor.Y + lineNumber * height, Math.Max(1, right - left), height);
            }
            lineNumber++;
        }
    }

    public static void DrawCaret(Graphics graphics, Rectangle caret, Color color)
    {
        var arm = Math.Clamp(caret.Height / 6, 3, 8);
        var x = caret.X; var bottom = caret.Bottom - 1;
        // A thin I-beam with contrasting edges stays readable over any screenshot.
        foreach (var (ink, width) in new[] { (Color.White, 3f), (color, 1f) })
        {
            using var pen = new Pen(ink, width);
            graphics.DrawLine(pen, x, caret.Top, x, bottom);
            graphics.DrawLine(pen, x - arm, caret.Top, x + arm, caret.Top);
            graphics.DrawLine(pen, x - arm, bottom, x + arm, bottom);
        }
    }
}
