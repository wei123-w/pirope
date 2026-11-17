using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SnipLite;

internal sealed class MyMemoryTranslationProvider(HttpClient? client = null) : ITranslationProvider
{
    private const int RequestByteLimit = 500;
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(12),
        MaxResponseContentBufferSize = 1_048_576
    };

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException(UiText.T("没有识别到可翻译的文字。"));
        if (text.Length > 12_000) throw new InvalidOperationException(UiText.T("文字较多，请缩小截图选区后翻译。"));
        source = TranslationLanguages.Normalize(source, true, "en");
        target = TranslationLanguages.Normalize(target, false, "zh-CN");
        if (source == "auto") throw new InvalidOperationException(UiText.T("请在翻译栏选择源语言。"));
        if (source == target) return new(text, source);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        var languagePair = Uri.EscapeDataString(source + "|" + target);
        var translated = new StringBuilder();
        try
        {
            foreach (var (chunk, separator) in SplitText(text))
            {
                if (chunk.Length > 0)
                {
                    var url = "https://api.mymemory.translated.net/get?langpair=" + languagePair
                        + "&q=" + Uri.EscapeDataString(chunk);
                    using var response = await (client ?? SharedClient).GetAsync(url, timeout.Token).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                        throw new InvalidOperationException(UiText.T("MyMemory 请求受限，请稍后重试或切换服务。"));
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException(Localization.LanguageService.Format("MyMemory 请求失败（HTTP {0}）。", (int)response.StatusCode));
                    translated.Append(ParseResponse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false)));
                }
                translated.Append(separator);
            }
            return new(translated.ToString(), source);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException(UiText.T("MyMemory 翻译请求超时，请稍后重试。")); }
        catch (HttpRequestException)
        { throw new InvalidOperationException(UiText.T("无法连接 MyMemory，请检查网络。")); }
        catch (JsonException)
        { throw new InvalidOperationException(UiText.T("MyMemory 返回了无法读取的结果，请稍后重试。")); }
    }

    private static string ParseResponse(string response)
    {
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
        if (root.TryGetProperty("quotaFinished", out var quota) && quota.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException(UiText.T("MyMemory 当日匿名额度已用完，可切换翻译服务。"));
        if (!root.TryGetProperty("responseStatus", out var status)) throw new JsonException();
        if (status.GetRawText() is "429" or "\"429\"")
            throw new InvalidOperationException(UiText.T("MyMemory 请求受限，请稍后重试或切换服务。"));
        if (status.GetRawText() is not ("200" or "\"200\""))
            throw new InvalidOperationException(UiText.T("MyMemory 暂时无法完成翻译，请稍后重试或切换服务。"));
        if (!root.TryGetProperty("responseData", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("translatedText", out var result) || result.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(result.GetString()))
            throw new JsonException();
        return WebUtility.HtmlDecode(result.GetString()!);
    }

    internal static IEnumerable<(string Text, string Separator)> SplitText(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = start;
            while (end < text.Length && char.IsWhiteSpace(text[end])) end++;
            if (end > start)
            {
                yield return ("", text[start..end]);
                start = end;
                continue;
            }

            var bytes = 0;
            var boundary = start;
            while (end < text.Length && text[end] is not ('\r' or '\n'))
            {
                var valid = Rune.TryGetRuneAt(text, end, out var rune);
                if (!valid) rune = Rune.ReplacementChar;
                if (bytes + rune.Utf8SequenceLength > RequestByteLimit)
                {
                    if (boundary > start) end = boundary;
                    break;
                }
                bytes += rune.Utf8SequenceLength;
                end += rune.Utf16SequenceLength;
                if (bytes >= RequestByteLimit / 2 && (Rune.IsWhiteSpace(rune) || rune.Value is '。' or '！' or '？' or '!' or '?'))
                    boundary = end;
            }

            var raw = text[start..end];
            var chunk = raw.TrimEnd();
            yield return (chunk, raw[chunk.Length..]);
            start = end;
        }
    }
}
