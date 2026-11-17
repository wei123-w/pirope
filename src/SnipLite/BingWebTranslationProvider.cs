using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SnipLite;

internal sealed class BingWebTranslationProvider : ITranslationProvider
{
    public static BingWebTranslationProvider Shared { get; } = new();
    private const string Site = "https://cn.bing.com";
    private static readonly Uri Page = new(Site + "/translator?setlang=zh-cn");
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(12),
        MaxResponseContentBufferSize = 2_097_152
    };
    private readonly SemaphoreSlim _sessionGate = new(1);
    private Session? _session;
    private sealed record Session(string Key, string Token, string Impression, string Instance, DateTimeOffset Expires);

    public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException(UiText.T("没有识别到可翻译的文字。"));
        if (text.Length > 12_000) throw new InvalidOperationException(UiText.T("文字较多，请缩小截图选区后翻译。"));
        source = TranslationLanguages.Normalize(source, true, "en");
        target = TranslationLanguages.Normalize(target, false, "zh-CN");
        if (source == target) return new(text, source);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        try
        {
            var session = await GetSessionAsync(timeout.Token).ConfigureAwait(false);
            var translated = new StringBuilder();
            string? detected = null;
            foreach (var (chunk, separator) in GooglePublicTranslationProvider.SplitText(text))
            {
                if (chunk.Length > 0)
                {
                    var result = await TranslateChunkAsync(session, chunk, source, target, timeout.Token).ConfigureAwait(false);
                    translated.Append(result.Text);
                    detected ??= result.DetectedLanguage;
                }
                translated.Append(separator);
            }
            return new(translated.ToString(), detected);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException(UiText.T("必应网页翻译超时，请稍后重试。")); }
        catch (HttpRequestException)
        { throw new InvalidOperationException(UiText.T("无法连接必应网页翻译，请检查网络。")); }
        catch (JsonException)
        { throw new InvalidOperationException(UiText.T("必应网页翻译返回了无法读取的结果，请稍后重试。")); }
    }

    private async Task<Session> GetSessionAsync(CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _session) is { } current && current.Expires > DateTimeOffset.UtcNow)
                return current;

            using var response = await Client.GetAsync(Page, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response.StatusCode);
            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var values = Regex.Match(html, "params_AbusePreventionHelper\\s*=\\s*(\\[\\s*\\d+\\s*,\\s*\"[^\"]+\"\\s*,\\s*\\d+\\s*\\])").Groups[1].Value;
            var impression = Regex.Match(html, "\\bIG\\s*:\\s*\"([A-Fa-f0-9]+)\"").Groups[1].Value;
            var instance = Regex.Match(html, "data-iid=\"(translator\\.[^\"]+)\"").Groups[1].Value;
            if (values.Length == 0 || impression.Length == 0 || instance.Length == 0)
                throw new InvalidOperationException(UiText.T("必应网页未提供翻译会话，请稍后重试或切换服务。"));

            using var json = JsonDocument.Parse(values);
            var lifetime = TimeSpan.FromMilliseconds(Math.Clamp(json.RootElement[2].GetInt64(), 0, 300_000));
            var session = new Session(json.RootElement[0].GetRawText(), json.RootElement[1].GetString()!,
                impression, instance, DateTimeOffset.UtcNow.Add(lifetime));
            Volatile.Write(ref _session, session);
            return session;
        }
        finally { _sessionGate.Release(); }
    }

    private async Task<TranslationResult> TranslateChunkAsync(Session session, string text, string source,
        string target, CancellationToken cancellationToken)
    {
        var url = Site + "/ttranslatev3?isVertical=1&IG=" + Uri.EscapeDataString(session.Impression)
            + "&IID=" + Uri.EscapeDataString(session.Instance);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["text"] = text,
                ["fromLang"] = source == "auto" ? "auto-detect" : MicrosoftTranslationProvider.ToMicrosoftCode(source),
                ["to"] = MicrosoftTranslationProvider.ToMicrosoftCode(target),
                ["key"] = session.Key,
                ["token"] = session.Token
            })
        };
        request.Headers.Referrer = Page;
        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        CheckSessionStatus(response.StatusCode, session);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var root = json.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("statusCode", out var code)
            && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var status))
            CheckSessionStatus((HttpStatusCode)status, session);
        return MicrosoftTranslationProvider.ParseResult(root, source);
    }

    private void CheckSessionStatus(HttpStatusCode status, Session session)
    {
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            Interlocked.CompareExchange(ref _session, null, session);
        EnsureSuccess(status);
    }

    private static void EnsureSuccess(HttpStatusCode status)
    {
        if (status == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException(UiText.T("必应网页翻译请求过于频繁，请稍后重试。"));
        if ((int)status is < 200 or >= 300)
            throw new InvalidOperationException(Localization.LanguageService.Format("必应网页翻译暂不可用（HTTP {0}），请稍后重试。", (int)status));
    }
}
