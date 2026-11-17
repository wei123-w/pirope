namespace SnipLite;

// Compatibility adapter. Language policy and resources live in Localization/.
internal static class UiText
{
    public static string T(string text, string? language = null) => Localization.LanguageService.Text(text, language);
    public static void Apply(Control root, string? language = null) => Localization.LocalizedControls.Apply(root, language);
}
