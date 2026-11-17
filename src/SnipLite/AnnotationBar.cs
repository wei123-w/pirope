using System.Drawing.Drawing2D;

namespace SnipLite;

/// <summary>
/// The floating toolbar under the selection, drawn and hit-tested by hand.
/// Layout follows Snipaste: one row of tools / actions, plus a second row with
/// the active tool's options (line width, current colour, palette).
/// </summary>
internal sealed class AnnotationBar
{
    /// <summary>Snipaste's palette: 11 columns x 2 rows, saturated then pastel.</summary>
    public static readonly Color[] Palette =
    {
        Color.FromArgb(0, 0, 0),
        Color.FromArgb(74, 74, 74),
        Color.FromArgb(120, 40, 40),
        Color.FromArgb(229, 57, 53),
        Color.FromArgb(240, 74, 16),
        Color.FromArgb(247, 148, 29),
        Color.FromArgb(253, 216, 53),
        Color.FromArgb(76, 175, 80),
        Color.FromArgb(0, 172, 193),
        Color.FromArgb(33, 120, 220),
        Color.FromArgb(140, 60, 200),

        Color.FromArgb(255, 255, 255),
        Color.FromArgb(205, 205, 205),
        Color.FromArgb(216, 146, 110),
        Color.FromArgb(248, 187, 208),
        Color.FromArgb(255, 204, 128),
        Color.FromArgb(255, 241, 118),
        Color.FromArgb(197, 225, 165),
        Color.FromArgb(128, 222, 190),
        Color.FromArgb(129, 212, 250),
        Color.FromArgb(179, 157, 219),
        Color.FromArgb(225, 190, 231)
    };

    public const int Columns = 11;

    private const int Button = 34;
    private const int Gap = 2;
    private const int Pad = 5;
    // Two colour rows fit the same 34 px content height as every editor.
    private const int Swatch = 17;
    private const int Group = 9;

    private static Color BarBack => AppTheme.Current.Card;
    private static Color IconColor => AppTheme.Current.Text;
    private static Color Divider => AppTheme.Current.Border;
    private static Color SwatchBorder => AppTheme.Current.Border;

    /// <summary>Snipaste's default theme blue, also the fallback accent colour.</summary>
    public static readonly Color DefaultAccent = Color.FromArgb(0, 145, 234);

    private readonly List<(Rectangle Rect, string Id)> _items = new();
    private readonly List<Rectangle> _dividers = new();

    private Rectangle _main;
    private Rectangle _options;
    private Rectangle _message;

    public bool OcrMode { get; set; }
    public bool TranslationMode { get; set; }
    public int AttachedPanelHeight { get; set; }
    public bool OcrCopyVisible { get; set; }
    public bool CanUndo { get; set; }
    public bool CanRedo { get; set; }
    private string _rawStatus = "";
    private Func<string>? _localizedStatus;
    public string Status { get => _localizedStatus?.Invoke() ?? _rawStatus; set { _localizedStatus = null; _rawStatus = value; } }
    public void SetStatus(Func<string> status) => _localizedStatus = status;
    private int ResultButtonWidth()
    {
        using var font = new Font("Microsoft YaHei UI", 11 * Scale, GraphicsUnit.Pixel);
        var label = OcrMode ? UiText.T("复制文字") : UiText.T("复制");
        var width = Math.Max(TextRenderer.MeasureText(label, font).Width, TextRenderer.MeasureText(UiText.T("已复制"), font).Width);
        return Math.Clamp(width + Px(18), Px(OcrMode ? 80 : 56), Px(150));
    }
    public bool ResultCopyVisible { get; set; }
    public bool ResultCopied { get; set; }

    public ToolKind Tool { get; set; } = ToolKind.None;
    public Color Color { get; set; } = Palette[3];
    public float StrokeWidth { get; set; } = 3f;
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public float FontSize { get; set; } = 16f;
    public ArrowStyle ArrowStyle { get; set; }
    public float MarkerWidth { get; set; } = 24;
    public float BrushWidth { get; set; } = 28;
    public int MarkerOpacity { get; set; } = 50;
    public int MosaicBlock { get; set; } = 12;
    public bool MarkerRectangle { get; set; }
    public bool MosaicRectangle { get; set; } = true;
    public bool EraserRectangle { get; set; }
    public bool RectangleMode => Tool switch
    {
        ToolKind.Marker => MarkerRectangle,
        ToolKind.Mosaic => MosaicRectangle,
        ToolKind.Eraser => EraserRectangle,
        _ => false
    };

    /// <summary>Theme colour: the active-tool band and the bar border.</summary>
    public Color Accent { get; set; } = DefaultAccent;

    /// <summary>Shapes are outlines by default; the second cell of row 2 fills them.</summary>
    public bool Fill { get; set; }

    public bool Bold { get; set; }
    public bool Italic { get; set; }

    /// <summary>UI scale (physical pixels per 96 dpi).</summary>
    public float Scale { get; set; } = 1f;

    public Rectangle Bounds { get; private set; }
    public Rectangle MainBounds => _main;
    public Rectangle OptionsBounds => _options;
    public Rectangle MessageBounds => _message;

    public bool Visible => _items.Count > 0;

    private int Px(int value) => (int)Math.Round(value * Scale);

    private bool ShowsFill => Tool is ToolKind.Rectangle or ToolKind.Ellipse;

    private bool ShowsTextStyle => Tool == ToolKind.Text;

    public void Clear()
    {
        _items.Clear();
        _dividers.Clear();
        Bounds = Rectangle.Empty;
        _main = Rectangle.Empty;
        _options = Rectangle.Empty;
    }

    public void Layout(Rectangle selection, Size screen)
    {
        _items.Clear();
        _dividers.Clear();

        var button = Px(Button);
        var gap = Px(Gap);
        var pad = Px(Pad);
        var swatch = Math.Min(Px(Swatch), button / 2);
        var group = Px(Group);

        var step = swatch;
        var rows = (Palette.Length + Columns - 1) / Columns;
        var gridHeight = rows * step;

        var showOptions = !OcrMode && Tool != ToolKind.None;

        // ---------------------------------------------------------- row 1
        var x = pad;
        var y = pad;

        void Add(int left, int top, int width, int height, string id)
            => _items.Add((new Rectangle(left, top, width, height), id));

        void Cell(string id)
        {
            Add(x, y, button, button, id);
            x += button + gap;
        }

        Cell("rect");
        Cell("ellipse");
        Cell("arrow");
        Cell("pen");
        Cell("marker");
        Cell("mosaic");
        Cell("text");
        Cell("eraser");

        void Divider()
        {
            x += group - gap;
            _dividers.Add(new Rectangle(x, y + pad, 1, button - pad * 2));
            x += group;
        }

        Divider();
        Cell("undo");
        Cell("redo");
        Divider();

        Cell("ocr");
        Cell("translate"); Cell("explain"); Cell("qr");
        Divider();

        Cell("close");
        Cell("pin");
        Cell("save");
        Cell("copy");

        var mainWidth = x - gap + pad;
        var mainHeight = button + pad * 2;

        _main = new Rectangle(0, 0, mainWidth, mainHeight);

        var totalWidth = mainWidth;
        var totalHeight = mainHeight;

        // ---------------------------------------------------------- row 2
        if (showOptions)
        {
            var oy = mainHeight + gap;
            var ox = pad;
            var top = oy + pad;
            var contentHeight = Math.Max(button, gridHeight);
            var optionsHeight = contentHeight + pad * 2;

            void Divider2()
            {
                ox += group - gap;
                _dividers.Add(new Rectangle(ox, top + pad, 1, contentHeight - pad * 2));
                ox += group;
            }

            void Option(string id, int w = Button)
            {
                Add(ox, top, Px(w), button, id);
                ox += Px(w) + gap;
            }

            if (!ShowsTextStyle) Option("width");
            if (Tool is ToolKind.Marker or ToolKind.Mosaic or ToolKind.Eraser)
            {
                Option("modebrush");
                Option("moderect");
            }
            if (Tool == ToolKind.Marker) Option("opacity", 125);
            if (Tool == ToolKind.Mosaic) Option("strength", 125);
            if (Tool == ToolKind.Arrow) Option("arrowstyle", 70);

            if (ShowsFill)
            {
                Divider2();
                Add(ox, top, button, button, "fill");
                ox += button + gap;
            }

            if (ShowsTextStyle)
            {
                Divider2();
                Add(ox, top, button, button, "bold");
                ox += button + gap;
                Add(ox, top, button, button, "italic");
                ox += button + gap;
                Option("fontfamily", 156);
                Option("fontsize", 58);
            }

            if (Tool is not ToolKind.Eraser and not ToolKind.Mosaic)
            {
                Divider2();

            Add(ox, oy + pad, gridHeight, gridHeight, "colorcur");
            ox += gridHeight + gap * 2;

            var gridX = ox;

            for (var i = 0; i < Palette.Length; i++)
            {
                var col = i % Columns;
                var row = i / Columns;
                Add(gridX + col * step, oy + pad + row * step, swatch, swatch, "color" + i);
            }

            ox = gridX + Columns * step;
            }

            _options = new Rectangle(0, oy, ox + pad, optionsHeight);
            totalWidth = Math.Max(totalWidth, _options.Width);
            totalHeight = oy + optionsHeight;
        }
        else
        {
            _options = Rectangle.Empty;
        }

        _message = Rectangle.Empty;
        if (Status.Length > 0)
        {
            _message = new Rectangle(0, totalHeight - 1, mainWidth, Px(OcrMode ? 44 : 32));
            var copyWidth = ResultButtonWidth();
            if (ResultCopyVisible)
                Add(_message.Right - copyWidth - Px(6), _message.Top + Px(4), copyWidth, _message.Height - Px(8), "resultcopy");
            if (OcrMode && OcrCopyVisible)
                Add(_message.Right - copyWidth - Px(6), _message.Top + Px(5), copyWidth, Px(34), "ocrtextcopy");
            totalWidth = Math.Max(totalWidth, _message.Width);
            totalHeight = _message.Bottom;
        }

        var left = selection.Left + (selection.Width - totalWidth) / 2;
        var topPos = selection.Bottom + (int)Math.Round(10 * Scale);
        var placementHeight = totalHeight + (AttachedPanelHeight > 0 ? Px(4) + AttachedPanelHeight : 0);

        if (topPos + placementHeight > screen.Height - 2)
            topPos = selection.Top - placementHeight - (int)Math.Round(10 * Scale);

        if (topPos < 2)
            topPos = Math.Min(selection.Bottom - placementHeight - 10, screen.Height - placementHeight - 2);

        left = Math.Clamp(left, 2, Math.Max(2, screen.Width - totalWidth - 2));
        topPos = Math.Clamp(topPos, 2, Math.Max(2, screen.Height - placementHeight - 2));

        Bounds = new Rectangle(left, topPos, totalWidth, totalHeight);

        Offset(_items, left, topPos);
        Offset(_dividers, left, topPos);
        _main.Offset(left, topPos);
        _options.Offset(left, topPos);
        _message.Offset(left, topPos);
    }

    private static void Offset(List<(Rectangle Rect, string Id)> items, int dx, int dy)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            items[i] = (new Rectangle(item.Rect.X + dx, item.Rect.Y + dy, item.Rect.Width, item.Rect.Height), item.Id);
        }
    }

    private static void Offset(List<Rectangle> rects, int dx, int dy)
    {
        for (var i = 0; i < rects.Count; i++)
            rects[i] = new Rectangle(rects[i].X + dx, rects[i].Y + dy, rects[i].Width, rects[i].Height);
    }

    public string? HitTest(Point point)
    {
        foreach (var item in _items)
            if (item.Rect.Contains(point))
                return item.Id;

        return null;
    }

    public Rectangle ItemBounds(string id) => _items.FirstOrDefault(item => item.Id == id).Rect;

    public void SetSlider(string id, Point point)
    {
        var rect = ItemBounds(id);
        var fraction = Math.Clamp((point.X - rect.Left - Px(8)) / (float)Math.Max(1, rect.Width - Px(48)), 0, 1);
        if (id == "opacity") MarkerOpacity = (int)Math.Round(5 + fraction * 95);
        if (id == "strength") MosaicBlock = (int)Math.Round(2 + fraction * 38);
    }

    public static string Hint(string? id, bool ocrMode) => id switch
    {
        "ocr" => ocrMode ? UiText.T("退出文字选择（Esc）") : UiText.T("识别文字（OCR）"),
        "qr" => UiText.T("识别二维码 / 条码"),
        "translate" => UiText.T("截图翻译（默认英语 → 中文）"),
        "explain" => UiText.T("AI 解释截图内容"),
        "copy" => ocrMode ? UiText.T("复制截图") : UiText.T("复制截图（Ctrl+C）"),
        "close" => ocrMode ? UiText.T("取消截图") : UiText.T("取消截图（Esc）"),
        "pin" => UiText.T("贴图（Ctrl+T）"), "save" => UiText.T("另存为（Ctrl+S）"),
        "rect" => UiText.T("矩形（R）"), "ellipse" => UiText.T("椭圆（E）"), "arrow" => UiText.T("箭头（A）"),
        "pen" => UiText.T("铅笔（P）"), "marker" => UiText.T("马克笔（H）"), "text" => UiText.T("文字（T）"),
        "mosaic" => UiText.T("马赛克（M）"), "eraser" => UiText.T("橡皮擦（X），仅擦除标注"),
        "fontfamily" => UiText.T("选择字体"), "fontsize" => UiText.T("设置字号（可输入数值）"),
        "colorcur" => UiText.T("自定义 RGB 颜色"), "width" => UiText.T("设置线宽 / 笔刷大小"),
        "modeline" => UiText.T("线段模式"), "modebrush" => UiText.T("涂抹模式"), "moderect" => UiText.T("框选模式"),
        "opacity" => UiText.T("拖动调整马克笔不透明度"), "strength" => UiText.T("拖动调整马赛克程度"),
        "arrowstyle" => UiText.T("箭头样式"), "bold" => UiText.T("粗体"), "italic" => UiText.T("斜体"),
        "resultcopy" => UiText.T("复制二维码 / 条码完整内容"),
        "ocrtextcopy" => UiText.T("复制选中文字，未选字时复制全部识别结果"),
        _ => ""
    };

    public void Render(Graphics g)
    {
        if (_items.Count == 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        DrawBar(g, _main);

        if (_options.Height > 0)
            DrawBar(g, _options);

        if (_message.Height > 0)
        {
            DrawBar(g, _message);
            using var font = new Font("Microsoft YaHei UI", 12f * Scale, GraphicsUnit.Pixel);
            var text = Rectangle.Inflate(_message, -Px(10), 0);
            if (ResultCopyVisible || OcrMode && OcrCopyVisible) text.Width -= ResultButtonWidth() + Px(10);
            TextRenderer.DrawText(g, Status.Replace("\r", " ").Replace("\n", " "), font, text, IconColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        foreach (var (rect, id) in _items)
        {
            if (id is "resultcopy" or "ocrtextcopy")
            {
                using var back = new SolidBrush(AppTheme.AdaptAccent(Accent));
                using var path = FluentDrawing.Rounded(Rectangle.Inflate(rect, -Px(2), -Px(2)), Px(5));
                g.FillPath(back, path);
                using var font = new Font("Microsoft YaHei UI", 11 * Scale, GraphicsUnit.Pixel);
                TextRenderer.DrawText(g, ResultCopied ? UiText.T("已复制") : id == "ocrtextcopy" ? UiText.T("复制文字") : UiText.T("复制"), font, rect, AppTheme.Current.OnAccent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                continue;
            }
            if (id.StartsWith("color", StringComparison.Ordinal))
            {
                if (id == "colorcur")
                {
                    DrawCurrentColor(g, rect);
                    continue;
                }

                var index = int.Parse(id[5..]);
                var swatch = Palette[index];
                DrawSwatch(g, rect, swatch, swatch.ToArgb() == Color.ToArgb());
                continue;
            }

            if (id == "width")
            {
                DrawWidth(g, rect);
                continue;
            }
            if (id is "fontfamily" or "fontsize" or "arrowstyle")
            {
                var label = id switch
                {
                    "fontfamily" => FontFamily,
                    "fontsize" => FontSize.ToString("0.#"),
                    _ => ArrowStyle switch { ArrowStyle.Line => "―", ArrowStyle.Double => "↔", ArrowStyle.Open => "➝", ArrowStyle.Outline => "⇨", _ => "➜" }
                };
                using var border = new Pen(Divider);
                using var field = FluentDrawing.Rounded(Rectangle.Inflate(rect, -1, -1), Px(4));
                g.DrawPath(border, field);
                using var font = new Font("Microsoft YaHei UI", 12 * Scale, GraphicsUnit.Pixel);
                var textRect = Rectangle.Inflate(rect, -Px(6), 0);
                textRect.Width -= Px(12);
                if (id == "arrowstyle")
                {
                    var arrow = new ArrowShape { Start = new Point(textRect.Left + Px(2), textRect.Top + textRect.Height / 2), End = new Point(textRect.Right - Px(2), textRect.Top + textRect.Height / 2), Width = 1.6f * Scale, Color = IconColor, Style = ArrowStyle };
                    using var dummy = new Bitmap(1, 1); arrow.Render(g, dummy);
                }
                else TextRenderer.DrawText(g, label, font, textRect, IconColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                FluentIcons.Draw(g, "chevron", new Rectangle(rect.Right - Px(17), rect.Top + (rect.Height - Px(14)) / 2, Px(14), Px(14)), IconColor);
                continue;
            }
            if (id is "opacity" or "strength") { DrawSlider(g, rect, id); continue; }

            if (id == "fill")
            {
                DrawFill(g, rect, active: Fill);
                continue;
            }

            var active = id switch
            {
                "rect" => Tool == ToolKind.Rectangle,
                "ellipse" => Tool == ToolKind.Ellipse,
                "arrow" => Tool == ToolKind.Arrow,
                "pen" => Tool == ToolKind.Pen,
                "marker" => Tool == ToolKind.Marker,
                "eraser" => Tool == ToolKind.Eraser,
                "text" => Tool == ToolKind.Text,
                "mosaic" => Tool == ToolKind.Mosaic,
                "ocr" => OcrMode,
                "translate" => TranslationMode,
                "explain" => false,
                "bold" => Bold,
                "italic" => Italic,
                "moderect" => RectangleMode,
                "modebrush" or "modeline" => !RectangleMode,
                _ => false
            };
            if (OcrMode && id != "ocr") active = false;

            if (active)
            {
                var selected = Rectangle.Inflate(rect, -Px(2), -Px(2));
                using var fill = new SolidBrush(AppTheme.Current.AccentSoft);
                using var path = FluentDrawing.Rounded(selected, Px(5));
                g.FillPath(fill, path);
                using var indicator = new Pen(AppTheme.AdaptAccent(Accent), Math.Max(2, 2 * Scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(indicator, rect.Left + rect.Width / 2f - Px(5), selected.Bottom - Px(1), rect.Left + rect.Width / 2f + Px(5), selected.Bottom - Px(1));
            }

            DrawIcon(g, id, rect, active);
        }

        // palette grid lines: Snipaste draws the swatches as one solid block
        using (var grid = new Pen(SwatchBorder, 1f))
        {
            foreach (var (rect, id) in _items)
            {
                if (id == "colorcur" || !id.StartsWith("color", StringComparison.Ordinal)) continue;
                g.DrawRectangle(grid, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }
        }

        using var divider = new Pen(Divider, 1f);
        foreach (var d in _dividers)
            g.DrawLine(divider, d.X + 0.5f, d.Y, d.X + 0.5f, d.Bottom);
    }

    private void DrawBar(Graphics g, Rectangle rect)
    {
        using var path = FluentDrawing.Rounded(new RectangleF(rect.X + .5f, rect.Y + .5f, rect.Width - 1, rect.Height - 1), Px(8));
        using (var back = new SolidBrush(BarBack))
            g.FillPath(back, path);

        using var border = new Pen(Divider, 1f);
        g.DrawPath(border, path);
    }

    // -------------------------------------------------------------- options

    private void DrawSwatch(Graphics g, Rectangle rect, Color color, bool selected)
    {
        using (var brush = new SolidBrush(color))
            g.FillRectangle(brush, rect);

        using (var border = new Pen(SwatchBorder, 1f))
            g.DrawRectangle(border, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);

        if (!selected) return;

        var inset = Math.Max(1, Px(1));
        using var ring = new Pen(Color.White, Math.Max(2f, 2f * Scale));
        g.DrawRectangle(ring, rect.X + inset, rect.Y + inset, rect.Width - 1 - inset * 2, rect.Height - 1 - inset * 2);

        using var outline = new Pen(Accent, Math.Max(1f, Scale));
        g.DrawRectangle(outline, rect.X + inset, rect.Y + inset, rect.Width - 1 - inset * 2, rect.Height - 1 - inset * 2);
    }

    private void DrawCurrentColor(Graphics g, Rectangle rect)
    {
        if (Tool == ToolKind.Marker)
        {
            using var white = new SolidBrush(Color.White);
            using var gray = new SolidBrush(Color.FromArgb(210, 210, 210));
            var cell = Math.Max(2, Px(6));
            for (var y = 0; y < rect.Height; y += cell)
                for (var x = 0; x < rect.Width; x += cell)
                    g.FillRectangle((x / cell + y / cell) % 2 == 0 ? white : gray,
                        rect.X + x, rect.Y + y, Math.Min(cell, rect.Width - x), Math.Min(cell, rect.Height - y));
        }
        using (var brush = new SolidBrush(Tool == ToolKind.Marker ? Color.FromArgb((int)(255 * MarkerOpacity / 100d), Color) : Color))
            g.FillRectangle(brush, rect);

        using (var border = new Pen(SwatchBorder, 1f))
            g.DrawRectangle(border, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);

        // the little rainbow corner Snipaste shows on the current colour
        var size = Math.Max(5, rect.Width / 4);

        using var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            new Point(rect.X + 1, rect.Bottom - 2),
            new Point(rect.X + size, rect.Bottom - 2),
            new Point(rect.X + 1, rect.Bottom - size - 1)
        });

        using var rainbow = new LinearGradientBrush(
            new Point(rect.X, rect.Bottom),
            new Point(rect.X + size, rect.Bottom - size),
            Color.Red,
            Color.Blue)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue },
                Positions = new[] { 0f, 0.3f, 0.55f, 0.8f, 1f }
            }
        };

        g.FillPath(rainbow, path);

        using var edge = new Pen(Color.FromArgb(120, 0, 0, 0), 1f);
        g.DrawLine(edge, rect.X + size, rect.Bottom - 2, rect.X + 1, rect.Bottom - size - 1);
    }

    /// <summary>Fill style cell: hatch = outline only, solid square = filled.</summary>
    private void DrawFill(Graphics g, Rectangle rect, bool active)
    {
        var box = new Rectangle(
            rect.X + Px(8),
            rect.Y + Px(8),
            rect.Width - Px(16),
            rect.Height - Px(16));

        if (active)
        {
            using var brush = new SolidBrush(Color);
            g.FillRectangle(brush, box);
            return;
        }

        // diagonal hatch, clipped to the shape, like Snipaste's "no fill"
        var state = g.Save();
        g.SetClip(box);

        using (var pen = new Pen(IconColor, 1f))
        {
            for (var x = box.Left - box.Height; x < box.Right; x += Math.Max(2, Px(3)))
                g.DrawLine(pen, x, box.Bottom, x + box.Height, box.Top);
        }

        g.Restore(state);

        using var border = new Pen(IconColor, 1f);
        g.DrawRectangle(border, box);
    }

    private void DrawWidth(Graphics g, Rectangle rect)    {
        var width = Tool == ToolKind.Marker ? MarkerWidth : Tool is ToolKind.Eraser or ToolKind.Mosaic ? BrushWidth : StrokeWidth;
        var diameter = (int)Math.Clamp(width * 2.4f * Scale, 5f, 16f * Scale);
        var box = new Rectangle(rect.X + (rect.Width - diameter) / 2, rect.Y + (rect.Height - diameter) / 2, diameter, diameter);

        using (var brush = new SolidBrush(Color))
            g.FillEllipse(brush, box);

        using var ring = new Pen(Color.FromArgb(140, 0, 0, 0), 1f);
        g.DrawEllipse(ring, box);
    }

    private void DrawSlider(Graphics g, Rectangle rect, string id)
    {
        var fraction = id == "opacity" ? (MarkerOpacity - 5) / 95f : (MosaicBlock - 2) / 38f;
        var left = rect.Left + Px(8);
        var right = rect.Right - Px(40);
        var y = rect.Top + rect.Height / 2;
        using var track = new Pen(Color.FromArgb(190, 190, 190), Px(3));
        using var active = new Pen(Accent, Px(3));
        g.DrawLine(track, left, y, right, y);
        var x = left + (right - left) * fraction;
        g.DrawLine(active, left, y, x, y);
        using var fill = new SolidBrush(Color.White);
        g.FillRectangle(fill, x - Px(4), y - Px(6), Px(8), Px(12));
        g.DrawRectangle(active, x - Px(4), y - Px(6), Px(8), Px(12));
        using var font = new Font("Segoe UI", 11 * Scale, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, id == "opacity" ? $"{MarkerOpacity}%" : MosaicBlock.ToString(), font,
            new Rectangle(right + Px(5), rect.Y, Px(35), rect.Height), IconColor, TextFormatFlags.VerticalCenter);
    }

    // ---------------------------------------------------------------- icons

    private void DrawIcon(Graphics g, string id, Rectangle rect, bool active)
    {
        var s = Scale;
        var cx = rect.X + rect.Width / 2f;
        var cy = rect.Y + rect.Height / 2f;
        var color = active ? AppTheme.AdaptAccent(Accent) : id == "undo" && !CanUndo || id == "redo" && !CanRedo
            ? AppTheme.Current.Disabled : IconColor;

        if (FluentIcons.Supports(id))
        {
            FluentIcons.Draw(g, id, new RectangleF(cx - 10 * s, cy - 10 * s, 20 * s, 20 * s), color);
            return;
        }

        using var pen = new Pen(color, 1.7f * s)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        using var brush = new SolidBrush(color);

        switch (id)
        {
            case "modebrush":
                g.FillEllipse(brush, cx - 5 * s, cy - 5 * s, 10 * s, 10 * s);
                break;
            case "moderect":
                g.FillRectangle(brush, cx - 7 * s, cy - 5 * s, 14 * s, 10 * s);
                break;
            case "modeline":
                using (var stroke = new Pen(color, 5 * s)) g.DrawLine(stroke, cx - 7 * s, cy + 4 * s, cx + 7 * s, cy - 4 * s);
                break;
            case "marker":
                g.DrawPolygon(pen, new[] { new PointF(cx - 8*s, cy + 3*s), new PointF(cx + 1*s, cy - 8*s), new PointF(cx + 8*s, cy - 1*s), new PointF(cx - 3*s, cy + 8*s) });
                g.DrawLine(pen, cx - 8*s, cy + 7*s, cx - 3*s, cy + 9*s);
                break;
            case "eraser":
                g.DrawPolygon(pen, new[] { new PointF(cx - 8*s, cy + 2*s), new PointF(cx + 1*s, cy - 7*s), new PointF(cx + 8*s, cy), new PointF(cx - 1*s, cy + 9*s) });
                g.DrawLine(pen, cx - 4*s, cy - 2*s, cx + 3*s, cy + 5*s);
                break;
            case "ocr":
            case "qr":
                using (var font = new Font("Segoe UI", (id == "ocr" ? 10f : 12f) * s, FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, id.ToUpperInvariant(), font, rect, color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                break;
            case "translate":
                using (var font = new Font("Microsoft YaHei UI", 14f * s, FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, UiText.T("译"), font, rect, color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                break;

            case "rect":
                using (var path = Rounded(
                           new Rectangle((int)(cx - 7 * s), (int)(cy - 5.5f * s), (int)(14 * s), (int)(11 * s)),
                           Math.Max(2, (int)(2 * s))))
                    g.DrawPath(pen, path);
                break;

            case "ellipse":
                g.DrawEllipse(pen, cx - 7 * s, cy - 5.5f * s, 14 * s, 11 * s);
                break;

            case "arrow":
                g.DrawLine(pen, cx - 7 * s, cy + 5.5f * s, cx + 4.5f * s, cy - 6 * s);
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[]
                    {
                        new PointF(cx + 7 * s, cy - 7.5f * s),
                        new PointF(cx + 1.5f * s, cy - 6.5f * s),
                        new PointF(cx + 5.5f * s, cy - 1.5f * s)
                    });
                    g.FillPath(brush, path);
                }
                break;

            case "pen":
                {
                    var state = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(-45);

                    using (var path = new GraphicsPath())
                    {
                        path.AddRectangle(new RectangleF(-1.7f * s, -8f * s, 3.4f * s, 12f * s));
                        path.AddPolygon(new[]
                        {
                            new PointF(-1.7f * s, 4f * s),
                            new PointF(1.7f * s, 4f * s),
                            new PointF(0, 8f * s)
                        });
                        g.DrawPath(pen, path);
                    }

                    g.DrawLine(pen, -1.7f * s, -2f * s, 1.7f * s, -2f * s);
                    g.Restore(state);
                }
                break;

            case "text":
                g.DrawLine(pen, cx - 6 * s, cy - 6.5f * s, cx + 6 * s, cy - 6.5f * s);
                g.DrawLine(pen, cx, cy - 6.5f * s, cx, cy + 7 * s);
                break;

            case "bold":
                using (var font = new Font("Segoe UI", 13f * s, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("B", font, brush, rect, format);
                break;

            case "italic":
                using (var font = new Font("Times New Roman", 14f * s, FontStyle.Italic, GraphicsUnit.Pixel))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("I", font, brush, rect, format);
                break;

            case "mosaic":
                {
                    var cell = 5.5f * s;
                    for (var row = 0; row < 2; row++)
                    {
                        for (var col = 0; col < 2; col++)
                        {
                            var box = new RectangleF(cx - cell + col * cell, cy - cell + row * cell, cell, cell);

                            if ((row + col) % 2 == 0)
                                g.FillRectangle(brush, box);
                            else
                                g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                        }
                    }
                }
                break;

            case "undo":
                DrawHistory(g, rect, pen, false);
                break;

            case "redo":
                DrawHistory(g, rect, pen, true);
                break;

            case "close":
                g.DrawLine(pen, cx - 5.5f * s, cy - 5.5f * s, cx + 5.5f * s, cy + 5.5f * s);
                g.DrawLine(pen, cx + 5.5f * s, cy - 5.5f * s, cx - 5.5f * s, cy + 5.5f * s);
                break;

            case "pin":
                {
                    var state = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(35);

                    using (var path = new GraphicsPath())
                    {
                        path.AddRectangle(new RectangleF(-4.5f * s, -7.5f * s, 9 * s, 6f * s));
                        path.AddRectangle(new RectangleF(-1.4f * s, -1.5f * s, 2.8f * s, 5* s));
                        g.DrawPath(pen, path);
                    }

                    g.DrawLine(pen, 0, 3.5f * s, 0, 8.5f * s);
                    g.Restore(state);
                }
                break;

            case "save":
                using (var path = Rounded(
                           new Rectangle((int)(cx - 7 * s), (int)(cy - 7 * s), (int)(14 * s), (int)(14 * s)),
                           Math.Max(2, (int)(2 * s))))
                    g.DrawPath(pen, path);

                g.FillRectangle(brush, cx - 3.5f * s, cy - 7 * s, 7 * s, 4 * s);
                g.DrawRectangle(pen, cx - 4.5f * s, cy + 1 * s, 9 * s, 6 * s);
                break;

            case "copy":
                g.DrawRectangle(pen, cx - 7 * s, cy - 7 * s, 9.5f * s, 9.5f * s);
                g.DrawRectangle(pen, cx - 2.5f * s, cy - 2.5f * s, 9.5f * s, 9.5f * s);
                break;
        }
    }

    private void DrawHistory(Graphics g, Rectangle rect, Pen pen, bool redo)
    {
        var s = Scale;
        var cx = rect.X + rect.Width / 2f;
        var cy = rect.Y + rect.Height / 2f;

        var rx = 7f * s;
        var ry = 5.5f * s;
        var centerY = cy + 1.5f * s;

        using (var path = new GraphicsPath())
        {
            path.AddArc(cx - rx, centerY - ry, rx * 2, ry * 2, redo ? 340f : 200f, redo ? -140f : 140f);
            g.DrawPath(pen, path);
        }

        var angle = (redo ? 340f : 200f) * MathF.PI / 180f;
        var tip = new PointF(cx + rx * MathF.Cos(angle), centerY + ry * MathF.Sin(angle));
        var dir = new PointF(redo ? 0.42f : -0.42f, 0.91f);

        var head = 4.2f * s;
        var baseAngle = MathF.Atan2(dir.Y, dir.X);
        var center = new PointF(tip.X + dir.X * head * 0.5f, tip.Y + dir.Y * head * 0.5f);

        for (var side = -1; side <= 1; side += 2)
        {
            var a = baseAngle + MathF.PI + side * 0.55f;
            g.DrawLine(pen, center, new PointF(center.X + MathF.Cos(a) * head, center.Y + MathF.Sin(a) * head));
        }
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        var d = radius * 2;

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}
