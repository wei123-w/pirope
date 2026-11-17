using System.Drawing.Imaging;

namespace SnipLite;

internal static partial class Services
{
    public static DataObject ImageClipboardData(Bitmap image, bool bitmap)
    {
        var data = new DataObject();
        var png = new MemoryStream(); image.Save(png, ImageFormat.Png); png.Position = 0;
        data.SetData("PNG", false, png);
        if (bitmap) data.SetData(DataFormats.Bitmap, true, image);
        return data;
    }
    public static void CopyImage(Bitmap image, bool bitmap, AppSettings? settings = null)
    {
        var data = ImageClipboardData(image, bitmap);
        try { Clipboard.SetDataObject(data, true, 5, 80); if (settings?.SoundOnCopy == true) _ = PlaySoundAsync(settings.CaptureSound); }
        finally { if (data.TryGetData<MemoryStream>("PNG", false, out var png)) png?.Dispose(); }
    }
    public static Bitmap? ClipboardImage()
    {
        if (Clipboard.TryGetData<MemoryStream>("PNG", out var png) && png is not null)
        { using (png) using (var image = new Bitmap(png)) return new Bitmap(image); }
        if (!Clipboard.ContainsImage()) return null;
        using var bitmap = Clipboard.GetImage(); return bitmap is null ? null : new Bitmap(bitmap);
    }
    public static string ImageFileName(AppSettings settings, string extension)
    {
        var stem = settings.ExpandFileName(DateTime.Now);
        return new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(stem).ToLowerInvariant())
            ? Path.ChangeExtension(stem, extension) : stem + extension;
    }
    public static string SaveCaptureFile(Bitmap image, AppSettings settings, string folder, string extension = ".png")
    {
        Directory.CreateDirectory(folder);
        var filename = ImageFileName(settings, extension);
        var path = Path.Combine(folder, filename);
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(filename)} ({i}){extension}");
        SaveImage(image, path, extension is ".jpg" or ".jpeg", settings.JpegQuality);
        if (settings.SoundOnSave) _ = PlaySoundAsync(settings.CaptureSound);
        return path;
    }
    public static Task<bool> PlaySoundAsync(string path) => Task.Run(() =>
    {
        try
        {
            using var builtIn = string.IsNullOrWhiteSpace(path)
                ? typeof(Services).Assembly.GetManifestResourceStream("SnipLite.PaperFold.wav")
                    ?? throw new InvalidOperationException(UiText.T("默认截图音效资源缺失。"))
                : null;
            using var player = builtIn is null ? new System.Media.SoundPlayer(path) : new System.Media.SoundPlayer(builtIn);
            player.LoadTimeout = 3000;
            player.Load();
            player.PlaySync();
            return true;
        }
        catch (Exception ex) { AppLog.Write("音效播放失败", ex); return false; }
    });
}
