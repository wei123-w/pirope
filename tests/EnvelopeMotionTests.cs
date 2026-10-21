using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenLine.Tests
{
    internal static class EnvelopeMotionTests
    {
        static int checks;

        [STAThread]
        public static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                foreach (var theme in LineTheme.All) CheckTheme(theme);
                Console.WriteLine("信封动效测试：{0} 项通过", checks);
            }
            catch (Exception error) { Console.WriteLine("信封动效测试失败：" + error); Environment.ExitCode = 1; }
        }

        static void CheckTheme(LineTheme theme)
        {
            var icon = new EnvelopeIcon { Width = 96, Height = 86 };
            icon.Apply(theme); icon.Measure(new Size(96, 86)); icon.Arrange(new Rect(0, 0, 96, 86)); icon.UpdateLayout();
            Rect[] closed = Papers(icon);
            Check(closed.Length == 3, theme.Name + "使用三张独立信纸");
            byte[] pocket = Pixels(icon);
            icon.Expand(true);
            for (int frame = 0; frame < 48; frame++)
            {
                icon.Frame(1.0 / 60); icon.UpdateLayout();
                Rect[] current = Papers(icon);
                for (int paper = 0; paper < current.Length; paper++)
                {
                    if (Math.Abs(current[paper].Left - closed[paper].Left) > 0.2)
                        throw new Exception(theme.Name + "信纸展开时发生侧向偏移");
                    if (current[paper].Right > icon.BodyBounds.Right - 1 || current[paper].Left < icon.BodyBounds.Left + 1)
                        throw new Exception(theme.Name + "信纸从信封侧边穿出");
                }
                CheckPocket(icon, pocket);
                if (frame == 11) Export(theme.Id + "-抽出中", icon);
            }
            Rect[] opened = Papers(icon);
            bool lifted = true;
            for (int i = 0; i < opened.Length; i++) lifted &= closed[i].Top - opened[i].Top >= 8;
            Check(lifted && icon.Expansion > 0.98, theme.Name + "三张信纸向上抽出，全程不穿出侧边或覆盖前袋");
            Export(theme.Id + "-展开", icon);
            double beforeReverse = icon.Expansion;
            icon.Expand(false);
            Check(icon.Expansion == beforeReverse, theme.Name + "反向收回时保持当前位置");
            for (int i = 0; i < 50; i++) { icon.Frame(1.0 / 60); icon.UpdateLayout(); }
            Rect[] returned = Papers(icon);
            bool restored = true;
            for (int i = 0; i < returned.Length; i++) restored &= Math.Abs(returned[i].Top - closed[i].Top) < 0.2;
            Check(restored && icon.Expansion == 0, theme.Name + "收回后恢复原位");
            Export(theme.Id + "-收起", icon);
            icon.Expand(true); icon.Frame(0.1); icon.Reset(); icon.UpdateLayout();
            Check(icon.Expansion == 0, theme.Name + "隐藏后重置动效");
        }

        static Rect[] Papers(EnvelopeIcon icon)
        {
            var result = new List<Rect>();
            Collect(VisualTreeHelper.GetDrawing(icon), Matrix.Identity, icon.Source, result);
            return result.ToArray();
        }

        static void Collect(Drawing drawing, Matrix parent, ImageSource envelope, List<Rect> result)
        {
            var group = drawing as DrawingGroup;
            if (group != null)
            {
                var transform = group.Transform == null ? Matrix.Identity : group.Transform.Value; transform.Append(parent);
                foreach (var child in group.Children) Collect(child, transform, envelope, result);
            }
            var image = drawing as ImageDrawing;
            if (image != null && image.ImageSource != envelope) result.Add(Rect.Transform(image.Rect, parent));
        }

        static byte[] Pixels(EnvelopeIcon icon)
        {
            var bitmap = new RenderTargetBitmap(96, 86, 96, 96, PixelFormats.Pbgra32); bitmap.Render(icon);
            var pixels = new byte[96 * 86 * 4]; bitmap.CopyPixels(pixels, 96 * 4, 0); return pixels;
        }

        static void CheckPocket(EnvelopeIcon icon, byte[] expected)
        {
            byte[] actual = Pixels(icon); Rect body = icon.BodyBounds;
            for (int y = (int)(body.Top + body.Height * .74); y < body.Top + body.Height * .93; y++)
                for (int x = (int)(body.Left + body.Width * .08); x < body.Right - body.Width * .08; x++)
                    for (int channel = 0; channel < 4; channel++)
                        if (Math.Abs(actual[(y * 96 + x) * 4 + channel] - expected[(y * 96 + x) * 4 + channel]) > 2)
                            throw new Exception("展开时信纸覆盖了信封前袋");
        }

        static void Export(string name, EnvelopeIcon icon)
        {
            var drawing = new DrawingVisual();
            using (var painter = drawing.RenderOpen())
            {
                painter.DrawRectangle(LightTheme.Brush("#EDF1F6"), null, new Rect(0, 0, 288, 330));
                painter.PushTransform(new ScaleTransform(3, 3)); painter.PushTransform(new TranslateTransform(0, 16));
                painter.DrawDrawing(VisualTreeHelper.GetDrawing(icon)); painter.Pop(); painter.Pop();
            }
            var bitmap = new RenderTargetBitmap(288, 330, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string directory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "previews", "文字信封", "抽出动效"));
            Directory.CreateDirectory(directory);
            using (var output = File.Create(Path.Combine(directory, name + ".png"))) encoder.Save(output);
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++; Console.WriteLine("通过：" + message);
        }
    }
}
