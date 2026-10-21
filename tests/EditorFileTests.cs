using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenLine.Tests
{
    internal static class EditorFileTests
    {
        static int checks;
        [STAThread]
        public static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                Reject(null, "缺少导出图片会被拒绝");
                Reject("data:image/jpeg;base64,AA==", "非 PNG 导出会被拒绝");
                Reject("data:image/png;base64,not-base64!", "损坏的 Base64 会被拒绝");
                Reject("data:image/png;base64,iVBORw0KGgo=", "只有 PNG 文件头的损坏图片会被拒绝");
                string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data", "editor-files-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                var bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 0, 80, 140, 240, 255 }, 8);
                byte[] png = ImageContent.EncodeClipboard(bitmap);
                Check(ImageEditFiles.Decode("data:image/png;base64," + Convert.ToBase64String(png)).SequenceEqual(png), "合法 PNG 保留原始像素数据");
                string path = Path.Combine(root, "透明图片.png");
                ImageEditFiles.Save(path, png);
                BitmapSource result;
                using (var stream = File.OpenRead(path))
                    result = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                var pixels = new byte[8];
                new FormatConvertedBitmap(result, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, 8, 0);
                Check(result.PixelWidth == 2 && result.PixelHeight == 1 && pixels[3] == 0, "PNG 保存保留原始尺寸和透明像素");
                byte[] original = File.ReadAllBytes(path);
                try { ImageEditFiles.Save(path, new byte[] { 1, 2, 3 }); throw new Exception("损坏图片未被拒绝"); }
                catch (NotSupportedException) { }
                catch (FileFormatException) { }
                Check(File.ReadAllBytes(path).SequenceEqual(original), "无效编辑结果不会覆盖原图");
                string jpeg = Path.Combine(root, "图片.jpg");
                ImageEditFiles.Save(jpeg, png);
                byte[] encoded = File.ReadAllBytes(jpeg);
                Check(encoded[0] == 255 && encoded[1] == 216 && ImageContent.Read(jpeg).PixelWidth == 2, "JPEG 按真实 JPEG 编码保存，没有伪装成 PNG");
                File.WriteAllBytes(path, png);
                ImageEditFiles.Save(path, png);
                Check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "替换已有图片后不遗留临时文件");
                using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try { ImageEditFiles.Save(path, png); throw new Exception("占用中的原图被覆盖"); }
                    catch (IOException) { }
                }
                Check(File.ReadAllBytes(path).SequenceEqual(original) && !Directory.EnumerateFiles(root, "*.tmp").Any(), "持续文件占用时保留原图并清理临时文件");
                string transparent = Path.Combine(root, "全透明.png");
                var transparentImage = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
                var transparentEncoder = new PngBitmapEncoder();
                transparentEncoder.Frames.Add(BitmapFrame.Create(transparentImage));
                using (var stream = File.Create(transparent)) transparentEncoder.Save(stream);
                using (var stream = new MemoryStream(ImageEditFiles.ReadPng(transparent)))
                {
                    var loaded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                    var alpha = new byte[4];
                    new FormatConvertedBitmap(loaded, PixelFormats.Bgra32, null, 0).CopyPixels(alpha, 4, 0);
                    Check(alpha[3] == 0, "载入全透明原图时不会误套用剪贴板透明修正");
                }
                Console.WriteLine("编辑图片文件测试：{0} 项通过", checks);
                return 0;
            }
            catch (Exception error) { Console.WriteLine("编辑图片文件测试失败：" + error); return 1; }
        }
        static void Reject(string input, string title)
        {
            try { ImageEditFiles.Decode(input); throw new Exception(title); }
            catch (InvalidDataException) { }
            catch (FormatException) { }
            catch (NotSupportedException) { }
            Check(true, title);
        }
        static void Check(bool condition, string title)
        {
            if (!condition) throw new Exception(title);
            checks++;
            Console.WriteLine("通过：" + title);
        }
    }
}
