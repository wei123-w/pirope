using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ScreenLine
{
    internal sealed class WebCaptureWindow : Window
    {
        readonly StateStore store;
        readonly Action<byte[]> completed;
        readonly WebView2 browser = new WebView2();
        readonly TextBox address = new TextBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Text = "https://" };
        readonly TextBlock status = new TextBlock { Text = "输入网址后打开页面。截图仅在本机处理。", VerticalAlignment = VerticalAlignment.Center, Foreground = LightTheme.Brush("#65717F") };
        readonly Button capture = new Button { Content = "截取整页", Padding = new Thickness(12, 6, 12, 6), IsEnabled = false };
        readonly Button cancel = new Button { Content = "取消", Padding = new Thickness(12, 6, 12, 6), IsEnabled = false };
        readonly System.Web.Script.Serialization.JavaScriptSerializer json = new System.Web.Script.Serialization.JavaScriptSerializer();
        bool initialized;
        bool cancelled;

        public WebCaptureWindow(StateStore store, Action<byte[]> completed)
        {
            this.store = store; this.completed = completed;
            Title = "网页整页截图"; Width = 1000; Height = 720; MinWidth = 640; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brush("#F7F8FA");
            var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
            var toolbar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(buttons, Dock.Right); toolbar.Children.Add(buttons);
            var open = new Button { Content = "打开网页", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
            open.Click += delegate { Navigate(); }; buttons.Children.Add(open);
            capture.Click += async delegate { await CapturePage(); }; buttons.Children.Add(capture);
            cancel.Click += delegate { cancelled = true; cancel.IsEnabled = false; status.Text = "正在取消网页截图…"; };
            buttons.Children.Add(cancel);
            toolbar.Children.Add(address);
            var footer = new Border { Padding = new Thickness(8, 6, 8, 6), Background = Brushes.White, Child = status };
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            root.Children.Add(browser);
            Loaded += Initialize;
            address.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs args) { if (args.Key == System.Windows.Input.Key.Enter) Navigate(); };
        }

        async void Initialize(object sender, RoutedEventArgs args)
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(store.Root, "WebCaptureCache"));
                await browser.EnsureCoreWebView2Async(environment);
                browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
                browser.CoreWebView2.NavigationStarting += delegate(object source, CoreWebView2NavigationStartingEventArgs e)
                {
                    Uri target;
                    if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out target) || (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)) e.Cancel = true;
                };
                browser.CoreWebView2.NewWindowRequested += delegate(object source, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
                browser.CoreWebView2.NavigationCompleted += delegate(object source, CoreWebView2NavigationCompletedEventArgs e)
                {
                    capture.IsEnabled = e.IsSuccess;
                    status.Text = e.IsSuccess ? "页面已加载。可等待动态内容完成后截取。" : "页面加载失败，请检查网址或网络后重试。";
                };
                initialized = true;
            }
            catch (Exception error) { status.Text = "无法启动网页截图：" + error.Message; store.Log(error); }
        }

        void Navigate()
        {
            if (!initialized) { status.Text = "网页组件仍在启动，请稍候。"; return; }
            Uri uri;
            string value = address.Text.Trim();
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) value = "https://" + value;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            { status.Text = "网址无效，请输入 http 或 https 地址。"; return; }
            capture.IsEnabled = false; status.Text = "正在加载网页…"; browser.CoreWebView2.Navigate(uri.AbsoluteUri);
        }

        async Task CapturePage()
        {
            if (!initialized || browser.CoreWebView2 == null) return;
            capture.IsEnabled = false; cancel.IsEnabled = true; cancelled = false; status.Text = "正在读取页面尺寸…";
            try
            {
                var metrics = json.Deserialize<Metrics>(await browser.ExecuteScriptAsync("JSON.stringify({height:Math.max(document.documentElement.scrollHeight,document.body.scrollHeight),viewport:window.innerHeight,width:window.innerWidth,ratio:window.devicePixelRatio||1})"));
                if (metrics == null || metrics.height < 1 || metrics.viewport < 1) throw new InvalidOperationException("无法读取网页尺寸。");
                if (metrics.height > 30000 || (long)metrics.width * metrics.height > 100000000) throw new InvalidOperationException("页面过长，超过安全截图尺寸上限；请缩小页面或分段截图。");
                double scale;
                using (var first = await CaptureViewport()) scale = first.Width / (double)metrics.width;
                int outWidth = Math.Max(1, (int)Math.Round(metrics.width * scale));
                int outHeight = Math.Max(1, (int)Math.Ceiling(metrics.height * scale));
                using (var result = new System.Drawing.Bitmap(outWidth, outHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var graphics = System.Drawing.Graphics.FromImage(result))
                {
                    await browser.ExecuteScriptAsync("window.__picropeSticky=[];for(const e of document.querySelectorAll('*')){if(getComputedStyle(e).position==='fixed'||getComputedStyle(e).position==='sticky'){window.__picropeSticky.push([e,e.style.visibility]);e.style.visibility='hidden'}}");
                    int steps = (int)Math.Ceiling(metrics.height / (double)metrics.viewport);
                    for (int i = 0; i < steps; i++)
                    {
                        if (cancelled)
                        {
                            await browser.ExecuteScriptAsync("if(window.__picropeSticky)for(const [e,v] of window.__picropeSticky)e.style.visibility=v;window.scrollTo(0,0)");
                            status.Text = "已取消网页截图。"; capture.IsEnabled = true; return;
                        }
                        int y = (int)Math.Round(i * metrics.viewport);
                        await browser.ExecuteScriptAsync("window.scrollTo(0," + y + ");");
                        await Task.Delay(250);
                        using (var segment = await CaptureViewport())
                        {
                            int destY = (int)Math.Round(y * scale);
                            int remaining = outHeight - destY;
                            int height = Math.Min(segment.Height, remaining);
                            if (height > 0) graphics.DrawImage(segment, new System.Drawing.Rectangle(0, destY, outWidth, height), new System.Drawing.Rectangle(0, 0, Math.Min(segment.Width, outWidth), height), System.Drawing.GraphicsUnit.Pixel);
                        }
                        status.Text = "正在截取网页：" + Math.Min(100, (i + 1) * 100 / steps) + "%";
                    }
                    await browser.ExecuteScriptAsync("if(window.__picropeSticky)for(const [e,v] of window.__picropeSticky)e.style.visibility=v;window.scrollTo(0,0)");
                    using (var memory = new MemoryStream()) { result.Save(memory, System.Drawing.Imaging.ImageFormat.Png); completed(memory.ToArray()); }
                }
                status.Text = "网页截图已加入图绳。"; cancel.IsEnabled = false; capture.IsEnabled = true;
            }
            catch (Exception error)
            {
                RestorePage();
                store.Log(error); status.Text = "网页截图失败：" + error.Message; cancel.IsEnabled = false; capture.IsEnabled = true;
            }
        }

        async Task<System.Drawing.Bitmap> CaptureViewport()
        {
            using (var memory = new MemoryStream())
            {
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, memory);
                memory.Position = 0;
                using (var image = System.Drawing.Image.FromStream(memory)) return new System.Drawing.Bitmap(image);
            }
        }

        async void RestorePage()
        {
            try { await browser.ExecuteScriptAsync("if(window.__picropeSticky)for(const [e,v] of window.__picropeSticky)e.style.visibility=v;window.scrollTo(0,0)"); }
            catch { }
        }

        sealed class Metrics { public double height { get; set; } public double viewport { get; set; } public double width { get; set; } public double ratio { get; set; } }
        static SolidColorBrush Brush(string value) { return (SolidColorBrush)new BrushConverter().ConvertFromString(value); }
    }
}
