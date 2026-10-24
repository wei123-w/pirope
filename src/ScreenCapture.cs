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

        public static Bitmap Region(bool annotate = true, System.Collections.Generic.IDictionary<string, string> hotkeys = null, string initialAiAction = null, System.Func<Bitmap, string, string, string, System.Threading.Tasks.Task<string>> aiAction = null)
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            using (var desktop = Copy(bounds))
            using (var overlay = new RegionOverlay(desktop, bounds, annotate, hotkeys, aiAction, initialAiAction))
            {
                if (overlay.ShowDialog() != DialogResult.OK || overlay.Result == null) return null;
                return (Bitmap)overlay.Result.Clone();
            }
        }

        public static Bitmap Continuous()
        {
            var parts = new System.Collections.Generic.List<Bitmap>();
            try
            {
                while (true)
                {
                    Bitmap part = Region(false);
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

        internal static Bitmap Copy(Rectangle bounds)
        {
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }

        internal static Bitmap RenderRegionFrame(Bitmap desktop, Rectangle desktopBounds, Rectangle selection, Color shade, float opacity)
        {
            if (desktop == null) throw new ArgumentNullException("desktop");
            if (desktop.Size != desktopBounds.Size) throw new ArgumentException("Desktop snapshot bounds must match its pixel size.", "desktopBounds");
            if (float.IsNaN(opacity) || opacity < 0 || opacity > 1) throw new ArgumentOutOfRangeException("opacity");
            var result = new Bitmap(desktop.Width, desktop.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.DrawImageUnscaled(desktop, 0, 0);
                Rectangle local = Rectangle.Intersect(new Rectangle(selection.X - desktopBounds.X, selection.Y - desktopBounds.Y, selection.Width, selection.Height), new Rectangle(0, 0, desktop.Width, desktop.Height));
                using (var outside = new Region(new Rectangle(0, 0, desktop.Width, desktop.Height)))
                {
                    if (local.Width > 0 && local.Height > 0) outside.Exclude(local);
                    graphics.SetClip(outside, System.Drawing.Drawing2D.CombineMode.Replace);
                    using (var brush = new SolidBrush(Color.FromArgb((int)Math.Round(opacity * 255), shade))) graphics.FillRectangle(brush, 0, 0, desktop.Width, desktop.Height);
                    graphics.ResetClip();
                }
            }
            return result;
        }

        internal static Bitmap CropRegion(Bitmap desktop, Rectangle desktopBounds, Rectangle selection)
        {
            if (desktop == null) throw new ArgumentNullException("desktop");
            if (desktop.Size != desktopBounds.Size) throw new ArgumentException("Desktop snapshot bounds must match its pixel size.", "desktopBounds");
            Rectangle local = new Rectangle(selection.X - desktopBounds.X, selection.Y - desktopBounds.Y, selection.Width, selection.Height);
            local = Rectangle.Intersect(local, new Rectangle(0, 0, desktop.Width, desktop.Height));
            if (local.Width < 2 || local.Height < 2) throw new ArgumentOutOfRangeException("selection");
            return desktop.Clone(local, PixelFormat.Format32bppArgb);
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
