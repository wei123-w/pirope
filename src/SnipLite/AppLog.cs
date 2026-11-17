namespace SnipLite;

internal static class AppLog
{
    public static string Level { get; set; } = "Normal";
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipLite", "app.log");
    public static void Write(string message, Exception? error = null, bool debug = false)
    {
        if (Level == "Off" || debug && Level != "Debug" || error is null && Level == "Error") return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2_000_000) File.Move(FilePath, FilePath + ".old", true);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{(error is null ? "" : Environment.NewLine + error)}{Environment.NewLine}");
        }
        catch { }
    }
}
