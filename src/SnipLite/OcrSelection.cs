using System.Globalization;
using System.Text;

namespace SnipLite;

internal sealed record RecognizedWord(string Text, RectangleF Bounds, int Line);
internal sealed record RecognizedCharacter(string Text, RectangleF Bounds, string Prefix);

/// <summary>OCR geometry is relative to the original crop, in physical pixels.</summary>
internal sealed class OcrDocument
{
    public IReadOnlyList<RecognizedCharacter> Characters { get; }
    public IReadOnlyList<RectangleF> LineBounds { get; }
    public string Text => GetText(0, Characters.Count - 1);

    private OcrDocument(IReadOnlyList<RecognizedCharacter> characters, IReadOnlyList<RectangleF> lineBounds)
    { Characters = characters; LineBounds = lineBounds; }

    internal static OcrDocument FromCharacters(IEnumerable<RecognizedCharacter> characters, IEnumerable<RectangleF>? lineBounds = null)
        => new(characters.ToArray(), lineBounds?.ToArray() ?? Array.Empty<RectangleF>());

    public OcrDocument(IEnumerable<RecognizedWord> words)
    {
        var characters = new List<RecognizedCharacter>();
        var lineBounds = new Dictionary<int, RectangleF>();
        RecognizedWord? previous = null;
        using var measure = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(measure);
        using var font = new Font("Microsoft YaHei UI", 16, GraphicsUnit.Pixel);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

        foreach (var word in words)
        {
            if (string.IsNullOrEmpty(word.Text)) continue;
            lineBounds[word.Line] = lineBounds.TryGetValue(word.Line, out var line) ? RectangleF.Union(line, word.Bounds) : word.Bounds;
            var prefix = previous is null ? "" : previous.Line != word.Line
                ? Environment.NewLine : IsChinese(previous.Text[^1]) && IsChinese(word.Text[0]) ? "" : " ";
            var elements = new List<string>();
            var enumerator = StringInfo.GetTextElementEnumerator(word.Text);
            while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());
            var widths = elements.Select(t => Math.Max(1f, graphics.MeasureString(t, font, PointF.Empty, format).Width)).ToArray();
            var total = widths.Sum();
            var x = word.Bounds.Left;
            for (var i = 0; i < elements.Count; i++)
            {
                var width = word.Bounds.Width * widths[i] / total;
                characters.Add(new(elements[i], new RectangleF(x, word.Bounds.Y, width, word.Bounds.Height), i == 0 ? prefix : ""));
                x += width;
            }
            previous = word;
        }
        Characters = characters;
        LineBounds = lineBounds.Values.ToArray();
    }

    private static bool IsChinese(char value) => value is >= '\u4e00' and <= '\u9fff';

    public string GetText(int start, int end)
    {
        if (start < 0 || end < 0 || Characters.Count == 0) return "";
        var first = Math.Clamp(Math.Min(start, end), 0, Characters.Count - 1);
        var last = Math.Clamp(Math.Max(start, end), 0, Characters.Count - 1);
        var result = new StringBuilder();
        for (var i = first; i <= last; i++)
        {
            if (i > first) result.Append(Characters[i].Prefix);
            result.Append(Characters[i].Text);
        }
        return result.ToString();
    }

    public int HitTest(PointF point, bool nearest = false, float tolerance = 0)
    {
        var index = -1;
        var distance = float.MaxValue;
        for (var i = 0; i < Characters.Count; i++)
        {
            var bounds = Characters[i].Bounds;
            if (bounds.Contains(point)) return i;
            var dx = Math.Max(bounds.Left - point.X, Math.Max(0, point.X - bounds.Right));
            var dy = Math.Max(bounds.Top - point.Y, Math.Max(0, point.Y - bounds.Bottom));
            var candidate = dx * dx + dy * dy;
            if (candidate < distance) { distance = candidate; index = i; }
        }
        return nearest || distance <= tolerance * tolerance ? index : -1;
    }
}
