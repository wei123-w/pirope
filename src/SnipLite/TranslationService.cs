using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SnipLite;

internal sealed record TranslationLanguage(string Code, string Name);

internal static class TranslationLanguages
{
    public static readonly TranslationLanguage[] All =
    {
        new("auto", "自动检测语言"), new("en", "英语"), new("zh-CN", "中文简体"), new("zh-TW", "中文繁体"),
        new("ja", "日语"), new("ko", "韩语"), new("fr", "法语"), new("de", "德语"), new("es", "西班牙语"),
        new("ru", "俄语"), new("it", "意大利语"), new("pt", "葡萄牙语")
    };

    public static string Normalize(string? code, bool allowAuto, string fallback)
        => All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase) && (allowAuto || l.Code != "auto"))?.Code ?? fallback;
    public static string Name(string? code) => UiText.T(All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))?.Name ?? code ?? "未知语言");
}

internal sealed record TranslationResult(string Text, string? DetectedLanguage);

internal enum TranslationProviderKind { Google, Microsoft, Bing, MyMemory }

internal static class TranslationProviders
{
    public static readonly (TranslationProviderKind Kind, string Label)[] Choices =
    {
        (TranslationProviderKind.Bing, "必应网页翻译"),
        (TranslationProviderKind.Google, "Google"),
        (TranslationProviderKind.MyMemory, "MyMemory"),
        (TranslationProviderKind.Microsoft, "Microsoft Azure")
    };

    public static string Label(TranslationProviderKind kind)
        => UiText.T(Choices.FirstOrDefault(choice => choice.Kind == kind).Label ?? Choices[0].Label);

    public static ITranslationProvider Create(TranslationProviderKind kind) => kind switch
    {
        TranslationProviderKind.Google => new GooglePublicTranslationProvider(),
        TranslationProviderKind.Microsoft => new MicrosoftTranslationProvider(),
        TranslationProviderKind.MyMemory => new MyMemoryTranslationProvider(),
        _ => BingWebTranslationProvider.Shared
    };
}

internal interface ITranslationProvider
{
    Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellationToken);
}

/// <summary>Google's public web endpoint, not the credentialed Cloud Translation API.</summary>
internal sealed class GooglePublicTranslationProvider : ITranslationProvider
{
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 1_048_576
    };
    private readonly HttpClient _client;
    public GooglePublicTranslationProvider(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException(UiText.T("没有识别到可翻译的文字，请调整选区后重试。"));
        if (text.Length > 12_000) throw new InvalidOperationException(UiText.T("文字较多，请缩小截图选区后翻译（最多 12000 字符）。"));
        source = TranslationLanguages.Normalize(source, true, "en");
        target = TranslationLanguages.Normalize(target, false, "zh-CN");
        cancellationToken.ThrowIfCancellationRequested();
        if (source == target) return new(text, source);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        try
        {
            var translated = new StringBuilder();
            string? detected = null;
            foreach (var (chunk, separator) in SplitText(text))
            {
                if (string.IsNullOrWhiteSpace(chunk)) { translated.Append(separator); continue; }
                var url = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t&ie=UTF-8&oe=UTF-8"
                    + $"&sl={Uri.EscapeDataString(source)}&tl={Uri.EscapeDataString(target)}&q={Uri.EscapeDataString(chunk)}";
                using var response = await _client.GetAsync(url, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new InvalidOperationException(UiText.T("翻译请求过于频繁，请稍后重试。"));
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(UiText.T("翻译服务暂时不可用，请稍后重试。"));
                var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                var result = ParseResponse(json);
                translated.Append(result.Text).Append(separator);
                detected ??= result.DetectedLanguage;
            }
            return new(translated.ToString(), detected);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException(UiText.T("翻译请求超时，请检查网络后重试。")); }
        catch (HttpRequestException)
        { throw new InvalidOperationException(UiText.T("无法连接翻译服务，请检查网络后重试。")); }
        catch (JsonException)
        { throw new InvalidOperationException(UiText.T("翻译服务返回了无法读取的结果，请稍后重试。")); }
    }

    internal static TranslationResult ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 || root[0].ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(UiText.T("翻译服务返回了无效结果，请稍后重试。"));
        var text = new StringBuilder();
        foreach (var segment in root[0].EnumerateArray())
            if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0 && segment[0].ValueKind == JsonValueKind.String)
                text.Append(segment[0].GetString());
        if (text.Length == 0) throw new InvalidOperationException(UiText.T("翻译服务未返回译文，请稍后重试。"));
        var detected = root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String ? root[2].GetString() : null;
        return new(text.ToString(), detected);
    }

    // Keep short paragraphs intact; split unusually long ones at whitespace and never inside a surrogate pair.
    internal static IEnumerable<(string Text, string Separator)> SplitText(string text)
    {
        var offset = 0;
        while (offset < text.Length)
        {
            var end = Math.Min(offset + 600, text.Length);
            if (end < text.Length)
            {
                var split = end;
                while (split > offset + 300 && !char.IsWhiteSpace(text[split - 1])) split--;
                if (split > offset + 300) end = split;
                if (char.IsHighSurrogate(text[end - 1])) end--;
            }
            var raw = text[offset..end];
            var trimmed = raw.TrimEnd();
            var trailing = raw[trimmed.Length..];
            if (trimmed.Length > 0) yield return (trimmed, trailing);
            else yield return ("", trailing);
            offset = end;
        }
    }
}
