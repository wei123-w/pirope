using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ScreenLine
{
    // 移植 SnipLite 的 AnnotationBar 双层浮栏布局、调色板和绘制方式，并接入图绳操作。
    internal sealed class SnipLiteCaptureBar
    {
        sealed class Entry
        {
            public Rectangle Bounds;
            public string Id;
        }

        internal static readonly Color[] Palette =
        {
            Color.FromArgb(0, 0, 0), Color.FromArgb(74, 74, 74), Color.FromArgb(120, 40, 40),
            Color.FromArgb(229, 57, 53), Color.FromArgb(240, 74, 16), Color.FromArgb(247, 148, 29),
            Color.FromArgb(253, 216, 53), Color.FromArgb(76, 175, 80), Color.FromArgb(0, 172, 193),
            Color.FromArgb(33, 120, 220), Color.FromArgb(140, 60, 200), Color.White,
            Color.FromArgb(205, 205, 205), Color.FromArgb(216, 146, 110), Color.FromArgb(248, 187, 208),
            Color.FromArgb(255, 204, 128), Color.FromArgb(255, 241, 118), Color.FromArgb(197, 225, 165),
            Color.FromArgb(128, 222, 190), Color.FromArgb(129, 212, 250), Color.FromArgb(179, 157, 219),
            Color.FromArgb(225, 190, 231)
        };

        const int Button = 40;
        const int Gap = 3;
        const int Padding = 7;
        const int Swatch = 19;
        const int Group = 11;
        readonly List<Entry> items = new List<Entry>();
        readonly List<Rectangle> dividers = new List<Rectangle>();
        Rectangle mainBounds;
        Rectangle optionsBounds;
        float strokeWidth = 3;
        float fontSize = 24;
        int markerWidth = 24;
        int brushWidth = 28;
        string hoverId;

        internal Rectangle Bounds { get; private set; }
        internal Rectangle OptionsBounds { get { return optionsBounds; } }
        internal string Tool { get; set; }
        internal bool OptionsVisible { get; set; }
        internal Color SelectedColor { get; set; }
        internal float StrokeWidth { get { return strokeWidth; } }
        internal float FontSize { get { return fontSize; } }
        internal bool CanUndo { get; set; }
        internal bool CanRedo { get; set; }
        internal bool MarkerRectangle { get; set; }
        internal bool MosaicRectangle { get; set; }
        internal bool EraserRectangle { get; set; }
        internal bool Fill { get; set; }
        internal bool Bold { get; set; }
        internal bool Italic { get; set; }
        internal int MarkerOpacity { get; set; }
        internal int MosaicStrength { get; set; }
        internal int ArrowStyleIndex { get; set; }
        internal string FontFamily { get; set; }
        internal int MarkerWidth { get { return markerWidth; } set { markerWidth = value; } }
        internal int BrushWidth { get { return brushWidth; } set { brushWidth = value; } }
        internal bool Visible { get { return !Bounds.IsEmpty; } }
        internal int AttachedPanelHeight { get; set; }

        internal SnipLiteCaptureBar()
        {
            SelectedColor = Palette[3];
            MosaicRectangle = true;
            MarkerOpacity = 50;
            MosaicStrength = 12;
            FontFamily = "Microsoft YaHei";
        }

        internal void SetStrokeWidth(float value)
        {
            strokeWidth = Math.Max(1, Math.Min(100, value));
        }

        internal void SetFontSize(float value)
        {
            fontSize = Math.Max(4, Math.Min(200, value));
        }

        internal void CycleWidth()
        {
            strokeWidth = strokeWidth >= 12 ? 2 : strokeWidth + 2;
        }

        internal void SetToolWidth(float value)
        {
            value = Math.Max(1, Math.Min(100, value));
            if (Tool == "Highlight") MarkerWidth = (int)value;
            else if (Tool == "Mosaic" || Tool == "Eraser") BrushWidth = (int)value;
            else strokeWidth = value;
        }

        internal void CycleFontSize()
        {
            fontSize = fontSize >= 72 ? 12 : fontSize + 2;
        }

        internal void SetSlider(string id, Point point)
        {
            Rectangle rect = ItemBounds(id);
            float fraction = Math.Max(0, Math.Min(1, (point.X - rect.Left - 10f) / Math.Max(1, rect.Width - 52f)));
            if (id == "opacity") MarkerOpacity = (int)Math.Round(5 + fraction * 95);
            if (id == "strength") MosaicStrength = (int)Math.Round(2 + fraction * 38);
        }

        internal void Layout(Rectangle selection, Size screen)
        {
            items.Clear();
            dividers.Clear();
            int x = Padding;
            int y = Padding;
            // 主工具顺序、按钮格和分组间距沿用 SnipLite AnnotationBar。
            AddCell(ref x, y, "Rect");
            AddCell(ref x, y, "Ellipse");
            AddCell(ref x, y, "Arrow");
            AddCell(ref x, y, "Pen");
            AddCell(ref x, y, "Highlight");
            AddCell(ref x, y, "Mosaic");
            AddCell(ref x, y, "Text");
            AddCell(ref x, y, "Eraser");
            AddDivider(ref x, y);
            AddCell(ref x, y, "Undo");
            AddCell(ref x, y, "Redo");
            AddDivider(ref x, y);
            AddCell(ref x, y, "ExtractText");
            AddCell(ref x, y, "Translate");
            AddCell(ref x, y, "Explain");
            AddDivider(ref x, y);
            AddCell(ref x, y, "Cancel");
            AddCell(ref x, y, "Pin");
            AddCell(ref x, y, "Save");
            AddCell(ref x, y, "Confirm");

            int mainWidth = x - Gap + Padding;
            int mainHeight = Button + Padding * 2;
            mainBounds = new Rectangle(0, 0, mainWidth, mainHeight);
            bool showOptions = OptionsVisible && !string.IsNullOrEmpty(Tool) && Tool != "Move";
            int totalWidth = mainWidth;
            int totalHeight = mainHeight;
            if (showOptions)
            {
                int oy = mainHeight + Gap;
                int ox = Padding;
                int top = oy + Padding;
                int contentHeight = Button;
                if (Tool != "Text") AddOption(ref ox, top, "Width", Button);
                if (Tool == "Highlight" || Tool == "Mosaic" || Tool == "Eraser")
                {
                    AddOption(ref ox, top, "modebrush", Button);
                    AddOption(ref ox, top, "moderect", Button);
                }
                if (Tool == "Highlight") AddOption(ref ox, top, "opacity", 147);
                if (Tool == "Mosaic") AddOption(ref ox, top, "strength", 147);
                if (Tool == "Arrow") AddOption(ref ox, top, "arrowstyle", 82);
                if (Tool == "Rect" || Tool == "Ellipse")
                {
                    ox += Group - Gap;
                    dividers.Add(new Rectangle(ox, top + Padding, 1, Button - Padding * 2));
                    ox += Group;
                    items.Add(new Entry { Bounds = new Rectangle(ox, top, Button, Button), Id = "fill" });
                    ox += Button + Gap;
                }
                if (Tool == "Text")
                {
                    ox += Group - Gap;
                    dividers.Add(new Rectangle(ox, top + Padding, 1, Button - Padding * 2));
                    ox += Group;
                    AddOption(ref ox, top, "bold", Button);
                    AddOption(ref ox, top, "italic", Button);
                    AddOption(ref ox, top, "fontfamily", 184);
                    AddOption(ref ox, top, "fontsize", 68);
                }
                bool showColor = Tool != "Eraser" && Tool != "Mosaic";
                if (showColor)
                {
                    ox += Group - Gap;
                    dividers.Add(new Rectangle(ox, top + Padding, 1, Button - Padding * 2));
                    ox += Group;
                    items.Add(new Entry { Bounds = new Rectangle(ox, oy + Padding, Button, Button), Id = "ColorCurrent" });
                    ox += Button + Gap * 2;
                    int paletteStart = ox;
                    for (int index = 0; index < Palette.Length; index++)
                    {
                        int column = index % 11;
                        int row = index / 11;
                        items.Add(new Entry { Bounds = new Rectangle(paletteStart + column * Swatch, oy + Padding + row * Swatch, Swatch, Swatch), Id = "Color" + index });
                    }
                    ox = paletteStart + 11 * Swatch;
                }
                optionsBounds = new Rectangle(0, oy, ox + Padding, contentHeight + Padding * 2);
                totalWidth = Math.Max(totalWidth, optionsBounds.Width);
                totalHeight = optionsBounds.Bottom;
            }
            else optionsBounds = Rectangle.Empty;

            int left = selection.Left + (selection.Width - totalWidth) / 2;
            int below = selection.Bottom + 12;
            int placementHeight = totalHeight + (AttachedPanelHeight > 0 ? 4 + AttachedPanelHeight : 0);
            int above = selection.Top - placementHeight - 12;
            int topPosition = below + placementHeight <= screen.Height - 2 ? below : above;
            left = Math.Max(2, Math.Min(screen.Width - totalWidth - 2, left));
            topPosition = Math.Max(2, Math.Min(screen.Height - placementHeight - 2, topPosition));
            Bounds = new Rectangle(left, topPosition, totalWidth, totalHeight);
            Offset(items, left, topPosition);
            Offset(dividers, left, topPosition);
            mainBounds.Offset(left, topPosition);
            if (!optionsBounds.IsEmpty) optionsBounds.Offset(left, topPosition);
        }

        void AddCell(ref int x, int y, string id)
        {
            items.Add(new Entry { Bounds = new Rectangle(x, y, Button, Button), Id = id });
            x += Button + Gap;
        }

        void AddOption(ref int x, int y, string id, int width)
        {
            items.Add(new Entry { Bounds = new Rectangle(x, y, width, Button), Id = id });
            x += width + Gap;
        }

        void AddDivider(ref int x, int y)
        {
            x += Group - Gap;
            dividers.Add(new Rectangle(x, y + Padding, 1, Button - Padding * 2));
            x += Group;
        }

        static void Offset(List<Entry> entries, int x, int y)
        {
            foreach (Entry item in entries) item.Bounds.Offset(x, y);
        }

        static void Offset(List<Rectangle> rectangles, int x, int y)
        {
            for (int i = 0; i < rectangles.Count; i++) rectangles[i].Offset(x, y);
        }

        internal string HitTest(Point point)
        {
            foreach (Entry item in items) if (item.Bounds.Contains(point)) return item.Id;
            return null;
        }

        internal Rectangle ItemBounds(string id)
        {
            Entry found = items.FirstOrDefault(item => item.Id == id);
            return found == null ? Rectangle.Empty : found.Bounds;
        }

        internal bool SetHover(Point point)
        {
            string next = HitTest(point);
            if (next == hoverId) return false;
            hoverId = next;
            return true;
        }

        internal void Draw(Graphics graphics)
        {
            if (!Visible) return;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            DrawBar(graphics, mainBounds);
            if (!optionsBounds.IsEmpty) DrawBar(graphics, optionsBounds);
            foreach (Entry item in items)
            {
                string id = item.Id;
                Rectangle rect = item.Bounds;
                if (id.StartsWith("Color", StringComparison.Ordinal))
                {
                    int index;
                    if (int.TryParse(id.Substring(5), out index))
                    {
                        DrawSwatch(graphics, rect, Palette[index], Palette[index].ToArgb() == SelectedColor.ToArgb());
                        continue;
                    }
                }
                if (id == "Width")
                {
                    DrawWidth(graphics, rect);
                    continue;
                }
                if (id == "opacity" || id == "strength")
                {
                    DrawSlider(graphics, rect, id);
                    continue;
                }
                if (id == "fontfamily" || id == "fontsize" || id == "arrowstyle")
                {
                    DrawField(graphics, rect, id);
                    continue;
                }
                bool active = IsActive(id);
                if (active)
                {
                    Rectangle selected = Rectangle.Inflate(rect, -2, -2);
                    using (GraphicsPath path = Rounded(selected, 5))
                    using (var fill = new SolidBrush(Color.FromArgb(232, 241, 255))) graphics.FillPath(fill, path);
                    using (var edge = new Pen(Color.FromArgb(205, 222, 247)))
                    using (GraphicsPath path = Rounded(selected, 5)) graphics.DrawPath(edge, path);
                    using (var indicator = new Pen(Color.FromArgb(0, 145, 234), 2) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        graphics.DrawLine(indicator, rect.Left + rect.Width / 2f - 5, selected.Bottom - 1, rect.Left + rect.Width / 2f + 5, selected.Bottom - 1);
                }
                DrawItem(graphics, id, rect, active);
            }
            using (var pen = new Pen(Color.FromArgb(220, 226, 234)))
                foreach (Rectangle divider in dividers) graphics.DrawLine(pen, divider.X, divider.Top, divider.X, divider.Bottom);
        }

        bool IsActive(string id)
        {
            if (id == Tool) return true;
            if (id == "Undo" || id == "Redo") return false;
            if (id.StartsWith("Color", StringComparison.Ordinal)) return false;
            if (id == "modebrush") return Tool == "Highlight" && !MarkerRectangle || Tool == "Mosaic" && !MosaicRectangle || Tool == "Eraser" && !EraserRectangle;
            if (id == "moderect") return Tool == "Highlight" && MarkerRectangle || Tool == "Mosaic" && MosaicRectangle || Tool == "Eraser" && EraserRectangle;
            if (id == "fill") return Fill;
            if (id == "bold") return Bold;
            if (id == "italic") return Italic;
            return false;
        }

        void DrawWidth(Graphics graphics, Rectangle rect)
        {
            int width = Tool == "Highlight" ? MarkerWidth : Tool == "Mosaic" || Tool == "Eraser" ? BrushWidth : (int)StrokeWidth;
            int diameter = Math.Max(5, Math.Min(18, (int)(width * 1.8f)));
            float cx = rect.Left + rect.Width / 2f, cy = rect.Top + rect.Height / 2f;
            using (var fill = new SolidBrush(SelectedColor)) graphics.FillEllipse(fill, cx - diameter / 2f, cy - diameter / 2f, diameter, diameter);
            using (var edge = new Pen(Color.FromArgb(140, 0, 0, 0), 1)) graphics.DrawEllipse(edge, cx - diameter / 2f, cy - diameter / 2f, diameter, diameter);
        }

        void DrawSlider(Graphics graphics, Rectangle rect, string id)
        {
            float value = id == "opacity" ? (MarkerOpacity - 5) / 95f : (MosaicStrength - 2) / 38f;
            float left = rect.Left + 10, right = rect.Right - 42;
            float y = rect.Top + rect.Height / 2f;
            using (var track = new Pen(Color.FromArgb(190, 190, 190), 3)) graphics.DrawLine(track, left, y, right, y);
            float x = left + (right - left) * value;
            using (var active = new Pen(Color.FromArgb(0, 145, 234), 3))
            using (var fill = new SolidBrush(Color.White))
            {
                graphics.DrawLine(active, left, y, x, y);
                graphics.FillRectangle(fill, x - 4, y - 6, 8, 12);
                graphics.DrawRectangle(active, x - 4, y - 6, 8, 12);
            }
            using (var font = new Font("Segoe UI", 11, GraphicsUnit.Pixel))
            {
                string valueText = id == "opacity" ? MarkerOpacity + "%" : MosaicStrength.ToString();
                TextRenderer.DrawText(graphics, valueText, font, new Rectangle((int)right + 5, rect.Y, 35, rect.Height), Color.FromArgb(38, 45, 55), TextFormatFlags.VerticalCenter);
            }
        }

        void DrawField(Graphics graphics, Rectangle rect, string id)
        {
            string text = id == "fontfamily" ? FontFamily : id == "fontsize" ? FontSize.ToString("0.#") : new[] { "➜", "→", "↔", "⇨", "➤" }[ArrowStyleIndex % 5];
            using (var border = new Pen(Color.FromArgb(220, 226, 234)))
            using (GraphicsPath path = Rounded(Rectangle.Inflate(rect, -1, -1), 4)) graphics.DrawPath(border, path);
            using (var font = new Font("Microsoft YaHei UI", 12, GraphicsUnit.Pixel))
            {
                Rectangle textBounds = Rectangle.Inflate(rect, -7, 0);
                textBounds.Width -= 15;
                TextRenderer.DrawText(graphics, text, font, textBounds, Color.FromArgb(38, 45, 55), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            PointF center = new PointF(rect.Right - 11, rect.Top + rect.Height / 2f);
            using (var pen = new Pen(Color.FromArgb(38, 45, 55), 1.6f)) graphics.DrawLines(pen, new[] { new PointF(center.X - 3, center.Y - 1), new PointF(center.X, center.Y + 2), new PointF(center.X + 3, center.Y - 1) });
        }

        static void DrawBar(Graphics graphics, Rectangle bounds)
        {
            using (GraphicsPath path = Rounded(new RectangleF(bounds.X + .5f, bounds.Y + .5f, bounds.Width - 1, bounds.Height - 1), 10))
            using (var fill = new SolidBrush(Color.White))
            using (var border = new Pen(Color.FromArgb(220, 226, 234)))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }
        }

        static GraphicsPath Rounded(Rectangle rectangle, int radius)
        {
            return Rounded(new RectangleF(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height), radius);
        }

        static GraphicsPath Rounded(RectangleF rectangle, int radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, d, d, 180, 90);
            path.AddArc(rectangle.Right - d, rectangle.Top, d, d, 270, 90);
            path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        static void DrawSwatch(Graphics graphics, Rectangle rect, Color color, bool selected)
        {
            using (var brush = new SolidBrush(color)) graphics.FillRectangle(brush, rect);
            using (var border = new Pen(Color.FromArgb(190, 198, 208))) graphics.DrawRectangle(border, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            if (!selected) return;
            using (var white = new Pen(Color.White, 2)) graphics.DrawRectangle(white, rect.X + 2, rect.Y + 2, rect.Width - 5, rect.Height - 5);
            using (var blue = new Pen(Color.FromArgb(0, 145, 234), 1)) graphics.DrawRectangle(blue, rect.X + 2, rect.Y + 2, rect.Width - 5, rect.Height - 5);
        }

        void DrawItem(Graphics graphics, string id, Rectangle rect, bool active)
        {
            float cx = rect.Left + rect.Width / 2f;
            float cy = rect.Top + rect.Height / 2f;
            Color ink = active ? Color.FromArgb(0, 111, 210) : id == "Undo" && !CanUndo || id == "Redo" && !CanRedo ? Color.FromArgb(175, 181, 190) : Color.FromArgb(38, 45, 55);
            string snipIcon = SnipLiteIconId(id);
            if (snipIcon != null)
            {
                int iconSize = Math.Min(24, Math.Min(rect.Width - 12, rect.Height - 12));
                Rectangle iconBounds = new Rectangle(rect.Left + (rect.Width - iconSize) / 2, rect.Top + (rect.Height - iconSize) / 2, iconSize, iconSize);
                DrawSnipLiteIcon(graphics, snipIcon, iconBounds, ink);
                return;
            }
            using (var pen = new Pen(ink, 1.8f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var brush = new SolidBrush(ink))
            {
                switch (id)
                {
                    case "Rect":
                        using (GraphicsPath path = Rounded(new Rectangle((int)(cx - 7), (int)(cy - 5.5f), 14, 11), 2)) graphics.DrawPath(pen, path);
                        break;
                    case "Ellipse": graphics.DrawEllipse(pen, cx - 7, cy - 5.5f, 14, 11); break;
                    case "Arrow":
                        graphics.DrawLine(pen, cx - 7, cy + 5.5f, cx + 4.5f, cy - 6);
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddPolygon(new[] { new PointF(cx + 7, cy - 7.5f), new PointF(cx + 1.5f, cy - 6.5f), new PointF(cx + 5.5f, cy - 1.5f) });
                            graphics.FillPath(brush, path);
                        }
                        break;
                    case "Pen":
                        GraphicsState penState = graphics.Save();
                        graphics.TranslateTransform(cx, cy);
                        graphics.RotateTransform(-45);
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddRectangle(new RectangleF(-1.7f, -8f, 3.4f, 12f));
                            path.AddPolygon(new[] { new PointF(-1.7f, 4f), new PointF(1.7f, 4f), new PointF(0, 8f) });
                            graphics.DrawPath(pen, path);
                        }
                        graphics.DrawLine(pen, -1.7f, -2f, 1.7f, -2f);
                        graphics.Restore(penState);
                        break;
                    case "Highlight":
                        using (var marker = new Pen(Color.FromArgb(229, 57, 53), 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            graphics.DrawLine(marker, cx - 7, cy + 5, cx + 6, cy - 5);
                        graphics.DrawLine(pen, cx - 7, cy + 8, cx + 1, cy + 8);
                        break;
                    case "Mosaic":
                        for (int row = 0; row < 2; row++) for (int col = 0; col < 2; col++)
                        {
                            RectangleF cell = new RectangleF(cx - 6 + col * 6, cy - 6 + row * 6, 5, 5);
                            if ((row + col) % 2 == 0) graphics.FillRectangle(brush, cell); else graphics.DrawRectangle(pen, cell.X, cell.Y, cell.Width, cell.Height);
                        }
                        break;
                    case "Text":
                        using (var f = new Font("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            graphics.DrawString("T", f, brush, new RectangleF(cx - 10, cy - 11, 20, 22), sf);
                        break;
                    case "Eraser": graphics.DrawPolygon(pen, new[] { new PointF(cx - 8, cy + 2), new PointF(cx + 1, cy - 7), new PointF(cx + 8, cy), new PointF(cx - 1, cy + 9) }); graphics.DrawLine(pen, cx - 4, cy - 2, cx + 3, cy + 5); break;
                    case "Number":
                        graphics.DrawEllipse(pen, cx - 8, cy - 8, 16, 16);
                        using (var f = new Font("Segoe UI", 10, FontStyle.Bold)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) graphics.DrawString("1", f, brush, new RectangleF(cx - 7, cy - 7, 14, 14), sf);
                        break;
                    case "Eyedropper":
                        graphics.DrawLine(pen, cx - 5, cy + 5, cx + 5, cy - 5); graphics.DrawEllipse(pen, cx + 2, cy - 8, 6, 6); graphics.DrawLine(pen, cx - 7, cy + 7, cx - 3, cy + 3);
                        break;
                    case "modebrush": graphics.FillEllipse(brush, cx - 5, cy - 5, 10, 10); break;
                    case "moderect": graphics.FillRectangle(brush, cx - 7, cy - 5, 14, 10); break;
                    case "fill":
                        Rectangle fillBox = new Rectangle(rect.X + 10, rect.Y + 10, rect.Width - 20, rect.Height - 20);
                        if (Fill) graphics.FillRectangle(brush, fillBox);
                        else
                        {
                            GraphicsState fillState = graphics.Save(); graphics.SetClip(fillBox);
                            for (int line = fillBox.Left - fillBox.Height; line < fillBox.Right; line += 4) graphics.DrawLine(pen, line, fillBox.Bottom, line + fillBox.Height, fillBox.Top);
                            graphics.Restore(fillState); graphics.DrawRectangle(pen, fillBox);
                        }
                        break;
                    case "bold":
                        using (var boldFont = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) graphics.DrawString("B", boldFont, brush, rect, format);
                        break;
                    case "italic":
                        using (var italicFont = new Font("Times New Roman", 16, FontStyle.Italic, GraphicsUnit.Pixel))
                        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) graphics.DrawString("I", italicFont, brush, rect, format);
                        break;
                    case "Undo": DrawHistory(graphics, cx, cy, pen, false); break;
                    case "Redo": DrawHistory(graphics, cx, cy, pen, true); break;
                    case "ExtractText": graphics.DrawRectangle(pen, cx - 7, cy - 8, 14, 16); graphics.DrawLine(pen, cx - 4, cy - 3, cx + 4, cy - 3); graphics.DrawLine(pen, cx - 4, cy, cx + 4, cy); graphics.DrawLine(pen, cx - 4, cy + 3, cx + 2, cy + 3); break;
                    case "Translate":
                        using (var f = new Font("Microsoft YaHei UI", 14, FontStyle.Bold, GraphicsUnit.Pixel)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) graphics.DrawString("译", f, brush, rect, sf);
                        break;
                    case "Explain": graphics.DrawRectangle(pen, cx - 9, cy - 8, 18, 15); graphics.DrawLines(pen, new[] { new PointF(cx - 4, cy + 7), new PointF(cx - 6, cy + 10), new PointF(cx - 6, cy + 7) }); graphics.DrawLine(pen, cx - 5, cy - 2, cx + 5, cy - 2); graphics.DrawLine(pen, cx - 5, cy + 2, cx + 3, cy + 2); break;
                    case "Pin":
                        GraphicsState pinState = graphics.Save(); graphics.TranslateTransform(cx, cy); graphics.RotateTransform(35);
                        using (GraphicsPath path = new GraphicsPath()) { path.AddRectangle(new RectangleF(-4.5f, -7.5f, 9, 6)); path.AddRectangle(new RectangleF(-1.4f, -1.5f, 2.8f, 5)); graphics.DrawPath(pen, path); }
                        graphics.DrawLine(pen, 0, 3.5f, 0, 8.5f); graphics.Restore(pinState); break;
                    case "Save":
                        using (GraphicsPath path = Rounded(new Rectangle((int)(cx - 7), (int)(cy - 7), 14, 14), 2)) graphics.DrawPath(pen, path);
                        graphics.FillRectangle(brush, cx - 3.5f, cy - 7, 7, 4); graphics.DrawRectangle(pen, cx - 4.5f, cy + 1, 9, 6); break;
                    case "Cancel": graphics.DrawLine(pen, cx - 6, cy - 6, cx + 6, cy + 6); graphics.DrawLine(pen, cx + 6, cy - 6, cx - 6, cy + 6); break;
                    case "Confirm": using (var fill = new SolidBrush(Color.FromArgb(229, 57, 53))) using (var white = new Pen(Color.White, 2.2f)) { graphics.FillEllipse(fill, cx - 10, cy - 10, 20, 20); graphics.DrawLines(white, new[] { new PointF(cx - 5, cy), new PointF(cx - 1, cy + 4), new PointF(cx + 6, cy - 5) }); } break;
                    case "Width":
                        int diameter = Math.Max(4, Math.Min(16, (int)(StrokeWidth * 2.4f)));
                        using (var fill = new SolidBrush(SelectedColor)) graphics.FillEllipse(fill, cx - diameter / 2, cy - diameter / 2, diameter, diameter);
                        using (var edge = new Pen(Color.FromArgb(140, 0, 0, 0), 1)) graphics.DrawEllipse(edge, cx - diameter / 2, cy - diameter / 2, diameter, diameter);
                        break;
                    case "FontSize": break;
                    case "ColorCurrent":
                        if (Tool == "Highlight")
                        {
                            int cellSize = 7;
                            using (var white = new SolidBrush(Color.White))
                            using (var gray = new SolidBrush(Color.FromArgb(210, 210, 210)))
                                for (int y = rect.Top + 4; y < rect.Bottom - 4; y += cellSize)
                                    for (int x = rect.Left + 4; x < rect.Right - 4; x += cellSize)
                                        graphics.FillRectangle(((x / cellSize + y / cellSize) % 2 == 0) ? white : gray, x, y, Math.Min(cellSize, rect.Right - 4 - x), Math.Min(cellSize, rect.Bottom - 4 - y));
                        }
                        using (var fill = new SolidBrush(Tool == "Highlight" ? Color.FromArgb(255 * MarkerOpacity / 100, SelectedColor) : SelectedColor)) graphics.FillRectangle(fill, Rectangle.Inflate(rect, -4, -4));
                        using (var edge = new Pen(Color.FromArgb(170, 182, 194))) graphics.DrawRectangle(edge, rect.X + 4, rect.Y + 4, rect.Width - 9, rect.Height - 9);
                        int corner = Math.Max(5, rect.Width / 4);
                        using (GraphicsPath triangle = new GraphicsPath())
                        {
                            triangle.AddPolygon(new[] { new Point(rect.X + 1, rect.Bottom - 2), new Point(rect.X + corner, rect.Bottom - 2), new Point(rect.X + 1, rect.Bottom - corner - 1) });
                            using (var rainbow = new LinearGradientBrush(new Point(rect.X, rect.Bottom), new Point(rect.X + corner, rect.Bottom - corner), Color.Red, Color.Blue))
                            {
                                rainbow.InterpolationColors = new ColorBlend { Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue }, Positions = new[] { 0f, .3f, .55f, .8f, 1f } };
                                graphics.FillPath(rainbow, triangle);
                            }
                        }
                        using (var edge = new Pen(Color.FromArgb(120, 0, 0, 0), 1)) graphics.DrawLine(edge, rect.X + corner, rect.Bottom - 2, rect.X + 1, rect.Bottom - corner - 1);
                        break;
                    case "CustomColor":
                        graphics.DrawEllipse(pen, cx - 7, cy - 7, 14, 14); graphics.DrawLine(pen, cx - 4, cy, cx + 4, cy); graphics.DrawLine(pen, cx, cy - 4, cx, cy + 4); break;
                }
            }
        }

        static string SnipLiteIconId(string id)
        {
            switch (id)
            {
                case "Rect": return "rect";
                case "Ellipse": return "ellipse";
                case "Arrow": return "arrow";
                case "Pen": return "pen";
                case "Highlight": return "marker";
                case "Eraser": return "eraser";
                case "Text": return "text";
                case "Mosaic": return "mosaic";
                case "ExtractText": return "ocr";
                case "Translate": return "translate";
                // 图绳的 AI 解释占用 SnipLite 第三个辅助操作的位置。
                case "Undo": return "undo";
                case "Redo": return "redo";
                case "Pin": return "pin";
                case "Save": return "save";
                case "Cancel": return "close";
                case "Confirm": return "copy";
                default: return null;
            }
        }

        // 逐条移植 SnipLite FluentIcons 的 24 单位矢量路径，沿用其线宽和端点样式。
        static void DrawSnipLiteIcon(Graphics graphics, string id, Rectangle bounds, Color color)
        {
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = Math.Min(bounds.Width, bounds.Height) / 24f;
                graphics.TranslateTransform(bounds.Left + (bounds.Width - 24 * scale) / 2f, bounds.Top + (bounds.Height - 24 * scale) / 2f);
                graphics.ScaleTransform(scale, scale);
                using (var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    switch (id)
                    {
                        case "rect": DrawSnipBox(graphics, pen, 4, 5, 16, 14, 2); break;
                        case "ellipse": graphics.DrawEllipse(pen, 4, 5, 16, 14); break;
                        case "arrow": graphics.DrawLine(pen, 5, 19, 19, 5); graphics.DrawLines(pen, new[] { new PointF(10, 5), new PointF(19, 5), new PointF(19, 14) }); break;
                        case "pen": graphics.DrawLines(pen, new[] { new PointF(4, 16), new PointF(15, 5), new PointF(19, 9), new PointF(8, 20), new PointF(3, 21), new PointF(4, 16) }); graphics.DrawLine(pen, 13, 7, 17, 11); break;
                        case "marker": graphics.DrawLines(pen, new[] { new PointF(4, 14), new PointF(15, 3), new PointF(21, 9), new PointF(10, 20), new PointF(4, 14) }); graphics.DrawLine(pen, 3, 21, 10, 21); graphics.DrawLine(pen, 6, 12, 12, 18); break;
                        case "eraser": graphics.DrawLines(pen, new[] { new PointF(4, 13), new PointF(13, 4), new PointF(21, 12), new PointF(12, 21), new PointF(8, 21), new PointF(4, 17), new PointF(4, 13) }); graphics.DrawLine(pen, 8, 9, 16, 17); graphics.DrawLine(pen, 12, 21, 21, 21); break;
                        case "text": graphics.DrawLine(pen, 4, 5, 20, 5); graphics.DrawLine(pen, 12, 5, 12, 20); graphics.DrawLine(pen, 8, 20, 16, 20); break;
                        case "mosaic": DrawSnipBox(graphics, pen, 4, 4, 16, 16, 2); graphics.DrawLine(pen, 12, 4, 12, 20); graphics.DrawLine(pen, 4, 12, 20, 12); break;
                        case "ocr":
                            graphics.DrawLines(pen, new[] { new PointF(8, 3), new PointF(3, 3), new PointF(3, 8) }); graphics.DrawLines(pen, new[] { new PointF(16, 3), new PointF(21, 3), new PointF(21, 8) });
                            graphics.DrawLines(pen, new[] { new PointF(3, 16), new PointF(3, 21), new PointF(8, 21) }); graphics.DrawLines(pen, new[] { new PointF(16, 21), new PointF(21, 21), new PointF(21, 16) });
                            graphics.DrawLine(pen, 7, 8, 17, 8); graphics.DrawLine(pen, 12, 8, 12, 17); graphics.DrawLine(pen, 9, 17, 15, 17); break;
                        case "translate":
                            graphics.DrawLine(pen, 3, 5, 15, 5); graphics.DrawLine(pen, 9, 2, 9, 5); graphics.DrawArc(pen, 4, 1, 9, 12, 0, 90); graphics.DrawLine(pen, 6, 8, 12, 14);
                            graphics.DrawLines(pen, new[] { new PointF(12, 21), new PointF(17, 10), new PointF(22, 21) }); graphics.DrawLine(pen, 14, 17, 20, 17); break;
                        case "undo": DrawSnipUndoRedo(graphics, pen, false); break;
                        case "redo": DrawSnipUndoRedo(graphics, pen, true); break;
                        case "close": graphics.DrawLine(pen, 6, 6, 18, 18); graphics.DrawLine(pen, 18, 6, 6, 18); break;
                        case "pin": graphics.DrawLines(pen, new[] { new PointF(9, 3), new PointF(19, 13), new PointF(15, 14), new PointF(11, 18), new PointF(6, 13), new PointF(10, 9), new PointF(9, 3) }); graphics.DrawLine(pen, 8, 16, 3, 21); break;
                        case "save": graphics.DrawLines(pen, new[] { new PointF(4, 3), new PointF(17, 3), new PointF(21, 7), new PointF(21, 21), new PointF(3, 21), new PointF(3, 4), new PointF(4, 3) }); DrawSnipBox(graphics, pen, 7, 3, 9, 6, 1); DrawSnipBox(graphics, pen, 7, 14, 10, 7, 1); break;
                        case "copy": DrawSnipBox(graphics, pen, 8, 8, 13, 13, 2); graphics.DrawLines(pen, new[] { new PointF(16, 4), new PointF(16, 3), new PointF(3, 3), new PointF(3, 16), new PointF(4, 16) }); break;
                    default: break;
                    }
                }
            }
            finally { graphics.Restore(state); }
        }

        static void DrawSnipBox(Graphics graphics, Pen pen, float x, float y, float width, float height, float radius)
        {
            using (var path = Rounded(new RectangleF(x, y, width, height), (int)radius)) graphics.DrawPath(pen, path);
        }

        static void DrawSnipUndoRedo(Graphics graphics, Pen pen, bool redo)
        {
            if (!redo) { graphics.DrawLines(pen, new[] { new PointF(8, 4), new PointF(3, 9), new PointF(8, 14) }); graphics.DrawLines(pen, new[] { new PointF(3, 9), new PointF(14, 9), new PointF(18, 12), new PointF(18, 17), new PointF(15, 20), new PointF(10, 20) }); }
            else { graphics.DrawLines(pen, new[] { new PointF(16, 4), new PointF(21, 9), new PointF(16, 14) }); graphics.DrawLines(pen, new[] { new PointF(21, 9), new PointF(10, 9), new PointF(6, 12), new PointF(6, 17), new PointF(9, 20), new PointF(14, 20) }); }
        }

        static void DrawHistory(Graphics graphics, float cx, float cy, Pen pen, bool redo)
        {
            float rx = 7f, ry = 5.5f, centerY = cy + 1.5f;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(cx - rx, centerY - ry, rx * 2, ry * 2, redo ? 340f : 200f, redo ? -140f : 140f);
                graphics.DrawPath(pen, path);
            }
            double angle = (redo ? 340d : 200d) * Math.PI / 180d;
            PointF tip = new PointF(cx + rx * (float)Math.Cos(angle), centerY + ry * (float)Math.Sin(angle));
            PointF direction = new PointF(redo ? .42f : -.42f, .91f);
            PointF perpendicular = new PointF(-direction.Y, direction.X);
            PointF basePoint = new PointF(tip.X - direction.X * 5f, tip.Y - direction.Y * 5f);
            graphics.DrawLines(pen, new[] { tip, new PointF(basePoint.X + perpendicular.X * 3f, basePoint.Y + perpendicular.Y * 3f) });
            graphics.DrawLine(pen, tip, new PointF(basePoint.X - perpendicular.X * 3f, basePoint.Y - perpendicular.Y * 3f));
        }
    }
}
