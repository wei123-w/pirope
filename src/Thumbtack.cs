using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ScreenLine
{
    internal sealed class Thumbtack : Canvas
    {
        public LineTheme Appearance { get; private set; }

        public Thumbtack()
        {
            Width = Height = 32;
            IsHitTestVisible = false;
        }

        public void Apply(LineTheme theme, int colorIndex)
        {
            Appearance = theme;
            Children.Clear();
            Color color = (Color)ColorConverter.ConvertFromString(theme.ClipColors[colorIndex % theme.ClipColors.Length]);
            Color light = Color.FromRgb((byte)(color.R + (255 - color.R) * 0.65), (byte)(color.G + (255 - color.G) * 0.65), (byte)(color.B + (255 - color.B) * 0.65));
            Color dark = Color.FromRgb((byte)(color.R * 0.67), (byte)(color.G * 0.67), (byte)(color.B * 0.67));
            var finish = new LinearGradientBrush(light, dark, new Point(0, 0), new Point(1, 0.8));
            finish.GradientStops.Insert(1, new GradientStop(color, 0.55));
            finish.Freeze();
            var edge = new SolidColorBrush(dark); edge.Freeze();
            var body = new Canvas { Width = 32, Height = 32, RenderTransform = new RotateTransform(-12, 16, 27) };
            var shadow = new Ellipse { Width = 20, Height = 7, Fill = new SolidColorBrush(Color.FromArgb(35, 34, 44, 54)) };
            body.Children.Add(shadow); SetLeft(shadow, 7.5); SetTop(shadow, 19);
            body.Children.Add(new Path { Data = Geometry.Parse("M15,21 L15.4,28.5 Q16,30 16.6,28.5 L17,21 Z"),
                Fill = new LinearGradientBrush(Color.FromRgb(235, 240, 245), Color.FromRgb(114, 128, 141), 0) });
            body.Children.Add(new Path { Data = Geometry.Parse("M11,8 C12.5,11 13,15 10,18 L22,18 C19,15 19.5,11 21,8 Z"),
                Fill = finish, Stroke = edge, StrokeThickness = 0.6 });
            var foot = new Ellipse { Width = 19, Height = 7, Fill = finish, Stroke = edge, StrokeThickness = 0.6 };
            body.Children.Add(foot); SetLeft(foot, 6.5); SetTop(foot, 16.5);
            var head = new Ellipse { Width = 17, Height = 8, Fill = finish, Stroke = edge, StrokeThickness = 0.6 };
            body.Children.Add(head); SetLeft(head, 7.5); SetTop(head, 3.5);
            body.Children.Add(new Path { Data = Geometry.Parse("M10,6 Q13,4.5 17,5 M9,19 Q13,17.5 17,18"),
                Stroke = Brushes.White, Opacity = 0.65, StrokeThickness = 1, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            Children.Add(body);
        }
    }
}
