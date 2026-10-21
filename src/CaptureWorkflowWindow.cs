using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenLine
{
    internal sealed class CaptureWorkflowWindow : Window
    {
        public CaptureWorkflowWindow(CaptureWorkflowPreset[] presets, Action<CaptureWorkflowPreset> run)
        {
            Title = "截图流程预设"; Width = 340; Height = Math.Min(440, 130 + presets.Length * 48);
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.CanResize; Background = Brushes.White;
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "选择要运行的截图流程", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(5, 5, 5, 12) });
            foreach (var preset in presets.OrderBy(item => item.Name))
            {
                var button = new Button { Content = preset.Name + "　·　" + ActionLabel(preset.Action), Height = 40, Margin = new Thickness(0, 2, 0, 2), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 0, 10, 0) };
                button.Click += delegate { Close(); run(preset); };
                panel.Children.Add(button);
            }
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        static string ActionLabel(string action)
        {
            if (action == "Copy") return "复制"; if (action == "Edit") return "编辑"; if (action == "Hang") return "挂上绳子"; return "预览";
        }
    }
}
