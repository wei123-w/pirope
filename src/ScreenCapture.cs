using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DrawingPoint = System.Drawing.Point;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfButton = System.Windows.Controls.Button;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace ScreenLine
{
    internal static class ScreenCapture
    {
        public static Bitmap FullScreen(bool all)
        {
            Screen screen = all ? null : Screen.FromPoint(Cursor.Position);
            Rectangle bounds = all ? SystemInformation.VirtualScreen : screen.Bounds;
            return Copy(bounds);
        }

        public static Bitmap ActiveWindow()
        {
            return ActiveWindow(WindowsNative.GetForegroundWindow());
        }

        public static Bitmap ActiveWindow(IntPtr window)
        {
            WindowsNative.Rectangle rect;
            if (window == IntPtr.Zero || window == WindowsNative.GetShellWindow() || !WindowsNative.GetWindowRect(window, out rect))
                throw new InvalidOperationException("无法读取当前活动窗口，请先激活要截图的窗口。");
            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (bounds.Width < 2 || bounds.Height < 2) throw new InvalidOperationException("当前窗口不可截图，请先恢复窗口后重试。");
            return Copy(bounds);
        }

        public static Bitmap Region()
        {
            using (var overlay = new RegionOverlay())
            {
                if (overlay.ShowDialog() != DialogResult.OK || overlay.Selection.Width < 2 || overlay.Selection.Height < 2) return null;
                return Copy(overlay.Selection);
            }
        }

        public static Bitmap Continuous()
        {
            var parts = new System.Collections.Generic.List<Bitmap>();
            try
            {
                while (true)
                {
                    Bitmap part = Region();
                    if (part == null) break;
                    parts.Add(part);
                    var answer = System.Windows.Forms.MessageBox.Show("已截取第 " + parts.Count + " 段。\n选择“是”继续截取，选择“否”完成拼接。", "连续截图拼接", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (answer == DialogResult.Yes) continue;
                    break;
                }
                if (parts.Count == 0) return null;
                int width = parts.Max(part => part.Width);
                long totalHeight = parts.Sum(part => (long)part.Height);
                if (totalHeight > 30000 || (long)width * totalHeight > 100000000) throw new InvalidOperationException("拼接图片过大，请减少截图段数后重试。");
                var result = new Bitmap(width, (int)totalHeight, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(result))
                {
                    graphics.Clear(Color.White);
                    int y = 0;
                    foreach (Bitmap part in parts) { graphics.DrawImageUnscaled(part, 0, y); y += part.Height; }
                }
                return result;
            }
            finally { foreach (Bitmap part in parts) part.Dispose(); }
        }

        static Bitmap Copy(Rectangle bounds)
        {
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }

        sealed class RegionOverlay : Form
        {
            DrawingPoint origin;
            bool selecting;
            public Rectangle Selection { get; private set; }

            public RegionOverlay()
            {
                FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
                Bounds = SystemInformation.VirtualScreen; TopMost = true; ShowInTaskbar = false;
                KeyPreview = true; Cursor = Cursors.Cross; BackColor = Color.Black; Opacity = .38;
                DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
                KeyDown += delegate(object sender, KeyEventArgs args) { if (args.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
                MouseDown += delegate(object sender, MouseEventArgs args)
                {
                    if (args.Button != MouseButtons.Left) return;
                    origin = Cursor.Position; selecting = true; Selection = new Rectangle(origin, System.Drawing.Size.Empty); Invalidate();
                };
                MouseMove += delegate(object sender, MouseEventArgs args)
                {
                    if (!selecting) return;
                    Selection = RectFromPoints(origin, Cursor.Position); Invalidate();
                };
                MouseUp += delegate(object sender, MouseEventArgs args)
                {
                    if (!selecting || args.Button != MouseButtons.Left) return;
                    Selection = RectFromPoints(origin, Cursor.Position); selecting = false; DialogResult = DialogResult.OK; Close();
                };
                Paint += delegate(object sender, PaintEventArgs args)
                {
                    if (selecting || Selection.Width > 0)
                    {
                        Rectangle local = new Rectangle(Selection.Left - Bounds.Left, Selection.Top - Bounds.Top, Selection.Width, Selection.Height);
                        using (var pen = new Pen(Color.DeepSkyBlue, 2)) args.Graphics.DrawRectangle(pen, local);
                        string size = Selection.Width + " × " + Selection.Height;
                        args.Graphics.DrawString(size, Font, Brushes.White, local.Left, Math.Max(0, local.Top - 22));
                    }
                    args.Graphics.DrawString("拖动选择截图区域　·　按 Esc 取消", Font, Brushes.White, 18, 16);
                };
            }

            static Rectangle RectFromPoints(DrawingPoint a, DrawingPoint b)
            {
                return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
            }
        }
    }

    internal sealed class CaptureDialog : Window
    {
        readonly TextBlock countdown = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        readonly WpfComboBox mode = new WpfComboBox { Height = 30, Margin = new Thickness(0, 0, 0, 14) };
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        int remaining;
        public int Mode { get { return mode.SelectedIndex; } }

        CaptureDialog(int seconds)
        {
            remaining = seconds; Title = "延时截图"; Width = 340; Height = 205; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = WpfBrushes.White;
            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(countdown);
            mode.Items.Add("全屏截图（当前显示器）"); mode.Items.Add("框选截图"); mode.Items.Add("活动窗口截图"); mode.SelectedIndex = 0;
            panel.Children.Add(mode);
            var cancel = new WpfButton { Content = "取消", HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Padding = new Thickness(15, 6, 15, 6) };
            cancel.Click += delegate { DialogResult = false; Close(); }; panel.Children.Add(cancel);
            Content = panel;
            timer.Tick += delegate
            {
                remaining--;
                if (remaining <= 0) { timer.Stop(); DialogResult = true; Close(); }
                else UpdateCountdown();
            };
            Loaded += delegate { UpdateCountdown(); timer.Start(); };
            KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs args) { if (args.Key == System.Windows.Input.Key.Escape) { DialogResult = false; Close(); } };
            Closed += delegate { timer.Stop(); };
        }

        void UpdateCountdown() { countdown.Text = "" + remaining + " 秒后开始截图"; }

        public static bool Delay(Window owner, int seconds, out int selectedMode)
        {
            var dialog = new CaptureDialog(Math.Max(1, seconds)) { Owner = owner };
            bool? result = dialog.ShowDialog();
            selectedMode = dialog.Mode;
            return result == true;
        }

        public static bool ChooseFullScreenTarget(Window owner, out bool allDisplays)
        {
            bool result = false, allSelected = false; allDisplays = false;
            var dialog = new Window { Title = "全屏截图", Width = 340, Height = 150, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner, Background = WpfBrushes.White };
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = "选择要截取的屏幕", FontSize = 15, Margin = new Thickness(0, 0, 0, 16) });
            var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var current = new WpfButton { Content = "当前显示器", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
            var all = new WpfButton { Content = "全部显示器", Padding = new Thickness(12, 6, 12, 6) };
            current.Click += delegate { result = true; dialog.DialogResult = true; dialog.Close(); };
            all.Click += delegate { result = true; allSelected = true; dialog.DialogResult = true; dialog.Close(); };
            buttons.Children.Add(current); buttons.Children.Add(all); panel.Children.Add(buttons); dialog.Content = panel;
            dialog.ShowDialog(); allDisplays = allSelected; return result;
        }
    }
}
