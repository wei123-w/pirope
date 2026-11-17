namespace SnipLite;

internal static class AppIcon
{
    // Shared for window captions; callers must not dispose this application-lifetime icon.
    public static Icon Window { get; } = Load(32);

    public static Icon Load(int size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("SnipLite.AppIcon.ico")
            ?? throw new InvalidOperationException("The application icon is missing.");
        using var icon = new Icon(stream, size, size);
        return (Icon)icon.Clone();
    }
}
