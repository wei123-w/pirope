using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SnipLite;

internal sealed class MicrosoftTranslationProvider(
    HttpClient? client = null, MicrosoftTranslatorCredentials? credentials = null) : ITranslationProvider
{
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(12),
        MaxResponseContentBufferSize = 1_048_576
    };

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(UiText.T("没有识别到可翻译的文字，请调整选区后重试。"));
        if (text.Length > 12_000)
            throw new InvalidOperationException(UiText.T("文字较多，请缩小截图选区后翻译（最多 12000 字符）。"));
        source = TranslationLanguages.Normalize(source, true, "en");
        target = TranslationLanguages.Normalize(target, false, "zh-CN");
        var settings = credentials ?? MicrosoftTranslatorCredentials.Load();
        if (string.IsNullOrWhiteSpace(settings?.Key))
            throw new InvalidOperationException(UiText.T("请在首选项 → 常规 → 配置微软密钥中填写 Translator 密钥。"));
        if (source == target) return new(text, source);

        var url = "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to="
            + Uri.EscapeDataString(ToMicrosoftCode(target));
        if (source != "auto") url += "&from=" + Uri.EscapeDataString(ToMicrosoftCode(source));
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(new[] { new { Text = text } }), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key", settings.Key);
        if (settings.Region.Length > 0) request.Headers.Add("Ocp-Apim-Subscription-Region", settings.Region);

        try
        {
            using var response = await (client ?? SharedClient).SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new InvalidOperationException(UiText.T("微软翻译拒绝了请求，请检查密钥、资源区域和配额。"));
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException(UiText.T("微软翻译请求过于频繁，请稍后重试。"));
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(Localization.LanguageService.Format("微软翻译请求失败（HTTP {0}）。", (int)response.StatusCode));

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return ParseResult(json.RootElement, source);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException(UiText.T("微软翻译请求超时，请稍后重试。")); }
        catch (HttpRequestException)
        { throw new InvalidOperationException(UiText.T("无法连接微软翻译服务，请检查网络。")); }
        catch (JsonException)
        { throw new InvalidOperationException(UiText.T("微软翻译返回了无法读取的结果，请稍后重试。")); }
    }

    internal static TranslationResult ParseResult(JsonElement root, string source)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0
            || root[0].ValueKind != JsonValueKind.Object || !root[0].TryGetProperty("translations", out var translations)
            || translations.ValueKind != JsonValueKind.Array || translations.GetArrayLength() == 0)
            throw new JsonException();
        var translated = ReadString(translations[0], "text");
        if (string.IsNullOrWhiteSpace(translated)) throw new JsonException();
        var detected = root[0].TryGetProperty("detectedLanguage", out var language) ? ReadString(language, "language") : null;
        return new(translated, detected?.ToLowerInvariant() switch
        {
            "zh-hans" => "zh-CN",
            "zh-hant" => "zh-TW",
            _ => detected ?? (source == "auto" ? null : source)
        });
    }

    private static string? ReadString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static string ToMicrosoftCode(string code) => code switch
    {
        "zh-CN" => "zh-Hans",
        "zh-TW" => "zh-Hant",
        _ => code
    };
}
