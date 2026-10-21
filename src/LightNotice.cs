using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenLine
{
    internal sealed class LightNotice : Window
    {
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };

        public LightNotice(string message)
        {
            Title = "图绳提示";
            AutomationProperties.SetAutomationId(this, "LightNotice");
            Width = 380; SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            FontFamily = new FontFamily("Segoe UI");
            var content = new StackPanel();
            var heading = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            heading.Children.Add(new TextBlock { Text = "截图晾衣绳", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = LightTheme.Brush("#283343"), VerticalAlignment = VerticalAlignment.Center });
            var close = LightTheme.Button("×", Close, "quiet");
            close.Width = close.Height = 24; close.MinWidth = 0; close.Padding = new Thickness(0); close.FontSize = 18;
            close.HorizontalAlignment = HorizontalAlignment.Right;
            AutomationProperties.SetName(close, "关闭提示");
            heading.Children.Add(close); content.Children.Add(heading);
            content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 21, Foreground = LightTheme.Brush("#65717F") });
            var card = LightTheme.Card(content); card.Padding = new Thickness(20, 16, 20, 18);
            Content = card;
            SourceInitialized += delegate { WindowsNative.PrepareOverlay(new WindowInteropHelper(this).Handle); };
            Loaded += delegate { Left = SystemParameters.WorkArea.Right - ActualWidth - 8; Top = SystemParameters.WorkArea.Bottom - ActualHeight - 8; timer.Start(); };
            MouseEnter += delegate { timer.Stop(); };
            MouseLeave += delegate { timer.Start(); };
            timer.Tick += delegate { Close(); };
            Closed += delegate { timer.Stop(); };
        }
    }
}
