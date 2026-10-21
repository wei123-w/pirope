using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

namespace ScreenLine
{
    internal static class WindowsClipboard
    {
        public static async Task<string> ReadText()
        {
            ExternalException last = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    var data = Clipboard.GetDataObject();
                    if (data == null || data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.Bitmap)
                        || data.GetDataPresent("PNG") || data.GetDataPresent("image/png")) return null;
                    if (data.GetDataPresent(DataFormats.UnicodeText)) return data.GetData(DataFormats.UnicodeText) as string;
                    return data.GetDataPresent(DataFormats.Text) ? data.GetData(DataFormats.Text) as string : null;
                }
                catch (ExternalException error) { last = error; }
                await Task.Delay(60);
            }
            throw last;
        }

        public static async Task Write(object data)
        {
            // 使用 WinForms 的写入路径，避开 WPF 刷新剪贴板失败的问题，并保留原有数据格式。
            ExternalException last = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                try { System.Windows.Forms.Clipboard.SetDataObject(data, true, 1, 0); return; }
                catch (ExternalException error) { last = error; }
                await Task.Delay(60);
            }
            throw last;
        }
    }
}
