using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ScreenLine
{
    internal sealed class RecentCapturesWindow : Window
    {
        readonly StateStore store;
        readonly ListBox entries = new ListBox();
        readonly TextBox search = new TextBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 0, 10) };
        readonly Action<string, CaptureHistoryItem> action;

        public RecentCapturesWindow(StateStore store, Action<string, CaptureHistoryItem> action)
        {
            this.store = store; this.action = action;
            Title = "最近截图"; Width = 560; Height = 480; MinWidth = 420; MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brush("#F7F8FA");
            var panel = new DockPanel { Margin = new Thickness(18) }; Content = panel;
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
            var open = Button("打开"); open.Click += delegate { Invoke("open"); }; footer.Children.Add(open);
            var copy = Button("复制"); copy.Margin = new Thickness(8, 0, 0, 0); copy.Click += delegate { Invoke("copy"); }; footer.Children.Add(copy);
            var forget = Button("移除记录"); forget.Margin = new Thickness(8, 0, 0, 0); forget.Click += Forget; footer.Children.Add(forget);
            DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
            panel.Children.Add(entries);
            search.TextChanged += delegate { Refresh(); };
            entries.MouseDoubleClick += delegate { Invoke("open"); };
            var template = new DataTemplate(typeof(CaptureHistoryItem));
            var row = new FrameworkElementFactory(typeof(StackPanel)); row.SetValue(StackPanel.MarginProperty, new Thickness(5));
            var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding("Mode")); title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); row.AppendChild(title);
            var path = new FrameworkElementFactory(typeof(TextBlock)); path.SetBinding(TextBlock.TextProperty, new Binding("Path")); path.SetValue(TextBlock.FontSizeProperty, 11.0); path.SetValue(TextBlock.ForegroundProperty, Brush("#65717F")); row.AppendChild(path);
            var time = new FrameworkElementFactory(typeof(TextBlock)); time.SetBinding(TextBlock.TextProperty, new Binding("CreatedUtc") { StringFormat = "{}{0:yyyy-MM-dd HH:mm:ss} UTC" }); time.SetValue(TextBlock.FontSizeProperty, 10.0); time.SetValue(TextBlock.ForegroundProperty, Brush("#65717F")); row.AppendChild(time);
            template.VisualTree = row; entries.ItemTemplate = template;
            Refresh();
        }

        void Refresh()
        {
            string query = search.Text.Trim();
            entries.ItemsSource = null;
            entries.ItemsSource = store.State.RecentCaptures.Where(item => string.IsNullOrEmpty(query)
                || item.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (item.Mode ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        }

        void Invoke(string name)
        {
            var selected = entries.SelectedItem as CaptureHistoryItem;
            if (selected != null) action(name, selected);
        }

        void Forget(object sender, RoutedEventArgs args)
        {
            var selected = entries.SelectedItem as CaptureHistoryItem;
            if (selected == null) return;
            store.State.RecentCaptures.Remove(selected); store.Save(); Refresh();
        }

        static Button Button(string text) { return new Button { Content = text, Padding = new Thickness(12, 6, 12, 6), Background = Brushes.White }; }
        static SolidColorBrush Brush(string value) { return (SolidColorBrush)new BrushConverter().ConvertFromString(value); }
    }
}
