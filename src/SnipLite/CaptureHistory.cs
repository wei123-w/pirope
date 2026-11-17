using System.Drawing.Imaging;

namespace SnipLite;

internal sealed class CaptureHistory
{
    internal sealed record Entry(byte[] Png, Rectangle Bounds, DateTime Time)
    {
        public Bitmap Image() { using var stream = new MemoryStream(Png); using var image = new Bitmap(stream); return new Bitmap(image); }
        public string Label => $"{Time:HH:mm:ss}  {Bounds.Width} × {Bounds.Height}";
    }
    private readonly List<Entry> _entries = new();
    public IReadOnlyList<Entry> Entries => _entries;
    public void Add(Bitmap image, Rectangle bounds, int limit)
    {
        if (limit == 0) { _entries.Clear(); return; }
        using var stream = new MemoryStream(); image.Save(stream, ImageFormat.Png);
        _entries.Insert(0, new(stream.ToArray(), bounds, DateTime.Now));
        Trim(limit);
    }
    public void Trim(int limit) { if (_entries.Count > limit) _entries.RemoveRange(limit, _entries.Count - limit); }
    public void Clear() => _entries.Clear();
}
