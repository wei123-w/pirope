using System.Text.RegularExpressions;
using System.Globalization;
using System.Net;

namespace SnipLite;

internal static class TextImageRenderer
{
    private sealed record Run(string Text, FontStyle Style);
    public static bool IsFilePath(string value)
    {
        var text = value.Trim().Trim('"');
        return File.Exists(text) || Directory.Exists(text) || Regex.IsMatch(text, @"^(?:[a-zA-Z]:[\\/]|\\\\|file://)");
    }
    public static Bitmap Render(string text, string? html, AppSettings settings)
    {
        var runs = html is not null && !settings.IgnoreHtml ? ParseHtml(html) : new List<Run> { new(text, FontStyle.Regular) };
        if (runs.Count == 0) runs.Add(new(text, FontStyle.Regular));
        var margin = Math.Min(settings.TextImageMargin, (settings.TextImageMaxWidth - 20) / 2);
        var maxWidth = settings.TextImageMaxWidth - margin * 2;
        using var measure = new Bitmap(1, 1);
        using var g = Graphics.FromImage(measure);
        using var normal = new Font(settings.TextImageFont, settings.TextImageFontSize);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        var lineHeight = (int)Math.Ceiling(normal.GetHeight(g) * 1.15);
        var layout = new List<(string Text, FontStyle Style, Point At)>();
        var x = 0; var y = 0; var width = 1;
        foreach (var run in runs)
        {
            using var font = new Font(settings.TextImageFont, settings.TextImageFontSize, run.Style);
            var elements = StringInfo.GetTextElementEnumerator(run.Text.Replace("\r", ""));
            while (elements.MoveNext())
            {
                var element = elements.GetTextElement();
                if (element == "\n") { width = Math.Max(width, x); x = 0; y += lineHeight; continue; }
                var advance = Math.Max(1, (int)Math.Ceiling(g.MeasureString(element, font, PointF.Empty, format).Width));
                if (x > 0 && x + advance > maxWidth) { width = Math.Max(width, x); x = 0; y += lineHeight; }
                // Bound allocations for exceptionally large clipboard documents.
                if (y + lineHeight + margin * 2 > 30000) throw new InvalidOperationException(UiText.T("文字过长，请分段转换为贴图。"));
                layout.Add((element, run.Style, new Point(x + margin, y + margin)));
                x += advance;
            }
        }
        width = Math.Max(width, x);
        var image = new Bitmap(Math.Min(settings.TextImageMaxWidth, width + margin * 2),
            Math.Max(1, y + lineHeight + margin * 2));
        using var output = Graphics.FromImage(image);
        output.Clear(Color.FromArgb(settings.TextImageBackground));
        output.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(Color.FromArgb(settings.TextImageForeground));
        var fonts = layout.Select(item => item.Style).Distinct().ToDictionary(style => style, style => new Font(settings.TextImageFont, settings.TextImageFontSize, style));
        try { foreach (var item in layout) output.DrawString(item.Text, fonts[item.Style], brush, item.At, format); }
        finally { foreach (var font in fonts.Values) font.Dispose(); }
        return image;
    }
    private static List<Run> ParseHtml(string html)
    {
        var start = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        var end = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start) html = html[(start + 20)..end];
        else { var first = html.IndexOf('<'); if (first > 0) html = html[first..]; }
        html = Regex.Replace(html, @"<(script|style)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        var result = new List<Run>(); var bold = 0; var italic = 0; var underline = 0;
        foreach (var part in Regex.Split(html, "(<[^>]+>)"))
        {
            if (!part.StartsWith('<'))
            {
                var text = WebUtility.HtmlDecode(Regex.Replace(part, @"\s+", " "));
                if (text.Length > 0) result.Add(new(text, (bold > 0 ? FontStyle.Bold : 0) | (italic > 0 ? FontStyle.Italic : 0) | (underline > 0 ? FontStyle.Underline : 0)));
                continue;
            }
            var match = Regex.Match(part, @"^<\s*(/?)\s*([\w]+)");
            var closing = match.Groups[1].Value.Length > 0; var name = match.Groups[2].Value.ToLowerInvariant();
            var delta = closing ? -1 : 1;
            if (name is "b" or "strong") bold = Math.Max(0, bold + delta);
            if (name is "i" or "em") italic = Math.Max(0, italic + delta);
            if (name == "u") underline = Math.Max(0, underline + delta);
            if (name == "br" || closing && name is "p" or "div" or "li") result.Add(new("\n", FontStyle.Regular));
        }
        return result;
    }
}
