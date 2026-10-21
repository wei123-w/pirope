using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ScreenLine
{
    internal static class RopeLine
    {
        public static void Apply(Path rope, LineTheme theme)
        {
            rope.IsHitTestVisible = false;
            rope.StrokeThickness = theme.RopeWidth;
            rope.StrokeStartLineCap = rope.StrokeEndLineCap = PenLineCap.Round;
            if (theme.Id == "classic") { rope.Stroke = LightTheme.Brush(theme.RopeColor); return; }
            double width = theme.Id == "porcelain" ? 4 : 5;
            var fiber = new DrawingGroup();
            fiber.Children.Add(new GeometryDrawing(LightTheme.Brush(theme.RopeColor), null,
                new RectangleGeometry(new Rect(0, 0, width, theme.RopeWidth))));
            var thread = new Pen(LightTheme.Brush(theme.RopeHighlight), theme.Id == "graphite" ? 0.45 : 0.65);
            for (int i = -1; i < 3; i++)
                fiber.Children.Add(new GeometryDrawing(null, thread,
                    new LineGeometry(new Point(i * width / 2, 0), new Point((i + 1) * width / 2, theme.RopeWidth))));
            var brush = new DrawingBrush(fiber) {
                TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, width, theme.RopeWidth), Viewbox = new Rect(0, 0, width, theme.RopeWidth)
            };
            brush.Freeze();
            rope.Stroke = brush;
        }
    }
}
