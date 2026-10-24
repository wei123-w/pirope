using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ScreenLine
{
    internal static class AiService
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        internal static readonly string[] ProviderNames = new[]
        {
            "\u81EA\u5B9A\u4E49\u63A5\u53E3", "B.AI", "Agnes", "Groq", "Google Gemini", "DeepSeek",
            "\u901A\u4E49\u5343\u95EE\uFF08\u4E2D\u56FD\u7AD9\uFF09", "\u901A\u4E49\u5343\u95EE\uFF08\u56FD\u9645\u7AD9\uFF09", "OpenRouter", "\u7845\u57FA\u6D41\u52A8",
            "Mistral", "\u6708\u4E4B\u6697\u9762\uFF08\u4E2D\u56FD\u7AD9\uFF09", "\u6708\u4E4B\u6697\u9762\uFF08\u56FD\u9645\u7AD9\uFF09", "OpenAI", "Together"
        };
        internal static readonly Dictionary<string, string> ProviderPresets = new Dictionary<string, string>
        {
            { "B.AI", "https://api.b.ai/v1" },
            { "Agnes", "https://apihub.agnes-ai.com/v1" },
            { "Groq", "https://api.groq.com/openai/v1" },
            { "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai" },
            { "DeepSeek", "https://api.deepseek.com" },
            { "\u901A\u4E49\u5343\u95EE\uFF08\u4E2D\u56FD\u7AD9\uFF09", "https://dashscope.aliyuncs.com/compatible-mode/v1" },
            { "\u901A\u4E49\u5343\u95EE\uFF08\u56FD\u9645\u7AD9\uFF09", "https://dashscope-intl.aliyuncs.com/compatible-mode/v1" },
            { "OpenRouter", "https://openrouter.ai/api/v1" },
            { "\u7845\u57FA\u6D41\u52A8", "https://api.siliconflow.cn/v1" },
            { "Mistral", "https://api.mistral.ai/v1" },
            { "\u6708\u4E4B\u6697\u9762\uFF08\u4E2D\u56FD\u7AD9\uFF09", "https://api.moonshot.cn/v1" },
            { "\u6708\u4E4B\u6697\u9762\uFF08\u56FD\u9645\u7AD9\uFF09", "https://api.moonshot.ai/v1" },
            { "OpenAI", "https://api.openai.com/v1" },
            { "Together", "https://api.together.ai/v1" }
        };
        internal static string ProviderEndpoint(string provider)
        {
            string endpoint;
            return provider != null && ProviderPresets.TryGetValue(provider, out endpoint) ? endpoint : null;
        }

        internal static string ProviderForEndpoint(string endpoint)
        {
            string value = (endpoint ?? string.Empty).Trim().TrimEnd('/');
            foreach (var provider in ProviderPresets)
                if (string.Equals(value, provider.Value.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return provider.Key;
            return "\u81EA\u5B9A\u4E49\u63A5\u53E3";
        }

        internal static Task<string> TestConnectionAsync(SavedState settings)
        {
            return TestConnectionAsync(settings, null);
        }

        internal static async Task<string> TestConnectionAsync(SavedState settings, HttpMessageHandler handler)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (string.IsNullOrWhiteSpace(settings.AiEndpoint)) throw new InvalidOperationException("\u8BF7\u586B\u5199\u63A5\u53E3\u5730\u5740\u3002");
            if (string.IsNullOrWhiteSpace(settings.AiModel)) throw new InvalidOperationException("\u8BF7\u586B\u5199\u6A21\u578B\u540D\u79F0\u3002");
            Uri uri;
            if (!Uri.TryCreate(ChatEndpoint(settings.AiEndpoint), UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("\u63A5\u53E3\u5730\u5740\u5FC5\u987B\u662F\u6709\u6548\u7684 HTTP \u6216 HTTPS \u5730\u5740\u3002");
            var body = new Dictionary<string, object>
            {
                { "model", settings.AiModel.Trim() },
                { "messages", new object[] { new Dictionary<string, object> { { "role", "user" }, { "content", "\u53EA\u56DE\u590D OK" } } } },
                { "max_tokens", 8 }
            };
            using (var client = handler == null ? new HttpClient() : new HttpClient(handler, false))
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                using (var request = new HttpRequestMessage(HttpMethod.Post, uri))
                {
                    if (!string.IsNullOrWhiteSpace(settings.AiApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AiApiKey.Trim());
                    request.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false))
                    {
                        string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("\u8FDE\u63A5\u5931\u8D25\uFF08" + (int)response.StatusCode + "\uFF09\uFF1A" + ErrorDetail(content));
                        var result = Json.DeserializeObject(content) as Dictionary<string, object>;
                        var choices = result == null ? null : result["choices"] as object[];
                        var choice = choices == null || choices.Length == 0 ? null : choices[0] as Dictionary<string, object>;
                        var message = choice == null ? null : choice["message"] as Dictionary<string, object>;
                        if (message == null || !message.ContainsKey("content")) throw new InvalidOperationException("\u670D\u52A1\u5DF2\u54CD\u5E94\uFF0C\u4F46\u8FD4\u56DE\u683C\u5F0F\u4E0D\u7B26\u5408\u517C\u5BB9\u63A5\u53E3\u8981\u6C42\u3002");
                        return ReadContent(message["content"]);
                    }
                }
            }
        }

        internal static async Task<string> AskAsync(SavedState settings, Bitmap image, string action, string sourceLanguage = "\u81EA\u52A8\u68C0\u6D4B\u8BED\u8A00", string targetLanguage = "\u4E2D\u6587\u7B80\u4F53")
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.AiEndpoint) || string.IsNullOrWhiteSpace(settings.AiModel))
                throw new InvalidOperationException("\u8BF7\u5148\u5728\u8BBE\u7F6E\u7684 AI \u914D\u7F6E\u9875\u586B\u5199\u63A5\u53E3\u5730\u5740\u548C\u6A21\u578B\u540D\u79F0\u3002");
            if (image == null) throw new ArgumentNullException("image");
            string endpoint = ChatEndpoint(settings.AiEndpoint);
            string prompt = action == "Translate" ? TranslationPrompt(sourceLanguage, targetLanguage) : action == "ExtractText" ? TextExtractionPrompt() : "\u8BF7\u7528\u7B80\u4F53\u4E2D\u6587\u89E3\u91CA\u8FD9\u5F20\u56FE\u7247\u7684\u4E3B\u8981\u5185\u5BB9\u548C\u53EF\u89C1\u6587\u5B57\uFF0C\u91CD\u70B9\u8BF4\u660E\u5173\u952E\u6982\u5FF5\uFF1B\u5982\u679C\u56FE\u7247\u662F\u754C\u9762\u6216\u56FE\u8868\uFF0C\u8BF7\u6309\u7ED3\u6784\u6E05\u6670\u5730\u8BF4\u660E\u3002\u4E0D\u8981\u731C\u6D4B\u770B\u4E0D\u6E05\u7684\u4FE1\u606F\u3002";
            string imageDataUrl = PrepareImageDataUrl(image);
            var textPart = new Dictionary<string, object> { { "type", "text" }, { "text", prompt } };
            var imagePart = BuildImagePart(settings.AiEndpoint, imageDataUrl);
            var message = new Dictionary<string, object> { { "role", "user" }, { "content", new object[] { textPart, imagePart } } };
            var body = new Dictionary<string, object> { { "model", settings.AiModel.Trim() }, { "messages", new object[] { message } } };
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) })
            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                if (!string.IsNullOrWhiteSpace(settings.AiApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AiApiKey.Trim());
                request.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false))
                {
                    string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        string detail = content;
                        try
                        {
                            var parsed = Json.DeserializeObject(content) as Dictionary<string, object>;
                            var error = parsed == null ? null : parsed["error"] as Dictionary<string, object>;
                            if (error != null && error.ContainsKey("message")) detail = Convert.ToString(error["message"]);
                        }
                        catch { }
                        throw new InvalidOperationException("AI \u63A5\u53E3\u8FD4\u56DE\u9519\u8BEF\uFF08" + (int)response.StatusCode + "\uFF09\uFF1A" + detail);
                    }
                    var result = Json.DeserializeObject(content) as Dictionary<string, object>;
                    var choices = result == null ? null : result["choices"] as object[];
                    var choice = choices == null || choices.Length == 0 ? null : choices[0] as Dictionary<string, object>;
                    var responseMessage = choice == null ? null : choice["message"] as Dictionary<string, object>;
                    if (responseMessage == null || !responseMessage.ContainsKey("content")) throw new InvalidOperationException("AI \u63A5\u53E3\u54CD\u5E94\u4E2D\u6CA1\u6709\u53EF\u663E\u793A\u7684\u7ED3\u679C\u3002");
                    return ReadContent(responseMessage["content"]);
                }
            }
        }

        internal static string PrepareImageDataUrl(Bitmap image)
        {
            if (image == null) throw new ArgumentNullException("image");
            const int maximumEdge = 2048;
            Bitmap resized = null;
            Image source = image;
            try
            {
                int longestEdge = Math.Max(image.Width, image.Height);
                if (longestEdge > maximumEdge)
                {
                    double scale = (double)maximumEdge / longestEdge;
                    int width = Math.Max(1, (int)Math.Round(image.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(image.Height * scale));
                    resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                    using (Graphics graphics = Graphics.FromImage(resized))
                    {
                        graphics.Clear(Color.White);
                        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        graphics.DrawImage(image, new Rectangle(0, 0, width, height));
                    }
                    source = resized;
                }

                byte[] png;
                using (var stream = new MemoryStream())
                {
                    source.Save(stream, ImageFormat.Png);
                    png = stream.ToArray();
                }

                ImageCodecInfo jpegCodec = null;
                foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
                    if (codec.MimeType == "image/jpeg") { jpegCodec = codec; break; }
                if (jpegCodec != null)
                {
                    byte[] jpeg;
                    using (var stream = new MemoryStream())
                    using (var parameters = new EncoderParameters(1))
                    {
                        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                        source.Save(stream, jpegCodec, parameters);
                        jpeg = stream.ToArray();
                    }
                    if (jpeg.Length < png.Length * 0.9)
                        return "data:image/jpeg;base64," + Convert.ToBase64String(jpeg);
                }
                return "data:image/png;base64," + Convert.ToBase64String(png);
            }
            finally
            {
                if (resized != null) resized.Dispose();
            }
        }
        static string ErrorDetail(string content)
        {
            try
            {
                var parsed = Json.DeserializeObject(content) as Dictionary<string, object>;
                var error = parsed == null ? null : parsed["error"] as Dictionary<string, object>;
                if (error != null && error.ContainsKey("message")) return Convert.ToString(error["message"]);
            }
            catch { }
            return content;
        }

        internal static string TextExtractionPrompt()
        {
            return "\u8BF7\u8BC6\u522B\u56FE\u7247\u4E2D\u6E05\u6670\u53EF\u89C1\u7684\u6240\u6709\u6587\u5B57\u3002\u53EA\u8FD4\u56DE\u63D0\u53D6\u5230\u7684\u539F\u6587\u6587\u5B57\uFF0C\u5C3D\u91CF\u4FDD\u7559\u539F\u6709\u7684\u6BB5\u843D\u3001\u5217\u8868\u548C\u6362\u884C\u7ED3\u6784\u3002\u4E0D\u8981\u7FFB\u8BD1\u3001\u89E3\u91CA\u3001\u603B\u7ED3\u6216\u8865\u5145\u56FE\u4E2D\u6CA1\u6709\u7684\u6587\u5B57\uFF1B\u770B\u4E0D\u6E05\u7684\u5185\u5BB9\u8BF7\u5FFD\u7565\u3002";
        }
        internal static string TranslationPrompt(string sourceLanguage, string targetLanguage)
        {
            string source = string.IsNullOrWhiteSpace(sourceLanguage) || sourceLanguage == "\u81EA\u52A8\u68C0\u6D4B\u8BED\u8A00"
                ? "\u81EA\u52A8\u8BC6\u522B\u56FE\u7247\u4E2D\u7684\u539F\u6587\u8BED\u8A00"
                : "\u5C06" + sourceLanguage.Trim() + "\u539F\u6587";
            string target = string.IsNullOrWhiteSpace(targetLanguage) ? "\u4E2D\u6587\u7B80\u4F53" : targetLanguage.Trim();
            return "\u8BF7\u8BC6\u522B\u56FE\u7247\u4E2D\u6E05\u6670\u53EF\u89C1\u7684\u6587\u5B57\uFF0C\u5E76\u5C06" + source + "\u7FFB\u8BD1\u6210" + target + "\u3002\u5C3D\u91CF\u4FDD\u7559\u539F\u6709\u6BB5\u843D\u548C\u5217\u8868\uFF1B\u5982\u679C\u6CA1\u6709\u53EF\u7FFB\u8BD1\u6587\u5B57\uFF0C\u8BF7\u7B80\u8981\u8BF4\u660E\u3002\u53EA\u8FD4\u56DE\u8BD1\u6587\uFF0C\u4E0D\u8981\u63CF\u8FF0\u5904\u7406\u8FC7\u7A0B\u3002";
        }        internal static string ChatEndpoint(string endpoint)
        {
            string value = (endpoint ?? string.Empty).Trim().TrimEnd('/');
            if (value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return value;
            return value + "/chat/completions";
        }

        internal static Dictionary<string, object> BuildImagePart(string endpoint, string imageDataUrl)
        {
            Uri uri;
            object imageUrl = Uri.TryCreate(endpoint, UriKind.Absolute, out uri) && string.Equals(uri.Host, "api.mistral.ai", StringComparison.OrdinalIgnoreCase)
                ? (object)imageDataUrl
                : new Dictionary<string, object> { { "url", imageDataUrl } };
            return new Dictionary<string, object> { { "type", "image_url" }, { "image_url", imageUrl } };
        }

        static string ReadContent(object content)
        {
            string text = content as string;
            if (text != null) return text;
            var parts = content as object[];
            if (parts != null)
            {
                var output = new StringBuilder();
                foreach (object item in parts)
                {
                    var part = item as Dictionary<string, object>;
                    if (part != null && part.ContainsKey("text")) output.Append(Convert.ToString(part["text"]));
                }
                if (output.Length > 0) return output.ToString();
            }
            throw new InvalidOperationException("AI \u63A5\u53E3\u54CD\u5E94\u683C\u5F0F\u65E0\u6CD5\u8BC6\u522B\uFF0C\u8BF7\u786E\u8BA4\u63A5\u53E3\u517C\u5BB9\u804A\u5929\u8865\u5168\u683C\u5F0F\u3002");
        }
    }
}

