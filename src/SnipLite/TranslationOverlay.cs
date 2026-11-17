using System.Drawing.Imaging;
using System.Text;

namespace SnipLite;

internal sealed record TranslationBlock(string Text, RectangleF Bounds, int LineCount);

/// <summary>Map translated paragraphs back to the screenshot, without modifying the original pixels.</summary>
internal static class TranslationOverlay
{
    public static IReadOnlyList<TranslationBlock> GetBlocks(OcrDocument document)
    {
        var lines = new List<TranslationBlock>();
        var text = new StringBuilder(); var bounds = RectangleF.Empty;
        void Flush()
        {
            if (!string.IsNullOrWhiteSpace(text.ToString()) && bounds.Width > 0 && bounds.Height > 0)
                lines.Add(new(text.ToString(), lines.Count < document.LineBounds.Count ? document.LineBounds[lines.Count] : bounds, 1));
            text.Clear(); bounds = RectangleF.Empty;
        }
        foreach (var character in document.Characters)
        {
            if (character.Prefix.Contains('\n')) Flush();
            else if (text.Length > 0) text.Append(character.Prefix);
            text.Append(character.Text);
            bounds = bounds.IsEmpty ? character.Bounds : RectangleF.Union(bounds, character.Bounds);
        }
        Flush();
        var blocks = new List<TranslationBlock>();
        foreach (var line in lines)
        {
            if (blocks.Count > 0)
            {
                var previous = blocks[^1];
                var lineHeight = previous.Bounds.Height / previous.LineCount;
                var gap = line.Bounds.Top - previous.Bounds.Bottom;
                // Merge wrapped paragraphs, but keep distinct labels and separate columns apart.
                if (gap >= -lineHeight * .2f && gap <= lineHeight * .8f
                    && Math.Abs(line.Bounds.Left - previous.Bounds.Left) <= lineHeight * .65f)
                {
                    blocks[^1] = new(previous.Text + Environment.NewLine + line.Text,
                        RectangleF.Union(previous.Bounds, line.Bounds), previous.LineCount + 1);
                    continue;
                }
            }
            blocks.Add(line);
        }
        return blocks;
    }

    public static Bitmap Render(Bitmap source, IReadOnlyList<TranslationBlock> blocks,
        IReadOnlyList<TranslationResult> results, CancellationToken cancellationToken = default)
    {
        var layer = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using var graphics = Graphics.FromImage(layer);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);
            for (var i = 0; i < Math.Min(blocks.Count, results.Count); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var block = blocks[i]; var text = results[i].Text;
                if (string.IsNullOrWhiteSpace(text) || text.Trim() == block.Text.Trim()) continue;
                var area = Rectangle.Intersect(Rectangle.Ceiling(block.Bounds), new Rectangle(Point.Empty, source.Size));
                if (area.Width < 1 || area.Height < 1) continue;
                var padding = Math.Clamp((int)Math.Ceiling(block.Bounds.Height / Math.Max(1, block.LineCount) * .18f), 2, 8);
                var cover = Rectangle.Intersect(Rectangle.Inflate(area, padding, padding), new Rectangle(Point.Empty, source.Size));
                var background = SampleBackground(source, cover);
                var foreground = SampleForeground(source, area, background);
                using (var fill = new SolidBrush(background)) graphics.FillRectangle(fill, cover);
                using var format = new StringFormat { Trimming = StringTrimming.None, FormatFlags = StringFormatFlags.LineLimit };
                using var font = FitFont(graphics, text, area.Size, block.Bounds.Height / Math.Max(1, block.LineCount), format);
                using var ink = new SolidBrush(foreground);
                // GDI+ preserves alpha in the PArgb layer; GDI TextRenderer can punch transparent holes into it.
                graphics.DrawString(text, font, ink, area, format);
            }
            return layer;
        }
        catch { layer.Dispose(); throw; }
    }

    private static Font FitFont(Graphics graphics, string text, Size size, float lineHeight, StringFormat format)
    {
        var low = 4f; var high = Math.Clamp(lineHeight, 6, 96);
        for (var i = 0; i < 8; i++)
        {
            var candidate = (low + high) / 2;
            using var font = new Font("Microsoft YaHei UI", candidate, GraphicsUnit.Pixel);
            var measured = graphics.MeasureString(text, font, new SizeF(size.Width, 1_000_000), format);
            if (measured.Height <= size.Height && measured.Width <= size.Width) low = candidate;
            else high = candidate;
        }
        return new Font("Microsoft YaHei UI", low, GraphicsUnit.Pixel);
    }

    private static Color SampleBackground(Bitmap image, Rectangle bounds)
    {
        var samples = new List<Color>();
        var stepX = Math.Max(1, bounds.Width / 24); var stepY = Math.Max(1, bounds.Height / 16);
        for (var x = bounds.Left; x < bounds.Right; x += stepX)
        {
            samples.Add(image.GetPixel(x, bounds.Top)); samples.Add(image.GetPixel(x, bounds.Bottom - 1));
        }
        for (var y = bounds.Top; y < bounds.Bottom; y += stepY)
        {
            samples.Add(image.GetPixel(bounds.Left, y)); samples.Add(image.GetPixel(bounds.Right - 1, y));
        }
        return Median(samples);
    }

    private static Color SampleForeground(Bitmap image, Rectangle bounds, Color background)
    {
        var samples = new List<Color>();
        var step = Math.Max(1, (int)Math.Sqrt((long)bounds.Width * bounds.Height / 1500d));
        for (var y = bounds.Top; y < bounds.Bottom; y += step)
        for (var x = bounds.Left; x < bounds.Right; x += step)
        {
            var color = image.GetPixel(x, y);
            var contrast = Math.Abs(color.R - background.R) + Math.Abs(color.G - background.G) + Math.Abs(color.B - background.B);
            if (contrast > 180) samples.Add(color);
        }
        return samples.Count == 0 ? background.GetBrightness() < .5 ? Color.White : Color.FromArgb(35, 35, 35) : Median(samples);
    }

    private static Color Median(List<Color> colors) => Color.FromArgb(
        colors.Select(c => c.R).Order().ElementAt(colors.Count / 2),
        colors.Select(c => c.G).Order().ElementAt(colors.Count / 2),
        colors.Select(c => c.B).Order().ElementAt(colors.Count / 2));
}
