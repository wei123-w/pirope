using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ScreenLine
{
    internal sealed class SnipLiteCaptureResult : IDisposable
    {
        internal Bitmap Image;
        internal bool Pin;
        internal bool Explain;

        public void Dispose() { if (Image != null) Image.Dispose(); }
    }

    internal static class SnipLiteCaptureBridge
    {
        internal static async Task<SnipLiteCaptureResult> CaptureAsync(string executable, string actionOverride = null)
        {
            if (!File.Exists(executable)) throw new FileNotFoundException("找不到 SnipLite 截图组件，请先重新构建图绳。", executable);
            string directory = Path.Combine(Path.GetTempPath(), "PicRope-Capture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(directory));
                string modeArgument = actionOverride == "Translate" ? " --translation" : actionOverride == "ExtractText" ? " --ocr" : actionOverride == "Qr" ? " --qr" : actionOverride == "Explain" ? " --explain" : string.Empty;
                var start = new ProcessStartInfo(executable, "--capture --picrope-bridge:" + encodedPath + modeArgument)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(executable)
                };
                using (var process = Process.Start(start))
                {
                    if (process == null) throw new InvalidOperationException("无法启动 SnipLite 截图组件。");
                    await Task.Run(() => process.WaitForExit()).ConfigureAwait(true);
                    if (process.ExitCode != 0) throw new InvalidOperationException("SnipLite 截图组件异常退出（" + process.ExitCode + "）。");
                }

                string imagePath = Path.Combine(directory, "capture.png");
                string metadataPath = Path.Combine(directory, "result.json");
                if (!File.Exists(imagePath) || !File.Exists(metadataPath)) return null;
                var data = new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(metadataPath));
                using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var source = Image.FromStream(stream))
                    return new SnipLiteCaptureResult
                    {
                        Image = new Bitmap(source),
                        Pin = data != null && data.ContainsKey("pin") && Convert.ToBoolean(data["pin"]),
                        Explain = data != null && data.ContainsKey("explain") && Convert.ToBoolean(data["explain"])
                    };
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }
    }
}
