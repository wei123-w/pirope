using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ScreenLine
{
    internal sealed class AppearanceWindow : Window
    {
        readonly Action<LineTheme> apply;
        readonly List<TextBlock> selectionMarks = new List<TextBlock>();
        internal readonly List<Button> Options = new List<Button>();

        public AppearanceWindow(LineTheme selected, Gallery gallery, Action<LineTheme> apply)
        {
            this.apply = apply;
            Title = "外观";
            Width = 654; Height = 462;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true;
            FontFamily = new FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var body = new StackPanel();
            var heading = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            heading.Children.Add(new TextBlock { Text = "外观", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = LightTheme.Brush("#283343") });
            var close = LightTheme.Button("", Close);
            close.Content = LightTheme.CloseIcon(); close.Width = close.Height = 28; close.MinWidth = 0;
            close.Padding = new Thickness(0); close.Tag = "quiet"; close.HorizontalAlignment = HorizontalAlignment.Right;
            System.Windows.Automation.AutomationProperties.SetName(close, "关闭外观");
            heading.Children.Add(close);
            heading.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args) { if (args.OriginalSource is TextBlock) DragMove(); };
            body.Children.Add(heading);
            body.Children.Add(new TextBlock { Text = "绳子与夹子整套切换", FontSize = 12, Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(0, 0, 0, 18) });
            var choices = new WrapPanel();
            foreach (var theme in LineTheme.All)
            {
                var content = new StackPanel();
                content.Children.Add(Preview(theme, gallery));
                var label = new Grid { Margin = new Thickness(11, 0, 11, 5) };
                label.Children.Add(new TextBlock { Text = theme.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = LightTheme.Brush("#283343") });
                var mark = new TextBlock { Text = "已选", FontSize = 10, Foreground = LightTheme.Brush("#2463AE"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                selectionMarks.Add(mark); label.Children.Add(mark); content.Children.Add(label);
                content.Children.Add(new TextBlock { Text = theme.Description, FontSize = 10,
                    Foreground = LightTheme.Brush("#65717F"), Margin = new Thickness(11, 0, 0, 10) });
                var button = new Button { Content = content, Style = LightTheme.ButtonStyle,
                    Width = 180, Height = 139, Padding = new Thickness(0), Margin = new Thickness(0, 0, 10, 12), Tag = theme };
                System.Windows.Automation.AutomationProperties.SetName(button, theme.Name);
                System.Windows.Automation.AutomationProperties.SetAutomationId(button, "theme-" + theme.Id);
                button.Click += delegate { Select(theme); };
                Options.Add(button); choices.Children.Add(button);
            }
            body.Children.Add(choices);
            var card = LightTheme.Card(body);
            card.Padding = new Thickness(24); Content = card;
            Selected(selected);
            PreviewKeyDown += delegate(object sender, KeyEventArgs args) { if (args.Key == Key.Escape) Close(); };
        }

        void Select(LineTheme theme)
        {
            apply(theme);
            Selected(theme);
        }

        void Selected(LineTheme theme)
        {
            for (int i = 0; i < Options.Count; i++)
            {
                var button = Options[i];
                bool current = button.Tag == theme;
                selectionMarks[i].Visibility = current ? Visibility.Visible : Visibility.Collapsed;
                button.BorderBrush = LightTheme.Brush(current ? "#6E97CB" : "#DCE2EA");
                button.BorderThickness = new Thickness(current ? 2 : 1);
                System.Windows.Automation.AutomationProperties.SetItemStatus(button, current ? "已选择" : "");
            }
        }

        static Canvas Preview(LineTheme theme, Gallery gallery)
        {
            var canvas = new Canvas { Width = 174, Height = 81, IsHitTestVisible = false };
            var rope = new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,20 Q87,30 174,20") };
            RopeLine.Apply(rope, theme); canvas.Children.Add(rope);
            for (int i = 0; i < 3; i++)
            {
                var frame = new Border { Width = 44, Height = 32, CornerRadius = new CornerRadius(4),
                    Background = Brushes.White, BorderBrush = LightTheme.Brush("#DCE2EA"), BorderThickness = new Thickness(0.7), Padding = new Thickness(2) };
                var image = gallery.Items.Skip(i).FirstOrDefault() ?? gallery.Items.FirstOrDefault();
                frame.Child = image == null ? (UIElement)new Border { Background = LightTheme.Brush("#DCE5F1"), CornerRadius = new CornerRadius(2) }
                    : new Image { Source = image.Image.Thumbnail, Stretch = Stretch.Uniform, ClipToBounds = true };
                double x = 6 + i * 59, y = 27 + (i == 1 ? 2 : 0);
                canvas.Children.Add(frame); Canvas.SetLeft(frame, x); Canvas.SetTop(frame, y);
                var clip = new ClothesClip(theme, i) { RenderTransform = new ScaleTransform(0.42, 0.42) };
                canvas.Children.Add(clip); Canvas.SetLeft(clip, x + 22 - 8 * 0.42); Canvas.SetTop(clip, y - 8.4);
            }
            return canvas;
        }
    }
}
