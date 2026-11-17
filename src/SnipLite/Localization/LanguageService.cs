using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SnipLite.Localization;

internal sealed record InterfaceLanguage(string Code, string NativeName);

/// <summary>Offline interface resources, independent of OCR and translation providers.</summary>
internal static class LanguageService
{
    public const string SystemDefault = "system";
    public static IReadOnlyList<InterfaceLanguage> Languages { get; } = Array.AsReadOnly(new[]
    {
        new InterfaceLanguage("en-US", "English"), new("zh-CN", "简体中文"), new("zh-TW", "繁體中文"),
        new("ja", "日本語"), new("ko", "한국어"), new("fr", "Français"), new("de", "Deutsch"), new("es", "Español")
    });
    private static readonly IReadOnlyDictionary<string, string> Sources = Read("sources");
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Catalogs =
        Languages.ToDictionary(l => l.Code, l => (IReadOnlyDictionary<string, string>)Read(l.Code));
    private static string _preference = SystemDefault;
    public static string CurrentLanguage { get; private set; } = Resolve(SystemDefault);
    public static event Action? Changed;

    public static string NormalizePreference(string? preference)
    {
        if (string.IsNullOrWhiteSpace(preference) || preference.Equals(SystemDefault, StringComparison.OrdinalIgnoreCase)) return SystemDefault;
        return Languages.FirstOrDefault(l => l.Code.Equals(preference, StringComparison.OrdinalIgnoreCase))?.Code ?? SystemDefault;
    }
    public static string Resolve(string? preference, string? systemLanguage = null)
    {
        var normalized = NormalizePreference(preference);
        return normalized == SystemDefault ? MatchSystemLanguage(systemLanguage ?? WindowsDisplayLanguage()) : normalized;
    }
    internal static string MatchSystemLanguage(string language)
    {
        var parts = language.Replace('_', '-').ToLowerInvariant().Split('-');
        if (parts[0] == "zh")
            return parts.Contains("hant") || (!parts.Contains("hans") && parts.Any(p => p is "tw" or "hk" or "mo")) ? "zh-TW" : "zh-CN";
        return parts[0] switch { "ja" => "ja", "ko" => "ko", "fr" => "fr", "de" => "de", "es" => "es", _ => "en-US" };
    }
    public static void Configure(string? preference)
    {
        _preference = NormalizePreference(preference);
        var language = Resolve(_preference);
        if (language == CurrentLanguage) return;
        CurrentLanguage = language;
        Changed?.Invoke();
    }
    public static void RefreshSystemLanguage() => Configure(_preference);

    // Source strings are accepted only at UI boundaries, never on recognition or user content.
    public static string Text(string source, string? preference = null) =>
        Sources.TryGetValue(source, out var key) ? Get(key, preference) : source;
    public static string Get(string key, string? preference = null)
    {
        var language = preference is null ? CurrentLanguage : Resolve(preference);
        if (Catalogs[language].TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        return Catalogs["en-US"].GetValueOrDefault(key, key);
    }
    public static string Format(string source, params object?[] values) =>
        string.Format(CultureInfo.GetCultureInfo(CurrentLanguage), Text(source), values);
    private static Dictionary<string, string> Read(string name)
    {
        using var stream = typeof(LanguageService).Assembly.GetManifestResourceStream($"SnipLite.Localization.{name}.json")
            ?? throw new InvalidOperationException($"Missing interface resource: {name}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    private static string WindowsDisplayLanguage()
    {
        // Query UI language rather than the region or process-cached CurrentUICulture.
        if (OperatingSystem.IsWindows())
        {
            try { return CultureInfo.GetCultureInfo(GetUserDefaultUILanguage()).Name; }
            catch (CultureNotFoundException) { }
        }
        return CultureInfo.CurrentUICulture.Name;
    }
    [DllImport("kernel32.dll")] private static extern ushort GetUserDefaultUILanguage();
}
