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
                Size = new Size(42, 42);
                Margin = new Padding(2);
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
                Size = new Size(28, 42);
                Margin = new Padding(1, 2, 1, 2);
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

        static readonly string[] Tools = { "Rect", "Ellipse", "Arrow", "Pen", "Mosaic", "Text", "Highlight", "Number", "Eyedropper" };
        static readonly string[] Labels = { "矩形", "圆形", "箭头", "画笔", "马赛克", "文字", "高亮", "序号", "取色" };
        static readonly Color[] Palette = { Color.Red, Color.Black, Color.White, Color.Orange, Color.Yellow, Color.Green, Color.Blue, Color.Purple };
        readonly Bitmap desktop;
        readonly Rectangle desktopBounds;
        readonly bool annotate;
        readonly Func<Bitmap, string, string, string, Task<string>> aiAction;
        string initialAiAction;
        readonly Dictionary<string, string> hotkeys;
        readonly List<Mark> marks = new List<Mark>();
        readonly ToolStrip toolbar = new ToolStrip();
        readonly ToolStrip colorToolbar = new ToolStrip();
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
        bool colorPaletteShown;
        Color currentColor = Color.Red;
        TextBox textEntry;
        bool movingSelection;
        bool resizingSelection;
        bool aiBusy;
        Point selectionDragStart;
        Rectangle selectionDragOrigin;
        ResizeHandle activeResizeHandle;

        public Rectangle Selection { get; private set; }
        public Bitmap Result { get; private set; }
        public string ActiveTool { get { return activeTool; } }
        public Color CurrentColor { get { return currentColor; } }
        public bool SelectionLocked { get { return selectionLocked; } }
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
            colorPaletteShown = name != "Move";
            colorToolbar.Visible = colorPaletteShown;
            Cursor = name == "Text" ? Cursors.IBeam : name == "Eyedropper" ? Cursors.Hand : name == "Move" ? Cursors.Default : Cursors.Cross;
            if (selectionLocked) PlaceToolbar();
        }

        void ClickTool(string name)
        {
            if (name == activeTool && name != "Move")
            {
                colorPaletteShown = !colorPaletteShown;
                colorToolbar.Visible = colorPaletteShown;
                if (selectionLocked) PlaceToolbar();
                return;
            }
            SetTool(name);
        }

        internal void SetColor(Color color)
        {
            currentColor = color;
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
            else if (marks.Count > 0) marks.RemoveAt(marks.Count - 1);
            Invalidate();
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
            if (aiBusy) return;
            if (!selectionLocked) return;
            if (Result != null) Result.Dispose();
            Result = BuildResult();
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
                using (var border = new Pen(Color.FromArgb(40, 120, 230), 1.5f))
                    e.Graphics.DrawRectangle(border, localSelection.X, localSelection.Y, localSelection.Width, localSelection.Height);
                string dimensions = Selection.Width + " × " + Selection.Height;
                using (var font = new Font("Segoe UI", 10, FontStyle.Bold))
                using (var background = new SolidBrush(Color.FromArgb(210, 24, 34, 49)))
                using (var foreground = new SolidBrush(Color.White))
                {
                    SizeF size = e.Graphics.MeasureString(dimensions, font);
                    float top = Math.Max(4, localSelection.Top - size.Height - 8);
                    e.Graphics.FillRectangle(background, localSelection.Left, top, size.Width + 12, size.Height + 4);
                    e.Graphics.DrawString(dimensions, font, foreground, localSelection.Left + 6, top + 2);
                }
                if (annotate && selectionLocked) DrawSelectionHandles(e.Graphics, localSelection);
            }
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
                RectangleF handle = new RectangleF(center.X - 5, center.Y - 5, 10, 10);
                using (var fill = new SolidBrush(Color.White)) graphics.FillEllipse(fill, handle);
                using (var border = new Pen(Color.FromArgb(37, 105, 204), 1.5f)) graphics.DrawEllipse(border, handle);
            }
            graphics.Restore(state);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                desktop.Dispose();
                if (selectionBase != null) selectionBase.Dispose();
                if (Result != null) Result.Dispose();
            }
            base.Dispose(disposing);
        }

        void BuildToolbar()
        {
            toolbar.Visible = false;
            toolbar.AutoSize = true;
            toolbar.Dock = DockStyle.None;
            toolbar.GripStyle = ToolStripGripStyle.Hidden;
            toolbar.Padding = new Padding(9, 7, 9, 7);
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
            AddAction("Cancel", "\u53d6\u6d88\u622a\u56fe", delegate { CancelSelection(); });
            AddAction("Confirm", "\u786e\u8ba4\u5e76\u6302\u5230\u56fe\u7ef3 Enter", delegate { ConfirmSelection(); });
            Controls.Add(toolbar);
            BuildColorToolbar();
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
            colorToolbar.Padding = new Padding(9, 6, 9, 6);
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
            var custom = new IconButton("CustomColor", "\u81ea\u5b9a\u4e49\u989c\u8272", "\u81ea\u5b9a\u4e49\u989c\u8272") { Size = new Size(38, 42) };
            custom.Click += ChooseCustomColor;
            colorToolbar.Items.Add(custom);
            Controls.Add(colorToolbar);
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

        void RunAiAction(string action)
        {
            if (!selectionLocked || aiBusy) return;
            if (aiAction == null)
            {
                MessageBox.Show(this, "\u8BF7\u5148\u5728\u8BBE\u7F6E\u7684 AI \u914D\u7F6E\u9875\u586B\u5199\u63A5\u53E3\u5730\u5740\u548C\u6A21\u578B\u540D\u79F0\u3002", "AI \u914D\u7F6E", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            aiBusy = true;
            try
            {
                using (Bitmap image = BuildResult())
                {
                    if (action == "Translate")
                    {
                        using (var dialog = new AiTranslationWindow((source, target) => aiAction(image, action, source, target)))
                            dialog.ShowDialog(this);
                    }
                    else if (action == "Explain")
                    {
                        using (var dialog = new AiExplanationWindow(() => aiAction(image, action, "\u81EA\u52A8\u68C0\u6D4B\u8BED\u8A00", "\u4E2D\u6587\u7B80\u4F53")))
                            dialog.ShowDialog(this);
                    }
                    else if (action == "ExtractText")
                    {
                        using (var dialog = new AiTextExtractionWindow(() => aiAction(image, action, null, null)))
                            dialog.ShowDialog(this);
                    }
                }
            }
            catch (Exception error) { if (!IsDisposed && IsHandleCreated) MessageBox.Show(this, error.Message, "AI \u64CD\u4F5C\u5931\u8D25", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { aiBusy = false; }
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
            toolbar.PerformLayout();
            colorToolbar.PerformLayout();
            Size preferred = toolbar.GetPreferredSize(Size.Empty);
            Size colorPreferred = colorToolbar.GetPreferredSize(Size.Empty);
            Rectangle selected = ToLocal(Selection);
            int left = selected.Left + (selected.Width - preferred.Width) / 2;
            left = Math.Max(4, Math.Min(ClientSize.Width - preferred.Width - 4, left));
            int below = selected.Bottom + 8;
            int above = selected.Top - preferred.Height - 8;
            int paletteGap = 6;
            int groupHeight = preferred.Height + (colorPaletteShown ? paletteGap + colorPreferred.Height : 0);
            bool placeBelow = below + groupHeight <= ClientSize.Height - 4;
            int toolbarTop;
            int colorTop;
            if (placeBelow)
            {
                toolbarTop = below;
                colorTop = toolbarTop + preferred.Height + paletteGap;
            }
            else if (colorPaletteShown)
            {
                int groupTop = above - paletteGap - colorPreferred.Height;
                groupTop = Math.Max(4, Math.Min(ClientSize.Height - groupHeight - 4, groupTop));
                colorTop = groupTop;
                toolbarTop = colorTop + colorPreferred.Height + paletteGap;
            }
            else
            {
                int maxTop = Math.Max(4, ClientSize.Height - preferred.Height - 4);
                toolbarTop = Math.Max(4, Math.Min(maxTop, above));
                colorTop = toolbarTop;
            }
            toolbar.Location = new Point(left, toolbarTop);
            if (colorPaletteShown)
            {
                int colorLeft = left + (preferred.Width - colorPreferred.Width) / 2;
                colorLeft = Math.Max(4, Math.Min(ClientSize.Width - colorPreferred.Width - 4, colorLeft));
                colorToolbar.Location = new Point(colorLeft, colorTop);
                colorToolbar.BringToFront();
            }
            toolbar.BringToFront();
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
            if (activeTool == "Number") { marks.Add(new Mark { Tool = activeTool, Start = point, Number = marks.Count(mark => mark.Tool == "Number") + 1, Color = currentColor }); Invalidate(); return; }
            drawing = true;
            drawingTool = activeTool;
            lastPenPoint = Point.Round(point);
            inProgress = new Mark { Tool = drawingTool, Start = point, End = point, Color = currentColor, Points = new List<PointF> { point } };
            Invalidate();
        }

        void PointerMove(object sender, MouseEventArgs args)
        {
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
            else if (drawing && inProgress != null)
            {
                PointF point = new PointF(args.X - ToLocal(Selection).Left, args.Y - ToLocal(Selection).Top);
                inProgress.End = point;
                if (drawingTool == "Pen" && lastPenPoint != Point.Round(point)) { inProgress.Points.Add(point); lastPenPoint = Point.Round(point); }
                Invalidate();
            }
            else if (selectionLocked) UpdatePointerCursor(args.Location);
        }

        void PointerUp(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left) return;
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
            else if (drawing && inProgress != null)
            {
                PointF point = new PointF(args.X - ToLocal(Selection).Left, args.Y - ToLocal(Selection).Top);
                inProgress.End = point;
                if (drawingTool == "Pen" && inProgress.Points.Count == 1) inProgress.Points.Add(point);
                if (Math.Abs(inProgress.End.X - inProgress.Start.X) > 1 || Math.Abs(inProgress.End.Y - inProgress.Start.Y) > 1) marks.Add(inProgress);
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
            if (args.Control && args.KeyCode == Keys.Z) { args.Handled = true; Undo(); return; }
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
            textEntry = new TextBox { BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(245, 255, 255, 255), ForeColor = currentColor, Font = new Font("Microsoft YaHei", Math.Max(16, Selection.Width * .027f)), Width = Math.Min(320, Selection.Width - (int)point.X), Height = 38, Location = new Point(origin.X + (int)point.X, origin.Y + (int)point.Y) };
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
            if (value.Length > 0) marks.Add(new Mark { Tool = "Text", Start = new PointF(location.X - selectionOrigin.X, location.Y - selectionOrigin.Y), Text = value, Color = currentColor });
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
            float lineWidth = Math.Max(2, Math.Min(Selection.Width, Selection.Height) * .006f);
            using (var pen = new Pen(mark.Color, lineWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(mark.Color))
            {
                switch (mark.Tool)
                {
                    case "Rect": graphics.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height); break;
                    case "Ellipse": graphics.DrawEllipse(pen, box); break;
                    case "Arrow":
                        graphics.DrawLine(pen, mark.Start, mark.End);
                        float angle = (float)Math.Atan2(mark.End.Y - mark.Start.Y, mark.End.X - mark.Start.X);
                        float head = Math.Max(12, lineWidth * 4);
                        PointF left = new PointF(mark.End.X - head * (float)Math.Cos(angle - Math.PI / 6), mark.End.Y - head * (float)Math.Sin(angle - Math.PI / 6));
                        PointF right = new PointF(mark.End.X - head * (float)Math.Cos(angle + Math.PI / 6), mark.End.Y - head * (float)Math.Sin(angle + Math.PI / 6));
                        graphics.DrawLine(pen, mark.End, left); graphics.DrawLine(pen, mark.End, right); break;
                    case "Pen":
                        if (mark.Points != null && mark.Points.Count > 1) graphics.DrawLines(pen, mark.Points.ToArray());
                        else graphics.DrawLine(pen, mark.Start, mark.End);
                        break;
                    case "Mosaic": DrawMosaic(graphics, box, source); break;
                    case "Highlight": using (var translucent = new SolidBrush(Color.FromArgb(88, mark.Color))) graphics.FillRectangle(translucent, box); break;
                    case "Text":
                        using (var font = new Font("Microsoft YaHei", Math.Max(16, Selection.Width * .027f))) graphics.DrawString(mark.Text, font, brush, mark.Start);
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

        static void DrawMosaic(Graphics graphics, RectangleF box, Bitmap source)
        {
            if (source == null || box.Width < 1 || box.Height < 1) return;
            Rectangle target = Rectangle.Intersect(Rectangle.Round(box), new Rectangle(Point.Empty, source.Size));
            if (target.Width < 1 || target.Height < 1) return;
            using (Bitmap crop = source.Clone(target, PixelFormat.Format32bppArgb))
            using (var small = new Bitmap(Math.Max(1, target.Width / 12), Math.Max(1, target.Height / 12), PixelFormat.Format32bppArgb))
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
