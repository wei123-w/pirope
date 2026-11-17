using System.Security.Cryptography;
using System.Text.Json;

namespace SnipLite;

internal sealed class MicrosoftTranslatorCredentials(string key, string region)
{
    public string Key { get; } = key;
    public string Region { get; } = region;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipLite", "microsoft-translator.dat");

    public static bool HasKey()
    {
        try { return !string.IsNullOrWhiteSpace(Load()?.Key); }
        catch (InvalidOperationException) { return false; }
    }

    public static MicrosoftTranslatorCredentials? Load()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<MicrosoftTranslatorCredentials>(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(UiText.T("无法读取微软翻译密钥，请在首选项中重新配置。"));
        }
    }

    public static void Save(string key, string region)
    {
        if (key.Length == 0)
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            return;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(new MicrosoftTranslatorCredentials(key, region));
        try
        {
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
