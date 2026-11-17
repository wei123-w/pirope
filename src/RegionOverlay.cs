using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenLine
{
    internal sealed class RegionOverlay : Form
    {
        sealed class Mark
        {
            public string Tool;
            public PointF Start;
            public PointF End;
            public List<PointF> Points;
            public string Text;
            public int Number;
            public Color Color;
            public float LineWidth;
            public float FontSize;
            public string FontFamily;
            public bool Bold;
            public bool Italic;
            public bool Filled;
            public int Opacity;
            public int MosaicStrength;
            public bool RectangleMode;
            public int ArrowStyleIndex;
        }

        sealed class UndoRecord
        {
            public Mark Mark;
            public int Index;
            public bool WasAdded;
        }

        enum ResizeHandle { None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }

        sealed class FloatingToolbarRenderer : ToolStripProfessionalRenderer
        {
            static readonly Color Ink = Color.FromArgb(36, 43, 54);
            static readonly Color Active = Color.FromArgb(232, 241, 255);
            static readonly Color Hover = Color.FromArgb(243, 246, 250);

            public FloatingToolbarRenderer() : base(new ProfessionalColorTable()) { RoundedEdges = true; }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                Graphics graphics = e.Graphics;
                GraphicsState state = graphics.Save();
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = new Rectangle(1, 1, e.ToolStrip.Width - 3, e.ToolStrip.Height - 5);
                for (int i = 4; i >= 1; i--)
                {
                    using (var shadow = new SolidBrush(Color.FromArgb(5 + (5 - i) * 3, 18, 27, 42)))
                    using (GraphicsPath shadowPath = RoundedRectangle(new Rectangle(bounds.X, bounds.Y + i, bounds.Width, bounds.Height), 12))
                        graphics.FillPath(shadow, shadowPath);
                }
                using (GraphicsPath panel = RoundedRectangle(bounds, 12))
                using (var fill = new SolidBrush(Color.White))
                using (var border = new Pen(Color.FromArgb(222, 228, 236)))
                {
                    graphics.FillPath(fill, panel);
                    graphics.DrawPath(border, panel);
                }
                graphics.Restore(state);
            }

            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Enabled) return;
                var item = e.Item;
                bool selected = item is ToolStripButton && ((ToolStripButton)item).Checked;
                bool hot = item.Selected || item.Pressed;
                if (!selected && !hot) return;
                Rectangle bounds = new Rectangle(1, 1, item.Bounds.Width - 2, item.Bounds.Height - 2);
                using (GraphicsPath path = RoundedRectangle(bounds, 8))
                using (var brush = new SolidBrush(selected ? Active : Hover))
                    e.Graphics.FillPath(brush, path);
                if (selected)
                {
                    using (var pen = new Pen(Color.FromArgb(205, 222, 247)))
                    using (GraphicsPath path = RoundedRectangle(bounds, 8)) e.Graphics.DrawPath(pen, path);
                }
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                Rectangle bounds = e.Item.Bounds;
                int x = bounds.Left + bounds.Width / 2;
                int top = bounds.Top + 8;
                int bottom = bounds.Bottom - 8;
                using (var pen = new Pen(Color.FromArgb(225, 230, 237))) e.Graphics.DrawLine(pen, x, top, x, bottom);
            }
        }

        sealed class IconButton : ToolStripButton
        {
            readonly string glyph;

            public IconButton(string glyph, string label, string accessibleName)
            {
                this.glyph = glyph;
                ToolTipText = label;
                AccessibleName = accessibleName;
                DisplayStyle = ToolStripItemDisplayStyle.None;
                AutoSize = false;
                Size = new Size(38, 38);
                Margin = new Padding(1);
                CheckOnClick = false;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Rectangle bounds = ContentRectangle;
                Color color = Enabled ? Color.FromArgb(42, 49, 60) : Color.FromArgb(165, 170, 178);
                GraphicsState state = e.Graphics.Save();
                e.Graphics.TranslateTransform(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
                e.Graphics.ScaleTransform(1.18f, 1.18f);
                e.Graphics.TranslateTransform(-(bounds.Left + bounds.Width / 2f), -(bounds.Top + bounds.Height / 2f));
                ToolbarIcon.Draw(e.Graphics, bounds, glyph, color, Checked ? Color.FromArgb(37, 105, 204) : Color.FromArgb(220, 70, 68));
                e.Graphics.Restore(state);
            }
        }

        sealed class ColorChipButton : ToolStripButton
        {
            public Color ColorValue { get; private set; }

            public ColorChipButton(Color color)
            {
                ColorValue = color;
                Tag = color;
                ToolTipText = ColorName(color);
                AccessibleName = ToolTipText;
                DisplayStyle = ToolStripItemDisplayStyle.None;
                AutoSize = false;
                Size = new Size(24, 34);
                Margin = new Padding(1);
                CheckOnClick = false;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
            float diameter = Checked ? 15 : 13;
                float x = (ContentRectangle.Width - diameter) / 2f;
                float y = (ContentRectangle.Height - diameter) / 2f;
                var graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (Checked)
                {
                    using (var ring = new Pen(Color.FromArgb(55, 105, 185), 2)) graphics.DrawEllipse(ring, x - 4, y - 4, diameter + 8, diameter + 8);
                }
                using (var fill = new SolidBrush(ColorValue)) graphics.FillEllipse(fill, x, y, diameter, diameter);
                using (var edge = new Pen(ColorValue.ToArgb() == Color.White.ToArgb() ? Color.FromArgb(155, 164, 177) : Color.FromArgb(35, 48, 64, 82))) graphics.DrawEllipse(edge, x, y, diameter, diameter);
            }
        }

        static class ToolbarIcon
        {
            public static void Draw(Graphics graphics, Rectangle bounds, string glyph, Color ink, Color accent)
            {
                GraphicsState state = graphics.Save();
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float cx = bounds.Left + bounds.Width / 2f;
                float cy = bounds.Top + bounds.Height / 2f;
                using (var pen = new Pen(ink, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                using (var fill = new SolidBrush(ink))
                using (var accentBrush = new SolidBrush(accent))
                {
                    switch (glyph)
                    {
                        case "Rect": graphics.DrawRectangle(pen, cx - 8, cy - 7, 16, 14); break;
                        case "Move":
                            graphics.DrawLine(pen, cx, cy - 8, cx, cy + 8);
                            graphics.DrawLine(pen, cx - 8, cy, cx + 8, cy);
                            graphics.DrawLine(pen, cx, cy - 8, cx - 3, cy - 5);
                            graphics.DrawLine(pen, cx, cy - 8, cx + 3, cy - 5);
                            graphics.DrawLine(pen, cx, cy + 8, cx - 3, cy + 5);
                            graphics.DrawLine(pen, cx, cy + 8, cx + 3, cy + 5);
                            graphics.DrawLine(pen, cx - 8, cy, cx - 5, cy - 3);
                            graphics.DrawLine(pen, cx - 8, cy, cx - 5, cy + 3);
                            graphics.DrawLine(pen, cx + 8, cy, cx + 5, cy - 3);
                            graphics.DrawLine(pen, cx + 8, cy, cx + 5, cy + 3);
                            break;
                        case "Ellipse": graphics.DrawEllipse(pen, cx - 7, cy - 7, 14, 14); break;
                        case "Arrow":
                            graphics.DrawLine(pen, cx - 7, cy + 7, cx + 7, cy - 7);
                            graphics.DrawLine(pen, cx + 1, cy - 7, cx + 7, cy - 7);
                            graphics.DrawLine(pen, cx + 7, cy - 7, cx + 7, cy - 1);
                            break;
                        case "Pen":
                            graphics.DrawLine(pen, cx - 7, cy + 6, cx + 5, cy - 6);
                            graphics.DrawLine(pen, cx + 3, cy - 8, cx + 8, cy - 3);
                            graphics.DrawLine(pen, cx - 8, cy + 8, cx - 4, cy + 5);
                            break;
                        case "Mosaic":
                            for (int row = 0; row < 3; row++)
                                for (int column = 0; column < 3; column++)
                                {
                                    Rectangle cell = new Rectangle((int)cx - 7 + column * 5, (int)cy - 7 + row * 5, 4, 4);
                                    if ((row + column) % 2 == 0) graphics.FillRectangle(fill, cell);
                                    else graphics.DrawRectangle(pen, cell);
                                }
                            break;
                        case "Text":
                            using (var font = new Font("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel))
                            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                                graphics.DrawString("T", font, fill, new RectangleF(cx - 10, cy - 11, 20, 22), format);
                            break;
                        case "Highlight":
                            using (var marker = new Pen(accent, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                                graphics.DrawLine(marker, cx - 7, cy + 5, cx + 6, cy - 5);
                            graphics.DrawLine(pen, cx - 7, cy + 8, cx + 1, cy + 8);
                            break;
                        case "Number":
                            graphics.DrawEllipse(pen, cx - 8, cy - 8, 16, 16);
                            using (var font = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel))
                            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                                graphics.DrawString("1", font, fill, new RectangleF(cx - 7, cy - 7, 14, 14), format);
                            break;
                        case "Eyedropper":
                            graphics.DrawLine(pen, cx - 5, cy + 5, cx + 5, cy - 5);
                            graphics.DrawEllipse(pen, cx + 2, cy - 8, 6, 6);
                            graphics.DrawLine(pen, cx - 7, cy + 7, cx - 3, cy + 3);
                            break;
                        case "ExtractText":
                            graphics.DrawLine(pen, cx - 9, cy - 7, cx - 4, cy - 7);
                            graphics.DrawLine(pen, cx - 9, cy - 7, cx - 9, cy - 2);
                            graphics.DrawLine(pen, cx + 9, cy - 7, cx + 4, cy - 7);
                            graphics.DrawLine(pen, cx + 9, cy - 7, cx + 9, cy - 2);
                            graphics.DrawLine(pen, cx - 9, cy + 7, cx - 4, cy + 7);
                            graphics.DrawLine(pen, cx - 9, cy + 7, cx - 9, cy + 2);
                            graphics.DrawLine(pen, cx + 9, cy + 7, cx + 4, cy + 7);
                            graphics.DrawLine(pen, cx + 9, cy + 7, cx + 9, cy + 2);
                            using (var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel))
                            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                                graphics.DrawString("T", font, fill, new RectangleF(cx - 6, cy - 8, 12, 16), format);
                            break;
                        case "Translate":
                            graphics.DrawRectangle(pen, cx - 9, cy - 8, 12, 16);
                            graphics.DrawLine(pen, cx - 6, cy - 3, cx, cy - 3);
                            graphics.DrawLine(pen, cx - 6, cy, cx, cy);
                            using (var font = new Font("Segoe UI", 8, FontStyle.Bold, GraphicsUnit.Pixel)) graphics.DrawString("文", font, fill, cx + 1, cy - 7);
                            break;
                        case "Explain":
                            graphics.DrawRectangle(pen, cx - 9, cy - 8, 18, 14);
                            graphics.DrawLine(pen, cx - 3, cy + 6, cx - 6, cy + 9);
                            graphics.DrawLine(pen, cx - 6, cy + 9, cx - 6, cy + 5);
                            graphics.DrawEllipse(pen, cx - 1, cy - 4, 2, 2);
                            graphics.DrawLine(pen, cx - 4, cy + 1, cx + 4, cy + 1);
                            break;
                        case "Undo":
                            graphics.DrawArc(pen, cx - 7, cy - 7, 15, 15, 205, 245);
                            graphics.DrawLine(pen, cx - 8, cy - 2, cx - 8, cy - 8);
                            graphics.DrawLine(pen, cx - 8, cy - 8, cx - 2, cy - 8);
                            break;
                        case "Save":
                            graphics.DrawLine(pen, cx, cy - 8, cx, cy + 3);
                            graphics.DrawLine(pen, cx - 4, cy - 1, cx, cy + 3);
                            graphics.DrawLine(pen, cx + 4, cy - 1, cx, cy + 3);
                            graphics.DrawLine(pen, cx - 8, cy + 6, cx + 8, cy + 6);
                            graphics.DrawLine(pen, cx - 8, cy + 6, cx - 8, cy + 2);
                            graphics.DrawLine(pen, cx + 8, cy + 6, cx + 8, cy + 2);
                            break;
                        case "Pin":
                            graphics.DrawLine(pen, cx - 6, cy - 7, cx + 6, cy - 7);
                            graphics.DrawLine(pen, cx - 6, cy - 7, cx - 4, cy - 1);
                            graphics.DrawLine(pen, cx + 6, cy - 7, cx + 4, cy - 1);
                            graphics.DrawLine(pen, cx - 4, cy - 1, cx + 4, cy - 1);
                            graphics.DrawLine(pen, cx, cy - 1, cx, cy + 5);
                            graphics.DrawLine(pen, cx, cy + 5, cx - 5, cy + 10);
                            break;
                        case "Cancel":
                            graphics.DrawLine(pen, cx - 6, cy - 6, cx + 6, cy + 6);
                            graphics.DrawLine(pen, cx + 6, cy - 6, cx - 6, cy + 6);
                            break;
                        case "Confirm":
                            using (var confirm = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            using (var circle = new SolidBrush(accent))
                            {
                                graphics.FillEllipse(circle, cx - 11, cy - 11, 22, 22);
                                graphics.DrawLine(confirm, cx - 5, cy, cx - 1, cy + 4);
                                graphics.DrawLine(confirm, cx - 1, cy + 4, cx + 6, cy - 5);
                            }
                            break;
                        case "CustomColor":
                            graphics.DrawLine(pen, cx - 6, cy, cx + 6, cy);
                            graphics.DrawLine(pen, cx, cy - 6, cx, cy + 6);
                            break;
                    }
                }
                graphics.Restore(state);
            }
        }

        static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Max(1, radius * 2);
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        static readonly string[] Tools = { "Rect", "Ellipse", "Arrow", "Pen", "Mosaic", "Text", "Highlight", "Eraser", "Number", "Eyedropper" };
        static readonly string[] Labels = { "矩形", "圆形", "箭头", "画笔", "马赛克", "文字", "高亮", "橡皮擦", "序号", "取色" };
        static readonly Color[] Palette = { Color.Red, Color.Black, Color.White, Color.Orange, Color.Yellow, Color.Green, Color.Blue, Color.Purple };
        readonly Bitmap desktop;
        readonly Rectangle desktopBounds;
        readonly bool annotate;
        readonly Func<Bitmap, string, string, string, Task<string>> aiAction;
        string initialAiAction;
        readonly Dictionary<string, string> hotkeys;
        readonly List<Mark> marks = new List<Mark>();
        readonly Stack<UndoRecord> undoHistory = new Stack<UndoRecord>();
        readonly Stack<UndoRecord> redoHistory = new Stack<UndoRecord>();
        readonly ToolStrip toolbar = new ToolStrip();
        readonly ToolStrip colorToolbar = new ToolStrip();
        readonly ToolStripComboBox lineWidthPicker = new ToolStripComboBox();
        readonly ToolStripComboBox fontSizePicker = new ToolStripComboBox();
        readonly ToolStripLabel fontSizeLabel = new ToolStripLabel("字号");
        readonly RegionAiPanel aiPanel = new RegionAiPanel();
        readonly SnipLiteCaptureBar captureBar = new SnipLiteCaptureBar();
        readonly ToolTip captureToolTip = new ToolTip { InitialDelay = 420, ReshowDelay = 80, AutoPopDelay = 2400 };
        readonly Dictionary<string, ToolStripButton> toolButtons = new Dictionary<string, ToolStripButton>();
        Bitmap selectionBase;
        Point dragOrigin;
        Point lastPenPoint;
        bool selecting;
        bool selectionLocked;
        bool drawing;
        string drawingTool;
        Mark inProgress;
        string activeTool = "Move";
        string activeCaptureSlider;
        int arrowStyleIndex;
        string currentFontFamily = "Microsoft YaHei";
        bool colorPaletteShown;
        Color currentColor = Color.Red;
        float currentLineWidth = 3;
        float currentFontSize = 24;
        TextBox textEntry;
        bool movingSelection;
        bool resizingSelection;
        bool aiBusy;
        int aiRequestVersion;
        string activeAiAction;
        Point selectionDragStart;
        Rectangle selectionDragOrigin;
        ResizeHandle activeResizeHandle;

        public Rectangle Selection { get; private set; }
        public Bitmap Result { get; private set; }
        public string ActiveTool { get { return activeTool; } }
        public Color CurrentColor { get { return currentColor; } }
        public bool SelectionLocked { get { return selectionLocked; } }
        internal bool PinRequested { get; private set; }
        public int MarkCount { get { return marks.Count; } }

        internal RegionOverlay(Bitmap source, Rectangle bounds, bool annotate, System.Collections.Generic.IDictionary<string, string> hotkeys, Func<Bitmap, string, string, string, Task<string>> aiAction = null, string initialAiAction = null)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (source.Size != bounds.Size) throw new ArgumentException("桌面快照尺寸必须与桌面边界一致。", "bounds");
            desktop = (Bitmap)source.Clone();
            desktopBounds = bounds;
            this.annotate = annotate;
            this.aiAction = aiAction;
            this.initialAiAction = initialAiAction;
            this.hotkeys = hotkeys == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(hotkeys, StringComparer.OrdinalIgnoreCase);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            if (annotate) BuildToolbar();
            MouseDown += PointerDown;
            MouseMove += PointerMove;
            MouseUp += PointerUp;
            KeyDown += KeyPressed;
            FormClosing += delegate { if (Result == null) DialogResult = DialogResult.Cancel; };
        }

        internal void SetSelection(Rectangle selection)
        {
            Rectangle clipped = Rectangle.Intersect(selection, desktopBounds);
            if (clipped.Width < 2 || clipped.Height < 2) throw new ArgumentOutOfRangeException("selection");
            Selection = clipped;
            selectionLocked = true;
            if (selectionBase != null) selectionBase.Dispose();
            selectionBase = ScreenCapture.CropRegion(desktop, desktopBounds, Selection);
            if (!annotate) { ConfirmSelection(); return; }
            SetTool("Move");
            toolbar.Visible = true;
            PlaceToolbar();
            UpdatePointerCursor(PointToClient(Cursor.Position));
            Invalidate();
            if (!string.IsNullOrWhiteSpace(initialAiAction))
            {
                string action = initialAiAction; initialAiAction = null;
                BeginInvoke((Action)(delegate { RunAiAction(action); }));
            }
        }

        internal void SetTool(string name)
        {
            if (name != "Move" && !Tools.Contains(name)) return;
            activeTool = name;
            foreach (var pair in toolButtons) pair.Value.Checked = pair.Key == name;
            captureBar.Tool = name == "Move" ? null : name;
            colorPaletteShown = name != "Move";
            captureBar.OptionsVisible = colorPaletteShown && !aiPanel.Visible;
            colorToolbar.Visible = colorPaletteShown && !aiPanel.Visible;
            fontSizeLabel.Visible = name == "Text";
            fontSizePicker.Visible = name == "Text";
            Cursor = name == "Text" ? Cursors.IBeam : name == "Eyedropper" ? Cursors.Hand : name == "Move" ? Cursors.Default : Cursors.Cross;
            if (selectionLocked) PlaceToolbar();
        }

        void ClickTool(string name)
        {
            if (name == activeTool && name != "Move")
            {
                colorPaletteShown = !colorPaletteShown;
                captureBar.OptionsVisible = colorPaletteShown;
                colorToolbar.Visible = colorPaletteShown;
                if (selectionLocked) PlaceToolbar();
                return;
            }
            SetTool(name);
        }

        internal void SetColor(Color color)
        {
            currentColor = color;
            captureBar.SelectedColor = color;
            foreach (ToolStripItem item in toolbar.Items.Cast<ToolStripItem>().Concat(colorToolbar.Items.Cast<ToolStripItem>()))
            {
                var button = item as ToolStripButton;
                if (button != null && button.Tag is Color) button.Checked = ((Color)button.Tag).ToArgb() == color.ToArgb();
            }
            Invalidate();
        }

        internal void Undo()
        {
            if (inProgress != null) { inProgress = null; drawing = false; }
            else if (undoHistory.Count > 0)
            {
                UndoRecord record = undoHistory.Pop();
                if (record.WasAdded) marks.Remove(record.Mark);
                else marks.Insert(Math.Min(record.Index, marks.Count), record.Mark);
                redoHistory.Push(record);
            }
            PlaceToolbar();
            Invalidate();
        }

        internal void Redo()
        {
            if (redoHistory.Count > 0)
            {
                UndoRecord record = redoHistory.Pop();
                if (record.WasAdded) marks.Insert(Math.Min(record.Index, marks.Count), record.Mark);
                else marks.Remove(record.Mark);
                undoHistory.Push(record);
            }
            PlaceToolbar();
            Invalidate();
        }

        void AddMark(Mark mark)
        {
            int index = marks.Count;
            marks.Add(mark);
            undoHistory.Push(new UndoRecord { Mark = mark, Index = index, WasAdded = true });
            redoHistory.Clear();
        }

        void EraseMarkAt(PointF point)
        {
            float radius = Math.Max(6, captureBar.BrushWidth * 1.5f);
            RectangleF hit = new RectangleF(point.X - radius, point.Y - radius, radius * 2, radius * 2);
            for (int index = marks.Count - 1; index >= 0; index--)
            {
                Mark mark = marks[index];
                RectangleF bounds = RectFromPoints(mark.Start, mark.End);
                if (mark.Tool == "Text") bounds = new RectangleF(mark.Start.X, mark.Start.Y, 320, Math.Max(38, mark.FontSize * 1.8f));
                if (mark.Tool == "Number") bounds = new RectangleF(mark.Start.X - 20, mark.Start.Y - 20, 40, 40);
                if (mark.Points != null && mark.Points.Count > 1)
                {
                    float left = mark.Points.Min(p => p.X), top = mark.Points.Min(p => p.Y);
                    float right = mark.Points.Max(p => p.X), bottom = mark.Points.Max(p => p.Y);
                    bounds = RectangleF.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
                }
                bounds.Inflate(radius, radius);
                if (!bounds.IntersectsWith(hit)) continue;
                marks.RemoveAt(index);
                undoHistory.Push(new UndoRecord { Mark = mark, Index = index, WasAdded = false });
                redoHistory.Clear();
                return;
            }
        }

        Mark CreateActiveMark(PointF point)
        {
            float width = activeTool == "Highlight" ? captureBar.MarkerWidth
                : activeTool == "Mosaic" || activeTool == "Eraser" ? captureBar.BrushWidth : currentLineWidth;
            return new Mark
            {
                Tool = activeTool, Start = point, End = point, Color = currentColor, LineWidth = width,
                FontSize = currentFontSize, FontFamily = currentFontFamily, Bold = captureBar.Bold, Italic = captureBar.Italic,
                Filled = captureBar.Fill, Opacity = captureBar.MarkerOpacity, MosaicStrength = captureBar.MosaicStrength,
                ArrowStyleIndex = arrowStyleIndex,
                RectangleMode = activeTool == "Highlight" ? captureBar.MarkerRectangle : activeTool == "Mosaic" ? captureBar.MosaicRectangle : activeTool == "Eraser" && captureBar.EraserRectangle,
                Points = new List<PointF> { point }
            };
        }

        void EraseMarksInRectangle(RectangleF rectangle)
        {
            if (rectangle.Width < 1 || rectangle.Height < 1) return;
            for (int index = marks.Count - 1; index >= 0; index--)
            {
                Mark mark = marks[index];
                RectangleF bounds = RectFromPoints(mark.Start, mark.End);
                if (mark.Tool == "Text") bounds = new RectangleF(mark.Start.X, mark.Start.Y, 320, Math.Max(38, mark.FontSize * 1.8f));
                if (mark.Points != null && mark.Points.Count > 1)
                {
                    float left = mark.Points.Min(point => point.X), top = mark.Points.Min(point => point.Y);
                    float right = mark.Points.Max(point => point.X), bottom = mark.Points.Max(point => point.Y);
                    bounds = RectangleF.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
                }
                if (!bounds.IntersectsWith(rectangle)) continue;
                marks.RemoveAt(index);
                undoHistory.Push(new UndoRecord { Mark = mark, Index = index, WasAdded = false });
                redoHistory.Clear();
            }
        }

        internal Bitmap BuildResult()
        {
            if (selectionBase == null) throw new InvalidOperationException("请先框选截图区域。");
            var image = (Bitmap)selectionBase.Clone();
            using (Graphics graphics = Graphics.FromImage(image)) DrawMarks(graphics, image.Width, image.Height);
            return image;
        }

        internal void ConfirmSelection()
        {
            if (!selectionLocked) return;
            if (Result != null) Result.Dispose();
            Result = BuildResult();
            DialogResult = DialogResult.OK;
            Close();
        }

        internal void PinSelection()
        {
            if (!selectionLocked) return;
            if (Result != null) Result.Dispose();
            Result = BuildResult();
            PinRequested = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        internal void CancelSelection()
        {
            if (Result != null) { Result.Dispose(); Result = null; }
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(desktop, 0, 0);
            Rectangle localSelection = ToLocal(Selection);
            using (var outside = new Region(ClientRectangle))
            {
                if (localSelection.Width > 0 && localSelection.Height > 0) outside.Exclude(localSelection);
                e.Graphics.SetClip(outside, CombineMode.Replace);
                using (var shade = new SolidBrush(Color.FromArgb(112, Color.Black))) e.Graphics.FillRectangle(shade, ClientRectangle);
                e.Graphics.ResetClip();
            }
            if (selectionLocked && selectionBase != null)
            {
                var state = e.Graphics.Save();
                e.Graphics.TranslateTransform(localSelection.Left, localSelection.Top);
                DrawMarks(e.Graphics, selectionBase.Width, selectionBase.Height);
                if (inProgress != null) DrawMark(e.Graphics, inProgress, selectionBase);
                e.Graphics.Restore(state);
            }
            if (Selection.Width > 0 && Selection.Height > 0)
            {
                using (var border = new Pen(Color.FromArgb(0, 145, 234), 1f))
                    e.Graphics.DrawRectangle(border, localSelection.X - 1, localSelection.Y - 1, localSelection.Width + 1, localSelection.Height + 1);
                if (annotate && selectionLocked) DrawSelectionHandles(e.Graphics, localSelection);
            }
            if (annotate && selectionLocked)
            {
                captureBar.Tool = activeTool == "Move" ? null : activeTool;
                captureBar.OptionsVisible = colorPaletteShown;
                captureBar.CanUndo = undoHistory.Count > 0 || inProgress != null;
                captureBar.CanRedo = redoHistory.Count > 0;
                captureBar.Layout(localSelection, ClientSize);
                captureBar.Draw(e.Graphics);
            }
            if (Selection.Width > 0 && Selection.Height > 0) DrawSelectionBadge(e.Graphics, localSelection);
            if (!selectionLocked)
            {
                using (var font = new Font("Microsoft YaHei", 12, FontStyle.Regular))
                using (var brush = new SolidBrush(Color.White)) e.Graphics.DrawString("拖动框选截图区域，按 Esc 取消", font, brush, 18, 16);
            }
            base.OnPaint(e);
        }

        static void DrawSelectionHandles(Graphics graphics, Rectangle selection)
        {
            float centerX = selection.Left + selection.Width / 2f;
            float centerY = selection.Top + selection.Height / 2f;
            PointF[] centers =
            {
                new PointF(selection.Left, selection.Top), new PointF(centerX, selection.Top), new PointF(selection.Right, selection.Top),
                new PointF(selection.Right, centerY), new PointF(selection.Right, selection.Bottom), new PointF(centerX, selection.Bottom),
                new PointF(selection.Left, selection.Bottom), new PointF(selection.Left, centerY)
            };
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (PointF center in centers)
            {
                int size = 8;
                Rectangle handle = new Rectangle(
                    Math.Max(2, Math.Min((int)Math.Round(center.X - size / 2f), graphics.VisibleClipBounds.Width > size + 4 ? (int)graphics.VisibleClipBounds.Width - size - 2 : 2)),
                    Math.Max(2, Math.Min((int)Math.Round(center.Y - size / 2f), graphics.VisibleClipBounds.Height > size + 4 ? (int)graphics.VisibleClipBounds.Height - size - 2 : 2)), size, size);
                using (var halo = new Pen(Color.FromArgb(150, Color.Black), 3f)) graphics.DrawRectangle(halo, handle);
                using (var fill = new SolidBrush(Color.White)) graphics.FillRectangle(fill, handle);
                using (var border = new Pen(Color.FromArgb(0, 145, 234), 1.5f)) graphics.DrawRectangle(border, handle);
            }
            graphics.Restore(state);
        }

        void DrawSelectionBadge(Graphics graphics, Rectangle selection)
        {
            string text = Selection.Width + " × " + Selection.Height;
            using (var font = new Font("Consolas", 10, FontStyle.Bold))
            {
                SizeF measured = graphics.MeasureString(text, font);
                int padding = 6;
                Size size = new Size((int)Math.Ceiling(measured.Width) + padding * 2, (int)Math.Ceiling(measured.Height) + padding);
                Rectangle badge = SelectionBadgeBounds(selection, size, new Rectangle(Point.Empty, ClientSize), captureBar.Bounds, 10);
                using (var background = new SolidBrush(Color.White))
                using (var foreground = new SolidBrush(Color.FromArgb(38, 45, 55)))
                using (var border = new Pen(Color.FromArgb(220, 226, 234)))
                using (GraphicsPath path = RoundedRectangle(badge, 6))
                {
                    graphics.FillPath(background, path);
                    graphics.DrawPath(border, path);
                    graphics.DrawString(text, font, foreground, badge.X + padding, badge.Y + padding / 2f);
                }
            }
        }

        static Rectangle SelectionBadgeBounds(Rectangle selection, Size size, Rectangle viewport, Rectangle obstacle, int gap)
        {
            size = new Size(Math.Min(size.Width, viewport.Width), Math.Min(size.Height, viewport.Height));
            Func<int, int, Rectangle> place = (x, y) => new Rectangle(
                Math.Max(viewport.Left, Math.Min(viewport.Right - size.Width, x)),
                Math.Max(viewport.Top, Math.Min(viewport.Bottom - size.Height, y)), size.Width, size.Height);
            Rectangle above = place(selection.Left, selection.Top - size.Height - gap);
            Rectangle inside = place(selection.Left + gap, selection.Top + gap);
            if (selection.Top - size.Height - gap >= viewport.Top && !above.IntersectsWith(obstacle)) return above;
            if (!inside.IntersectsWith(obstacle)) return inside;
            Rectangle belowBar = place(selection.Left + gap, obstacle.Bottom + gap);
            if (!belowBar.IntersectsWith(obstacle)) return belowBar;
            Rectangle rightOfBar = place(obstacle.Right + gap, selection.Top + gap);
            if (!rightOfBar.IntersectsWith(obstacle)) return rightOfBar;
            return above;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                desktop.Dispose();
                if (selectionBase != null) selectionBase.Dispose();
                if (Result != null) Result.Dispose();
                captureToolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        void BuildToolbar()
        {
            toolbar.Visible = false;
            toolbar.AutoSize = true;
            toolbar.Dock = DockStyle.None;
            toolbar.GripStyle = ToolStripGripStyle.Hidden;
            toolbar.Padding = new Padding(7, 5, 7, 5);
            toolbar.BackColor = Color.Transparent;
            toolbar.RenderMode = ToolStripRenderMode.Professional;
            toolbar.Renderer = new FloatingToolbarRenderer();
            toolbar.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            toolbar.Cursor = Cursors.Default;
            var moveButton = new IconButton("Move", "移动或调整选区", "移动或调整选区") { Tag = "Move" };
            moveButton.Click += delegate { SetTool("Move"); };
            toolButtons["Move"] = moveButton;
            toolbar.Items.Add(moveButton);
            toolbar.Items.Add(new ToolStripSeparator());
            for (int i = 0; i < Tools.Length; i++)
            {
                string tool = Tools[i];
                string shortcut;
                string hint = hotkeys.TryGetValue(tool, out shortcut) && !string.IsNullOrWhiteSpace(shortcut) ? shortcut : (i + 1).ToString();
                var button = new IconButton(tool, Labels[i] + "  " + hint, Labels[i]) { Tag = tool };
                button.Click += delegate { ClickTool((string)button.Tag); };
                toolButtons[tool] = button;
                toolbar.Items.Add(button);
            }
            toolbar.Items.Add(new ToolStripSeparator());
            AddAction("Translate", "AI 翻译", delegate { RunAiAction("Translate"); });
            AddAction("Explain", "AI 解释", delegate { RunAiAction("Explain"); });
            AddAction("ExtractText", "AI 文字提取", delegate { RunAiAction("ExtractText"); });
            toolbar.Items.Add(new ToolStripSeparator());
            AddAction("Undo", "\u64a4\u9500 Ctrl+Z", delegate { Undo(); });
            AddAction("Save", "\u4fdd\u5b58\u5230\u672c\u5730", SaveLocal);
            AddAction("Pin", "贴图置顶", delegate { PinSelection(); });
            AddAction("Cancel", "\u53d6\u6d88\u622a\u56fe", delegate { CancelSelection(); });
            AddAction("Confirm", "\u786e\u8ba4\u5e76\u6302\u5230\u56fe\u7ef3 Enter", delegate { ConfirmSelection(); });
            BuildColorToolbar();
            aiPanel.Visible = false;
            aiPanel.RetryRequested += delegate { if (!string.IsNullOrWhiteSpace(activeAiAction)) RunAiAction(activeAiAction, true); };
            Controls.Add(aiPanel);
            SetRoundedToolbarRegion(toolbar);
            SetRoundedToolbarRegion(colorToolbar);
            SetTool("Move");
            SetColor(currentColor);
        }

        void BuildColorToolbar()
        {
            colorToolbar.Visible = false;
            colorToolbar.AutoSize = true;
            colorToolbar.Dock = DockStyle.None;
            colorToolbar.GripStyle = ToolStripGripStyle.Hidden;
            colorToolbar.Padding = new Padding(8, 4, 8, 4);
            colorToolbar.BackColor = Color.Transparent;
            colorToolbar.RenderMode = ToolStripRenderMode.Professional;
            colorToolbar.Renderer = new FloatingToolbarRenderer();
            colorToolbar.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            colorToolbar.Cursor = Cursors.Default;
            foreach (Color color in Palette)
            {
                var chip = new ColorChipButton(color);
                chip.Click += delegate { SetColor((Color)chip.Tag); };
                colorToolbar.Items.Add(chip);
            }
            colorToolbar.Items.Add(new ToolStripSeparator());
            colorToolbar.Items.Add(new ToolStripLabel("粗细"));
            lineWidthPicker.AutoSize = false;
            lineWidthPicker.Size = new Size(62, 28);
            lineWidthPicker.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string width in new[] { "1", "2", "3", "4", "6", "8" }) lineWidthPicker.Items.Add(width);
            lineWidthPicker.SelectedItem = "3";
            lineWidthPicker.SelectedIndexChanged += delegate
            {
                float selectedWidth;
                if (float.TryParse(Convert.ToString(lineWidthPicker.SelectedItem), out selectedWidth)) currentLineWidth = selectedWidth;
            };
            colorToolbar.Items.Add(lineWidthPicker);
            colorToolbar.Items.Add(new ToolStripSeparator());
            fontSizeLabel.Visible = false;
            colorToolbar.Items.Add(fontSizeLabel);
            fontSizePicker.AutoSize = false;
            fontSizePicker.Size = new Size(66, 28);
            fontSizePicker.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string size in new[] { "12", "16", "20", "24", "28", "32", "36", "48" }) fontSizePicker.Items.Add(size);
            fontSizePicker.SelectedItem = "24";
            fontSizePicker.Visible = false;
            fontSizePicker.SelectedIndexChanged += delegate
            {
                float selectedSize;
                if (float.TryParse(Convert.ToString(fontSizePicker.SelectedItem), out selectedSize)) currentFontSize = selectedSize;
            };
            colorToolbar.Items.Add(fontSizePicker);
            var custom = new IconButton("CustomColor", "\u81ea\u5b9a\u4e49\u989c\u8272", "\u81ea\u5b9a\u4e49\u989c\u8272") { Size = new Size(34, 34) };
            custom.Click += ChooseCustomColor;
            colorToolbar.Items.Add(custom);
        }

        void SetRoundedToolbarRegion(ToolStrip strip)
        {
            strip.SizeChanged += delegate
            {
                if (strip.Width < 2 || strip.Height < 2) return;
                using (GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, strip.Width, strip.Height), 12))
                {
                    Region previous = strip.Region;
                    strip.Region = new Region(path);
                    if (previous != null) previous.Dispose();
                }
            };
        }

        async void RunAiAction(string action, bool retry = false)
        {
            if (!selectionLocked || IsDisposed) return;
            if (!retry && aiPanel.Visible && activeAiAction == action)
            {
                aiRequestVersion++;
                aiBusy = false;
                aiPanel.Visible = false;
                PlaceToolbar();
                Invalidate();
                return;
            }
            if (aiBusy) return;
            activeAiAction = action;
            aiPanel.Begin(action);
            aiPanel.Visible = true;
            PlaceToolbar();
            aiPanel.BringToFront();
            if (aiAction == null)
            {
                aiPanel.SetFailure("请先在设置的 AI 配置页填写接口地址和模型名称。");
                return;
            }
            aiBusy = true;
            int requestVersion = ++aiRequestVersion;
            try
            {
                using (Bitmap image = BuildResult())
                {
                    string source = action == "Translate" ? aiPanel.SourceLanguage : action == "Explain" ? "自动检测语言" : null;
                    string target = action == "Translate" ? aiPanel.TargetLanguage : action == "Explain" ? "中文简体" : null;
                    string result = await aiAction(image, action, source, target);
                    if (!IsDisposed && requestVersion == aiRequestVersion)
                    {
                        aiPanel.SetResult(result);
                    }
                }
            }
            catch (Exception error)
            {
                if (!IsDisposed && requestVersion == aiRequestVersion)
                {
                    aiPanel.SetFailure(error.Message);
                }
            }
            finally { if (!IsDisposed && requestVersion == aiRequestVersion) aiBusy = false; }
        }
        void AddAction(string glyph, string label, EventHandler action)
        {
            var button = new IconButton(glyph, label, label) { Tag = glyph };
            button.Click += action;
            toolbar.Items.Add(button);
        }
        void ChooseCustomColor(object sender, EventArgs args)
        {
            using (var dialog = new ColorDialog { Color = currentColor, FullOpen = true, AnyColor = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) SetColor(dialog.Color);
        }

        void SaveLocal(object sender, EventArgs args)
        {
            try
            {
                using (var dialog = new SaveFileDialog { Title = "保存标注截图", FileName = "图绳截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png", Filter = "PNG 图片|*.png", DefaultExt = ".png", AddExtension = true, OverwritePrompt = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        using (Bitmap image = BuildResult()) image.Save(dialog.FileName, ImageFormat.Png);
            }
            catch (Exception error) { MessageBox.Show(this, "保存失败：" + error.Message, "截图标注", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            Activate();
        }

        void PlaceToolbar()
        {
            Rectangle selected = ToLocal(Selection);
            captureBar.Tool = activeTool == "Move" ? null : activeTool;
            captureBar.OptionsVisible = colorPaletteShown && !aiPanel.Visible;
            captureBar.SelectedColor = currentColor;
            captureBar.SetStrokeWidth(currentLineWidth);
            captureBar.SetFontSize(currentFontSize);
            captureBar.FontFamily = currentFontFamily;
            captureBar.CanUndo = undoHistory.Count > 0 || inProgress != null;
            captureBar.CanRedo = redoHistory.Count > 0;
            captureBar.AttachedPanelHeight = aiPanel.Visible ? aiPanel.Height : 0;
            captureBar.Layout(selected, ClientSize);
            if (!toolbar.Bounds.IsEmpty) toolbar.Location = captureBar.Bounds.Location;
            colorToolbar.Visible = colorPaletteShown && !aiPanel.Visible;
            if (!colorToolbar.Bounds.IsEmpty && !captureBar.OptionsBounds.IsEmpty) colorToolbar.Location = captureBar.OptionsBounds.Location;
            if (aiPanel.Visible) PlaceAiPanel();
            Invalidate();
        }

        void PlaceAiPanel()
        {
            if (!aiPanel.Visible || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            aiPanel.PerformLayout();
            Rectangle selected = ToLocal(Selection);
            int width = Math.Min(captureBar.Bounds.Width, Math.Max(300, ClientSize.Width - 16));
            int height = Math.Min(aiPanel.Height, Math.Max(120, ClientSize.Height - 16));
            aiPanel.Size = new Size(width, height);
            int left = Math.Max(8, Math.Min(ClientSize.Width - width - 8, captureBar.Bounds.Left));
            int below = captureBar.Bounds.Bottom + 4;
            int above = captureBar.Bounds.Top - height - 4;
            int top = below + height <= ClientSize.Height - 8 ? below : Math.Max(8, above);
            top = Math.Max(8, Math.Min(ClientSize.Height - height - 8, top));
            aiPanel.Location = new Point(left, top);
            aiPanel.BringToFront();
        }

        string CaptureBarHint(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string label;
            switch (id)
            {
                case "Rect": label = "矩形"; break;
                case "Ellipse": label = "圆形"; break;
                case "Arrow": label = "箭头"; break;
                case "Pen": label = "画笔"; break;
                case "Highlight": label = "高亮"; break;
                case "Mosaic": label = "马赛克"; break;
                case "Text": label = "文字"; break;
                case "Eraser": label = "橡皮擦"; break;
                case "Number": label = "序号"; break;
                case "Eyedropper": label = "取色"; break;
                case "Undo": label = "撤销（Ctrl+Z）"; break;
                case "Redo": label = "重做（Ctrl+Y）"; break;
                case "ExtractText": label = "AI 文字提取"; break;
                case "Translate": label = "AI 翻译"; break;
                case "Explain": label = "AI 解释"; break;
                case "Pin": label = "贴图置顶"; break;
                case "Save": label = "保存到本地"; break;
                case "Cancel": label = "取消截图（Esc）"; break;
                case "Confirm": label = "确认并挂到图绳（Enter）"; break;
                case "Width": label = "调整线宽或笔刷大小"; break;
                case "fontsize": label = "调整文字字号"; break;
                case "fontfamily": label = "选择文字字体"; break;
                case "arrowstyle": label = "选择箭头样式"; break;
                case "bold": label = "粗体"; break;
                case "italic": label = "斜体"; break;
                case "fill": label = "切换形状填充"; break;
                case "modebrush": label = "涂抹模式"; break;
                case "moderect": label = "框选模式"; break;
                case "opacity": label = "调整高亮不透明度"; break;
                case "strength": label = "调整马赛克强度"; break;
                case "ColorCurrent":
                case "CustomColor": label = "自定义颜色"; break;
                default:
                    if (id.StartsWith("Color", StringComparison.Ordinal)) return "选择颜色";
                    return null;
            }
            string shortcut;
            if (hotkeys.TryGetValue(id, out shortcut) && !string.IsNullOrWhiteSpace(shortcut)) label += "（" + shortcut + "）";
            return label;
        }

        void HandleCaptureBarClick(string id)
        {
            if (textEntry != null) CommitText();
            if (Tools.Contains(id)) { ClickTool(id); Invalidate(); return; }
            if (id.StartsWith("Color", StringComparison.Ordinal))
            {
                int index;
                if (int.TryParse(id.Substring(5), out index) && index >= 0 && index < SnipLiteCaptureBar.Palette.Length)
                {
                    SetColor(SnipLiteCaptureBar.Palette[index]);
                    return;
                }
            }
            switch (id)
            {
                case "Width":
                    ShowCaptureWidthPicker(); break;
                case "fontsize": ShowFontSizeMenu(); break;
                case "fontfamily": ShowFontFamilyMenu(); break;
                case "arrowstyle": ShowArrowStyleMenu(); break;
                case "bold": captureBar.Bold = !captureBar.Bold; if (textEntry != null) textEntry.Font = new Font(currentFontFamily, currentFontSize, (captureBar.Bold ? FontStyle.Bold : FontStyle.Regular) | (captureBar.Italic ? FontStyle.Italic : FontStyle.Regular)); Invalidate(); break;
                case "italic": captureBar.Italic = !captureBar.Italic; if (textEntry != null) textEntry.Font = new Font(currentFontFamily, currentFontSize, (captureBar.Bold ? FontStyle.Bold : FontStyle.Regular) | (captureBar.Italic ? FontStyle.Italic : FontStyle.Regular)); Invalidate(); break;
                case "fill": captureBar.Fill = !captureBar.Fill; Invalidate(); break;
                case "modebrush":
                case "moderect":
                    bool rectangleMode = id == "moderect";
                    if (activeTool == "Highlight") captureBar.MarkerRectangle = rectangleMode;
                    if (activeTool == "Mosaic") captureBar.MosaicRectangle = rectangleMode;
                    if (activeTool == "Eraser") captureBar.EraserRectangle = rectangleMode;
                    Invalidate(); break;
                case "ColorCurrent":
                case "CustomColor": ChooseCustomColor(this, EventArgs.Empty); break;
                case "Undo": Undo(); PlaceToolbar(); break;
                case "Redo": Redo(); break;
                case "Translate": RunAiAction("Translate"); break;
                case "Explain": RunAiAction("Explain"); break;
                case "ExtractText": RunAiAction("ExtractText"); break;
                case "Pin": PinSelection(); break;
                case "Save": SaveLocal(this, EventArgs.Empty); break;
                case "Cancel": CancelSelection(); break;
                case "Confirm": ConfirmSelection(); break;
            }
        }

        void ShowCaptureWidthPicker()
        {
            var menu = new ContextMenuStrip();
            int value = activeTool == "Highlight" ? captureBar.MarkerWidth
                : activeTool == "Mosaic" || activeTool == "Eraser" ? captureBar.BrushWidth : (int)currentLineWidth;
            var slider = new TrackBar { Minimum = 1, Maximum = 100, Value = Math.Max(1, Math.Min(100, value)), TickFrequency = 10, Width = 210, AutoSize = false };
            slider.ValueChanged += delegate
            {
                captureBar.SetToolWidth(slider.Value);
                if (activeTool != "Highlight" && activeTool != "Mosaic" && activeTool != "Eraser") currentLineWidth = slider.Value;
                Invalidate();
            };
            menu.Items.Add(new ToolStripControlHost(slider) { AutoSize = false, Size = new Size(220, 48), Padding = new Padding(4) });
            Rectangle bounds = captureBar.ItemBounds("Width");
            menu.Show(this, new Point(bounds.Left, bounds.Bottom));
        }

        void ShowFontSizeMenu()
        {
            var menu = new ContextMenuStrip();
            foreach (int size in new[] { 8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 48, 64, 72 })
            {
                int selectedSize = size;
                var item = new ToolStripMenuItem(size + " pt", null, delegate
                {
                    currentFontSize = selectedSize;
                    captureBar.SetFontSize(selectedSize);
                    if (textEntry != null) textEntry.Font = new Font(currentFontFamily, selectedSize, (captureBar.Bold ? FontStyle.Bold : FontStyle.Regular) | (captureBar.Italic ? FontStyle.Italic : FontStyle.Regular));
                    PlaceToolbar(); Invalidate();
                }) { Checked = Math.Abs(currentFontSize - size) < .1f };
                menu.Items.Add(item);
            }
            Rectangle bounds = captureBar.ItemBounds("fontsize");
            menu.Show(this, new Point(bounds.Left, bounds.Bottom));
        }

        void ShowFontFamilyMenu()
        {
            var menu = new ContextMenuStrip();
            foreach (string family in new[] { "Microsoft YaHei", "Segoe UI", "Arial", "Consolas", "Times New Roman" })
            {
                string selectedFamily = family;
                menu.Items.Add(new ToolStripMenuItem(family, null, delegate
                {
                    currentFontFamily = selectedFamily;
                    captureBar.FontFamily = selectedFamily;
                    if (textEntry != null) textEntry.Font = new Font(selectedFamily, currentFontSize, (captureBar.Bold ? FontStyle.Bold : FontStyle.Regular) | (captureBar.Italic ? FontStyle.Italic : FontStyle.Regular));
                    PlaceToolbar(); Invalidate();
                }) { Checked = string.Equals(currentFontFamily, family, StringComparison.OrdinalIgnoreCase) });
            }
            Rectangle bounds = captureBar.ItemBounds("fontfamily");
            menu.Show(this, new Point(bounds.Left, bounds.Bottom));
        }

        void ShowArrowStyleMenu()
        {
            var menu = new ContextMenuStrip();
            string[] names = { "标准箭头", "直线", "双向箭头", "实心箭头", "细箭头" };
            for (int index = 0; index < names.Length; index++)
            {
                int selected = index;
                menu.Items.Add(new ToolStripMenuItem(names[index], null, delegate
                {
                    arrowStyleIndex = selected;
                    captureBar.ArrowStyleIndex = selected;
                    Invalidate();
                }) { Checked = arrowStyleIndex == index });
            }
            Rectangle bounds = captureBar.ItemBounds("arrowstyle");
            menu.Show(this, new Point(bounds.Left, bounds.Bottom));
        }

        void PointerDown(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left || !annotate && selectionLocked) return;
            if (!selectionLocked)
            {
                dragOrigin = new Point(args.X + desktopBounds.Left, args.Y + desktopBounds.Top);
                selecting = true;
                Selection = new Rectangle(dragOrigin, Size.Empty);
                Invalidate();
                return;
            }
            string barItem = captureBar.HitTest(args.Location);
            if (barItem == "opacity" || barItem == "strength")
            {
                activeCaptureSlider = barItem;
                captureBar.SetSlider(barItem, args.Location);
                Capture = true;
                Invalidate();
                return;
            }
            if (barItem != null) { HandleCaptureBarClick(barItem); return; }
            if (textEntry != null) CommitText();
            Rectangle localSelection = ToLocal(Selection);
            ResizeHandle handle = HitTestHandle(args.Location, localSelection);
            if (handle != ResizeHandle.None)
            {
                resizingSelection = true;
                activeResizeHandle = handle;
                selectionDragStart = args.Location;
                selectionDragOrigin = localSelection;
                Cursor = CursorForHandle(handle);
                return;
            }
            if (activeTool == "Move" && localSelection.Contains(args.Location))
            {
                movingSelection = true;
                selectionDragStart = args.Location;
                selectionDragOrigin = localSelection;
                Cursor = Cursors.SizeAll;
                return;
            }
            if (activeTool == "Move") return;
            PointF point = new PointF(args.X - localSelection.Left, args.Y - localSelection.Top);
            if (point.X < 0 || point.Y < 0 || point.X >= Selection.Width || point.Y >= Selection.Height) return;
            if (activeTool == "Eyedropper") { SetColor(selectionBase.GetPixel(Math.Min(selectionBase.Width - 1, (int)point.X), Math.Min(selectionBase.Height - 1, (int)point.Y))); SetTool("Rect"); return; }
            if (activeTool == "Text") { StartText(point); return; }
            if (activeTool == "Number") { AddMark(new Mark { Tool = activeTool, Start = point, Number = marks.Count(mark => mark.Tool == "Number") + 1, Color = currentColor, LineWidth = currentLineWidth, FontSize = currentFontSize }); Invalidate(); return; }
            if (activeTool == "Eraser")
            {
                drawing = true; drawingTool = activeTool;
                if (captureBar.EraserRectangle) inProgress = CreateActiveMark(point);
                else EraseMarkAt(point);
                Invalidate(); return;
            }
            drawing = true;
            drawingTool = activeTool;
            lastPenPoint = Point.Round(point);
            inProgress = CreateActiveMark(point);
            Invalidate();
        }

        void PointerMove(object sender, MouseEventArgs args)
        {
            if (activeCaptureSlider != null)
            {
                captureBar.SetSlider(activeCaptureSlider, args.Location);
                Invalidate();
                return;
            }
            if (captureBar.SetHover(args.Location))
            {
                Invalidate();
                string hint = CaptureBarHint(captureBar.HitTest(args.Location));
                captureToolTip.Hide(this);
                if (!string.IsNullOrWhiteSpace(hint)) captureToolTip.Show(hint, this, args.X + 14, args.Y + 18, 1800);
            }
            if (selecting)
            {
                Point current = new Point(args.X + desktopBounds.Left, args.Y + desktopBounds.Top);
                Selection = RectFromPoints(dragOrigin, current);
                Invalidate();
            }
            else if (movingSelection || resizingSelection)
            {
                int deltaX = args.X - selectionDragStart.X;
                int deltaY = args.Y - selectionDragStart.Y;
                Rectangle updated = ResizeSelection(selectionDragOrigin, activeResizeHandle, deltaX, deltaY);
                if (movingSelection) updated = MoveSelection(selectionDragOrigin, deltaX, deltaY);
                Selection = new Rectangle(updated.X + desktopBounds.X, updated.Y + desktopBounds.Y, updated.Width, updated.Height);
                PlaceToolbar();
                Cursor = movingSelection ? Cursors.SizeAll : CursorForHandle(activeResizeHandle);
                Invalidate();
            }
            else if (drawing && drawingTool == "Eraser")
            {
                PointF point = new PointF(args.X - ToLocal(Selection).Left, args.Y - ToLocal(Selection).Top);
                if (inProgress != null) inProgress.End = point;
                else EraseMarkAt(point);
                PlaceToolbar();
                Invalidate();
            }
            else if (drawing && inProgress != null)
            {
                PointF point = new PointF(args.X - ToLocal(Selection).Left, args.Y - ToLocal(Selection).Top);
                inProgress.End = point;
                bool freehand = drawingTool == "Pen" || drawingTool == "Highlight" && !captureBar.MarkerRectangle || drawingTool == "Mosaic" && !captureBar.MosaicRectangle;
                if (freehand && lastPenPoint != Point.Round(point)) { inProgress.Points.Add(point); lastPenPoint = Point.Round(point); }
                Invalidate();
            }
            else if (selectionLocked) UpdatePointerCursor(args.Location);
        }

        void PointerUp(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left) return;
            if (activeCaptureSlider != null)
            {
                activeCaptureSlider = null;
                Capture = false;
                Invalidate();
                return;
            }
            if (selecting)
            {
                selecting = false;
                Rectangle selection = Selection;
                if (selection.Width < 2 || selection.Height < 2) { Selection = Rectangle.Empty; Invalidate(); return; }
                SetSelection(selection);
            }
            else if (movingSelection || resizingSelection)
            {
                if (resizingSelection) ShiftMarksForResize(selectionDragOrigin, ToLocal(Selection));
                movingSelection = false;
                resizingSelection = false;
                activeResizeHandle = ResizeHandle.None;
                RefreshSelectionBase();
                PlaceToolbar();
                UpdatePointerCursor(args.Location);
                Invalidate();
            }
            else if (drawing && drawingTool == "Eraser")
            {
                if (inProgress != null)
                {
                    EraseMarksInRectangle(RectFromPoints(inProgress.Start, inProgress.End));
                    inProgress = null;
                }
                drawing = false;
                drawingTool = null;
                PlaceToolbar();
                Invalidate();
            }
            else if (drawing && inProgress != null)
            {
                PointF point = new PointF(args.X - ToLocal(Selection).Left, args.Y - ToLocal(Selection).Top);
                inProgress.End = point;
                bool freehand = drawingTool == "Pen" || drawingTool == "Highlight" && !captureBar.MarkerRectangle || drawingTool == "Mosaic" && !captureBar.MosaicRectangle;
                if (freehand && inProgress.Points.Count == 1) inProgress.Points.Add(point);
                if (Math.Abs(inProgress.End.X - inProgress.Start.X) > 1 || Math.Abs(inProgress.End.Y - inProgress.Start.Y) > 1) AddMark(inProgress);
                inProgress = null;
                drawing = false;
                Invalidate();
            }
        }

        ResizeHandle HitTestHandle(Point point, Rectangle rectangle)
        {
            int left = rectangle.Left, top = rectangle.Top, right = rectangle.Right, bottom = rectangle.Bottom;
            int middleX = left + rectangle.Width / 2, middleY = top + rectangle.Height / 2;
            Point[] centers =
            {
                new Point(left, top), new Point(middleX, top), new Point(right, top), new Point(right, middleY),
                new Point(right, bottom), new Point(middleX, bottom), new Point(left, bottom), new Point(left, middleY)
            };
            for (int i = 0; i < centers.Length; i++)
                if (Math.Abs(point.X - centers[i].X) <= 9 && Math.Abs(point.Y - centers[i].Y) <= 9) return (ResizeHandle)(i + 1);
            return ResizeHandle.None;
        }

        static Cursor CursorForHandle(ResizeHandle handle)
        {
            switch (handle)
            {
                case ResizeHandle.TopLeft:
                case ResizeHandle.BottomRight: return Cursors.SizeNWSE;
                case ResizeHandle.TopRight:
                case ResizeHandle.BottomLeft: return Cursors.SizeNESW;
                case ResizeHandle.Top:
                case ResizeHandle.Bottom: return Cursors.SizeNS;
                case ResizeHandle.Left:
                case ResizeHandle.Right: return Cursors.SizeWE;
                default: return Cursors.Default;
            }
        }

        void UpdatePointerCursor(Point point)
        {
            if (captureBar.HitTest(point) != null) { Cursor = Cursors.Hand; return; }
            Rectangle local = ToLocal(Selection);
            ResizeHandle handle = HitTestHandle(point, local);
            if (handle != ResizeHandle.None) Cursor = CursorForHandle(handle);
            else if (activeTool == "Move" && local.Contains(point)) Cursor = Cursors.SizeAll;
            else if (activeTool == "Text") Cursor = Cursors.IBeam;
            else if (activeTool == "Eyedropper") Cursor = Cursors.Hand;
            else Cursor = Cursors.Cross;
        }

        Rectangle MoveSelection(Rectangle origin, int deltaX, int deltaY)
        {
            int maxX = Math.Max(0, ClientSize.Width - origin.Width);
            int maxY = Math.Max(0, ClientSize.Height - origin.Height);
            int left = Math.Max(0, Math.Min(maxX, origin.Left + deltaX));
            int top = Math.Max(0, Math.Min(maxY, origin.Top + deltaY));
            return new Rectangle(left, top, Math.Min(origin.Width, ClientSize.Width), Math.Min(origin.Height, ClientSize.Height));
        }

        Rectangle ResizeSelection(Rectangle origin, ResizeHandle handle, int deltaX, int deltaY)
        {
            int left = origin.Left, top = origin.Top, right = origin.Right, bottom = origin.Bottom;
            if (handle == ResizeHandle.TopLeft || handle == ResizeHandle.Left || handle == ResizeHandle.BottomLeft)
                left = Math.Max(0, Math.Min(right - 2, left + deltaX));
            if (handle == ResizeHandle.TopRight || handle == ResizeHandle.Right || handle == ResizeHandle.BottomRight)
                right = Math.Min(ClientSize.Width, Math.Max(left + 2, right + deltaX));
            if (handle == ResizeHandle.TopLeft || handle == ResizeHandle.Top || handle == ResizeHandle.TopRight)
                top = Math.Max(0, Math.Min(bottom - 2, top + deltaY));
            if (handle == ResizeHandle.BottomLeft || handle == ResizeHandle.Bottom || handle == ResizeHandle.BottomRight)
                bottom = Math.Min(ClientSize.Height, Math.Max(top + 2, bottom + deltaY));
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        void ShiftMarksForResize(Rectangle before, Rectangle after)
        {
            float offsetX = after.Left - before.Left;
            float offsetY = after.Top - before.Top;
            if (offsetX == 0 && offsetY == 0) return;
            foreach (Mark mark in marks)
            {
                mark.Start = new PointF(mark.Start.X - offsetX, mark.Start.Y - offsetY);
                mark.End = new PointF(mark.End.X - offsetX, mark.End.Y - offsetY);
                if (mark.Points != null)
                    for (int i = 0; i < mark.Points.Count; i++) mark.Points[i] = new PointF(mark.Points[i].X - offsetX, mark.Points[i].Y - offsetY);
            }
        }

        void RefreshSelectionBase()
        {
            if (selectionBase != null) selectionBase.Dispose();
            selectionBase = ScreenCapture.CropRegion(desktop, desktopBounds, Selection);
        }

        void KeyPressed(object sender, KeyEventArgs args)
        {
            if (textEntry != null) return;
            if (args.KeyCode == Keys.Escape) { args.Handled = true; CancelSelection(); return; }
            if (args.Control && !args.Shift && args.KeyCode == Keys.Z) { args.Handled = true; Undo(); return; }
            if (args.Control && (args.KeyCode == Keys.Y || args.Shift && args.KeyCode == Keys.Z)) { args.Handled = true; Redo(); return; }
            if (args.KeyCode == Keys.Enter && selectionLocked) { args.Handled = true; ConfirmSelection(); return; }
            if (!selectionLocked) return;
            string modifiers = ModifierText(args);
            foreach (string tool in Tools)
            {
                string configured;
                if (!hotkeys.TryGetValue(tool, out configured) || string.IsNullOrWhiteSpace(configured)) continue;
                string[] parts = configured.Split('+').Select(part => part.Trim()).ToArray();
                if (parts.Length == 0) continue;
                string key = KeyName(args.KeyCode);
                string configuredKey = parts[parts.Length - 1];
                bool mainMatches = string.Equals(configuredKey, key, StringComparison.OrdinalIgnoreCase)
                    || (key.Length == 1 && char.IsDigit(key[0]) && string.Equals(configuredKey, "Numpad" + key, StringComparison.OrdinalIgnoreCase))
                    || (key.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase) && string.Equals(configuredKey, key.Substring(6), StringComparison.OrdinalIgnoreCase));
                string expectedModifiers = string.Join("+", parts.Take(parts.Length - 1).OrderBy(part => ModifierOrder(part), StringComparer.OrdinalIgnoreCase));
                if (mainMatches && string.Equals(expectedModifiers, modifiers, StringComparison.OrdinalIgnoreCase))
                {
                    SetTool(tool); args.Handled = true; return;
                }
            }
        }

        string ModifierText(KeyEventArgs args)
        {
            var modifiers = new List<string>();
            if (args.Control) modifiers.Add("Ctrl");
            if (args.Alt) modifiers.Add("Alt");
            if (args.Shift) modifiers.Add("Shift");
            if ((WindowsNative.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (WindowsNative.GetAsyncKeyState(0x5C) & 0x8000) != 0) modifiers.Add("Win");
            return string.Join("+", modifiers.OrderBy(ModifierOrder, StringComparer.OrdinalIgnoreCase));
        }

        static string ModifierOrder(string value)
        {
            switch (value.ToLowerInvariant()) { case "ctrl": return "1"; case "alt": return "2"; case "shift": return "3"; case "win": return "4"; default: return "5"; }
        }

        static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)key - (int)Keys.D0).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "Numpad" + ((int)key - (int)Keys.NumPad0);
            string name = key.ToString();
            switch (name)
            {
                case "OemSemicolon": return "Semicolon";
                case "Oemplus": return "Equal";
                case "Oemcomma": return "Comma";
                case "OemMinus": return "Minus";
                case "OemPeriod": return "Period";
                case "OemQuestion": return "Slash";
                case "Oemtilde": return "Backquote";
                case "OemOpenBrackets": return "BracketLeft";
                case "OemPipe": return "Backslash";
                case "OemCloseBrackets": return "BracketRight";
                case "OemQuotes": return "Quote";
                case "Return": return "Enter";
                case "Back": return "Backspace";
                case "Left": return "ArrowLeft";
                case "Right": return "ArrowRight";
                case "Up": return "ArrowUp";
                case "Down": return "ArrowDown";
                default: return name;
            }
        }

        void StartText(PointF point)
        {
            if (textEntry != null) CommitText();
            Point origin = ToLocal(Selection).Location;
            FontStyle style = (captureBar.Bold ? FontStyle.Bold : FontStyle.Regular) | (captureBar.Italic ? FontStyle.Italic : FontStyle.Regular);
            textEntry = new TextBox { BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, ForeColor = currentColor, Font = new Font(currentFontFamily, currentFontSize, style), Width = Math.Min(320, Selection.Width - (int)point.X), Height = Math.Max(38, (int)(currentFontSize * 1.8f)), Location = new Point(origin.X + (int)point.X, origin.Y + (int)point.Y) };
            textEntry.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Enter && !args.Shift) { args.SuppressKeyPress = true; CommitText(); }
                else if (args.KeyCode == Keys.Escape) { args.SuppressKeyPress = true; Controls.Remove(textEntry); textEntry.Dispose(); textEntry = null; Invalidate(); }
            };
            textEntry.LostFocus += delegate { if (textEntry != null) CommitText(); };
            Controls.Add(textEntry); textEntry.BringToFront(); textEntry.Focus();
        }

        void CommitText()
        {
            if (textEntry == null) return;
            string value = textEntry.Text.Trim();
            Point location = textEntry.Location;
            Point selectionOrigin = ToLocal(Selection).Location;
            if (value.Length > 0) AddMark(new Mark { Tool = "Text", Start = new PointF(location.X - selectionOrigin.X, location.Y - selectionOrigin.Y), Text = value, Color = currentColor, LineWidth = currentLineWidth, FontSize = currentFontSize, FontFamily = currentFontFamily, Bold = captureBar.Bold, Italic = captureBar.Italic });
            Controls.Remove(textEntry); textEntry.Dispose(); textEntry = null; Invalidate();
        }

        void DrawMarks(Graphics graphics, int width, int height)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (Mark mark in marks) DrawMark(graphics, mark, selectionBase);
        }

        void DrawMark(Graphics graphics, Mark mark, Bitmap source)
        {
            RectangleF box = RectFromPoints(mark.Start, mark.End);
            float lineWidth = mark.LineWidth > 0 ? mark.LineWidth : Math.Max(2, Math.Min(Selection.Width, Selection.Height) * .006f);
            using (var pen = new Pen(mark.Color, lineWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(mark.Color))
            {
                switch (mark.Tool)
                {
                    case "Rect":
                        if (mark.Filled) graphics.FillRectangle(brush, box);
                        graphics.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height); break;
                    case "Ellipse":
                        if (mark.Filled) graphics.FillEllipse(brush, box);
                        graphics.DrawEllipse(pen, box); break;
                    case "Arrow":
                        graphics.DrawLine(pen, mark.Start, mark.End);
                        float angle = (float)Math.Atan2(mark.End.Y - mark.Start.Y, mark.End.X - mark.Start.X);
                        float head = Math.Max(12, lineWidth * 4);
                        PointF left = new PointF(mark.End.X - head * (float)Math.Cos(angle - Math.PI / 6), mark.End.Y - head * (float)Math.Sin(angle - Math.PI / 6));
                        PointF right = new PointF(mark.End.X - head * (float)Math.Cos(angle + Math.PI / 6), mark.End.Y - head * (float)Math.Sin(angle + Math.PI / 6));
                        if (mark.ArrowStyleIndex == 2)
                        {
                            float reverse = angle + (float)Math.PI;
                            PointF backLeft = new PointF(mark.Start.X - head * (float)Math.Cos(reverse - Math.PI / 6), mark.Start.Y - head * (float)Math.Sin(reverse - Math.PI / 6));
                            PointF backRight = new PointF(mark.Start.X - head * (float)Math.Cos(reverse + Math.PI / 6), mark.Start.Y - head * (float)Math.Sin(reverse + Math.PI / 6));
                            graphics.DrawLine(pen, mark.Start, backLeft); graphics.DrawLine(pen, mark.Start, backRight);
                        }
                        if (mark.ArrowStyleIndex == 1) break;
                        if (mark.ArrowStyleIndex == 3)
                        {
                            using (var arrowBrush = new SolidBrush(mark.Color)) graphics.FillPolygon(arrowBrush, new[] { mark.End, left, right });
                        }
                        else { graphics.DrawLine(pen, mark.End, left); graphics.DrawLine(pen, mark.End, right); }
                        break;
                    case "Pen":
                        if (mark.Points != null && mark.Points.Count > 1) graphics.DrawLines(pen, mark.Points.ToArray());
                        else graphics.DrawLine(pen, mark.Start, mark.End);
                        break;
                    case "Mosaic":
                        if (mark.RectangleMode || mark.Points == null || mark.Points.Count < 2) DrawMosaic(graphics, box, source, mark.MosaicStrength);
                        else
                        {
                            using (var path = new GraphicsPath())
                            using (var mosaicPen = new Pen(Color.Black, Math.Max(1, mark.LineWidth)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                            {
                                path.AddLines(mark.Points.ToArray());
                                path.Widen(mosaicPen);
                                GraphicsState state = graphics.Save(); graphics.SetClip(path);
                                RectangleF area = RectangleF.FromLTRB(mark.Points.Min(point => point.X), mark.Points.Min(point => point.Y), mark.Points.Max(point => point.X), mark.Points.Max(point => point.Y));
                                area.Inflate(mark.LineWidth / 2, mark.LineWidth / 2);
                                DrawMosaic(graphics, area, source, mark.MosaicStrength);
                                graphics.Restore(state);
                            }
                        }
                        break;
                    case "Highlight":
                        if (mark.RectangleMode || mark.Points == null || mark.Points.Count < 2)
                        {
                            using (var translucent = new SolidBrush(Color.FromArgb((int)(255 * Math.Max(5, mark.Opacity) / 100f), mark.Color))) graphics.FillRectangle(translucent, box);
                        }
                        else
                        {
                            using (var markerPen = new Pen(Color.FromArgb((int)(255 * Math.Max(5, mark.Opacity) / 100f), mark.Color), Math.Max(1, mark.LineWidth)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) graphics.DrawLines(markerPen, mark.Points.ToArray());
                        }
                        break;
                    case "Text":
                        FontStyle style = (mark.Bold ? FontStyle.Bold : FontStyle.Regular) | (mark.Italic ? FontStyle.Italic : FontStyle.Regular);
                        using (var font = new Font(string.IsNullOrEmpty(mark.FontFamily) ? "Microsoft YaHei" : mark.FontFamily, mark.FontSize > 0 ? mark.FontSize : Math.Max(16, Selection.Width * .027f), style)) graphics.DrawString(mark.Text, font, brush, mark.Start);
                        break;
                    case "Number":
                        float radius = Math.Max(13, Math.Min(Selection.Width, Selection.Height) * .025f);
                        var circle = new RectangleF(mark.Start.X - radius, mark.Start.Y - radius, radius * 2, radius * 2);
                        graphics.FillEllipse(brush, circle);
                        using (var font = new Font("Segoe UI", radius * 1.1f, FontStyle.Bold))
                        using (var white = new SolidBrush(Color.White))
                        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) graphics.DrawString(mark.Number.ToString(), font, white, circle, format);
                        break;
                }
            }
        }

        static void DrawMosaic(Graphics graphics, RectangleF box, Bitmap source, int blockSize)
        {
            if (source == null || box.Width < 1 || box.Height < 1) return;
            Rectangle target = Rectangle.Intersect(Rectangle.Round(box), new Rectangle(Point.Empty, source.Size));
            if (target.Width < 1 || target.Height < 1) return;
            int divisor = Math.Max(2, Math.Min(40, blockSize));
            using (Bitmap crop = source.Clone(target, PixelFormat.Format32bppArgb))
            using (var small = new Bitmap(Math.Max(1, target.Width / divisor), Math.Max(1, target.Height / divisor), PixelFormat.Format32bppArgb))
            using (Graphics sample = Graphics.FromImage(small))
            {
                sample.InterpolationMode = InterpolationMode.HighQualityBilinear;
                sample.DrawImage(crop, new Rectangle(Point.Empty, small.Size));
                var previous = graphics.InterpolationMode;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(small, target);
                graphics.InterpolationMode = previous;
                graphics.PixelOffsetMode = PixelOffsetMode.Default;
            }
        }

        static string ColorName(Color color)
        {
            if (color.ToArgb() == Color.Red.ToArgb()) return "红色";
            if (color.ToArgb() == Color.Black.ToArgb()) return "黑色";
            if (color.ToArgb() == Color.White.ToArgb()) return "白色";
            if (color.ToArgb() == Color.Orange.ToArgb()) return "橙色";
            if (color.ToArgb() == Color.Yellow.ToArgb()) return "黄色";
            if (color.ToArgb() == Color.Green.ToArgb()) return "绿色";
            if (color.ToArgb() == Color.Blue.ToArgb()) return "蓝色";
            return "紫色";
        }

        Rectangle ToLocal(Rectangle global) { return new Rectangle(global.X - desktopBounds.X, global.Y - desktopBounds.Y, global.Width, global.Height); }

        static RectangleF RectFromPoints(PointF a, PointF b)
        {
            return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        static Rectangle RectFromPoints(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }
    }
}
