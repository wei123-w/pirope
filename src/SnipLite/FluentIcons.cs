using System.Drawing.Drawing2D;

namespace SnipLite;

/// <summary>Resolution-independent 24-unit line icons shared by navigation, menus and capture tools.</summary>
internal static class FluentIcons
{
    public static bool Supports(string id) => id is "rect" or "ellipse" or "arrow" or "pen" or "marker" or "eraser" or "text" or "mosaic" or "ocr" or "qr" or "translate" or "explain" or "undo" or "redo" or "close" or "pin" or "save" or "copy";

    public static void Draw(Graphics g, string id, RectangleF bounds, Color color)
    {
        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
            using var pen = new Pen(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            void Line(float x1, float y1, float x2, float y2) => g.DrawLine(pen, x1, y1, x2, y2);
            void Lines(params PointF[] points) => g.DrawLines(pen, points);
            void Box(float x, float y, float w, float h, float r = 2) { using var path = FluentDrawing.Rounded(new RectangleF(x, y, w, h), r); g.DrawPath(pen, path); }
            void Corners()
            {
                Lines(new(8, 3), new(3, 3), new(3, 8)); Lines(new(16, 3), new(21, 3), new(21, 8));
                Lines(new(3, 16), new(3, 21), new(8, 21)); Lines(new(16, 21), new(21, 21), new(21, 16));
            }
            switch (id)
            {
                case "chevron": Lines(new(7, 10), new(12, 15), new(17, 10)); break;
                case "rect": Box(4, 5, 16, 14); break;
                case "ellipse": g.DrawEllipse(pen, 4, 5, 16, 14); break;
                case "arrow": Line(5, 19, 19, 5); Lines(new(10, 5), new(19, 5), new(19, 14)); break;
                case "pen": Lines(new(4, 16), new(15, 5), new(19, 9), new(8, 20), new(3, 21), new(4, 16)); Line(13, 7, 17, 11); break;
                case "marker": Lines(new(4, 14), new(15, 3), new(21, 9), new(10, 20), new(4, 14)); Line(3, 21, 10, 21); Line(6, 12, 12, 18); break;
                case "eraser": Lines(new(4, 13), new(13, 4), new(21, 12), new(12, 21), new(8, 21), new(4, 17), new(4, 13)); Line(8, 9, 16, 17); Line(12, 21, 21, 21); break;
                case "text": Line(4, 5, 20, 5); Line(12, 5, 12, 20); Line(8, 20, 16, 20); break;
                case "mosaic": Box(4, 4, 16, 16); Line(12, 4, 12, 20); Line(4, 12, 20, 12); break;
                case "ocr": Corners(); Line(7, 8, 17, 8); Line(12, 8, 12, 17); Line(9, 17, 15, 17); break;
                case "qr": Box(3, 3, 6, 6, 1); Box(15, 3, 6, 6, 1); Box(3, 15, 6, 6, 1); Lines(new(15, 14), new(15, 18), new(19, 18), new(19, 21)); Line(21, 14, 21, 16); Line(14, 21, 16, 21); break;
                case "translate": Line(3, 5, 15, 5); Line(9, 2, 9, 5); g.DrawArc(pen, 4, 1, 9, 12, 0, 90); Line(6, 8, 12, 14); Lines(new(12, 21), new(17, 10), new(22, 21)); Line(14, 17, 20, 17); break;
                case "explain": Box(3, 4, 18, 15, 3); Lines(new(7, 19), new(7, 22), new(11, 19)); Line(7, 9, 17, 9); Line(7, 13, 15, 13); break;
                case "undo": Lines(new(8, 4), new(3, 9), new(8, 14)); Lines(new(3, 9), new(14, 9), new(18, 12), new(18, 17), new(15, 20), new(10, 20)); break;
                case "redo": Lines(new(16, 4), new(21, 9), new(16, 14)); Lines(new(21, 9), new(10, 9), new(6, 12), new(6, 17), new(9, 20), new(14, 20)); break;
                case "close": Line(6, 6, 18, 18); Line(18, 6, 6, 18); break;
                case "pin": Lines(new(9, 3), new(19, 13), new(15, 14), new(11, 18), new(6, 13), new(10, 9), new(9, 3)); Line(8, 16, 3, 21); break;
                case "save": Lines(new(4, 3), new(17, 3), new(21, 7), new(21, 21), new(3, 21), new(3, 4), new(4, 3)); Box(7, 3, 9, 6, 1); Box(7, 14, 10, 7, 1); break;
                case "copy": Box(8, 8, 13, 13); Lines(new(16, 4), new(16, 3), new(3, 3), new(3, 16), new(4, 16)); break;
                case "capture": Corners(); Line(9, 15, 15, 9); break;
                case "settings":
                    Line(3, 6, 21, 6); Line(3, 12, 21, 12); Line(3, 18, 21, 18);
                    Box(7, 4, 3, 4, 1); Box(15, 10, 3, 4, 1); Box(6, 16, 3, 4, 1); break;
                case "palette": g.DrawEllipse(pen, 3, 3, 18, 18); g.DrawEllipse(pen, 7, 7, 2, 2); g.DrawEllipse(pen, 14, 7, 2, 2); g.DrawEllipse(pen, 7, 14, 2, 2); Line(15, 14, 18, 18); break;
                case "output": Lines(new(3, 8), new(3, 5), new(10, 5), new(12, 8), new(21, 8), new(21, 20), new(3, 20), new(3, 8)); Line(10, 14, 17, 14); Lines(new(14, 11), new(17, 14), new(14, 17)); break;
                case "keyboard": Box(2, 5, 20, 14); for(var y=9;y<=12;y+=3) for(var x=6;x<=18;x+=4) Line(x,y,x+.5f,y); Line(7,16,17,16); break;
                case "update": g.DrawArc(pen, 4, 4, 16, 16, 40, 280); Lines(new(21, 5), new(21, 11), new(15, 11)); break;
                case "about": g.DrawEllipse(pen, 3, 3, 18, 18); Line(12, 10, 12, 17); Line(12, 7, 12, 7.2f); break;
                case "check": Lines(new(5, 12), new(10, 17), new(20, 6)); break;
                default: Corners(); break;
            }
        }
        finally { g.Restore(state); }
    }

    public static string ForLabel(string label)
    {
        if (label.Contains("退出") || label.Contains("关闭")) return "close";
        if (label.Contains("重新启动") || label.Contains("恢复")) return "update";
        if (label.Contains("二维码")) return "qr";
        if (label.Contains("首选项")) return "settings";
        if (label.Contains("帮助") || label.Contains("许可证")) return "about";
        if (label.Contains("翻译")) return "translate";
        if (label.Contains("识别") || label.Contains("语言")) return "ocr";
        if (label.Contains("字体") || label.Contains("文字") || label.Contains("文件名")) return "text";
        if (label.Contains("颜色") || label.Contains("界面")) return "palette";
        if (label.Contains("快捷") || label.Contains("按键")) return "keyboard";
        if (label.Contains("文件") || label.Contains("路径") || label.Contains("保存")) return "output";
        if (label.Contains("贴图")) return "pin";
        if (label.Contains("截屏") || label.Contains("截图")) return "capture";
        return "settings";
    }
}
