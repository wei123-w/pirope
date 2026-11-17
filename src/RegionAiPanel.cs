using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenLine
{
    // 依照 SnipLite TranslationPanel 移植为工具栏下方的单行附加菜单。
    internal sealed class RegionAiPanel : UserControl
    {
        static readonly Color Ink = Color.FromArgb(38, 45, 55);
        static readonly Color Muted = Color.FromArgb(111, 124, 143);
        static readonly Color Blue = Color.FromArgb(0, 145, 234);
        static readonly Color Border = Color.FromArgb(220, 226, 234);
        static readonly string[] Languages = { "自动检测语言", "中文简体", "中文繁体", "英语", "日语", "韩语", "法语", "德语", "西班牙语", "俄语", "葡萄牙语", "意大利语" };

        readonly Button source = new Button();
        readonly Button swap = new Button();
        readonly Button target = new Button();
        readonly Button copy = new Button();
        readonly Button retry = new Button();
        readonly Label status = new Label();
        readonly ToolTip tips = new ToolTip();
        string action;
        string resultText = string.Empty;
        string sourceLanguage = "自动检测语言";
        string targetLanguage = "中文简体";

        internal event EventHandler RetryRequested;
        internal string SourceLanguage { get { return sourceLanguage; } }
        internal string TargetLanguage { get { return targetLanguage; } }
        internal string ResultText { get { return resultText; } }

        internal RegionAiPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.White;
            ForeColor = Ink;
            Font = new Font("Microsoft YaHei UI", 11f);
            Size = new Size(680, 44);
            MinimumSize = new Size(420, 44);

            ConfigureButton(source, true);
            source.Text = sourceLanguage;
            source.TextAlign = ContentAlignment.MiddleLeft;
            source.Padding = new Padding(8, 0, 20, 0);
            source.Paint += delegate(object sender, PaintEventArgs args) { DrawChevron(args.Graphics, source.ClientRectangle); };
            source.Click += delegate { ShowLanguages(source, true); };
            Controls.Add(source);

            ConfigureButton(swap, false);
            swap.Text = "⇄";
            swap.Font = new Font("Segoe UI Symbol", 16f);
            swap.Click += delegate
            {
                string oldSource = sourceLanguage;
                sourceLanguage = targetLanguage;
                targetLanguage = oldSource == "自动检测语言" ? "英语" : oldSource;
                source.Text = sourceLanguage;
                target.Text = targetLanguage;
                if (RetryRequested != null) RetryRequested(this, EventArgs.Empty);
            };
            tips.SetToolTip(swap, "交换原文语言和目标语言");
            Controls.Add(swap);

            ConfigureButton(target, true);
            target.Text = targetLanguage;
            target.TextAlign = ContentAlignment.MiddleLeft;
            target.Padding = new Padding(8, 0, 20, 0);
            target.Paint += delegate(object sender, PaintEventArgs args) { DrawChevron(args.Graphics, target.ClientRectangle); };
            target.Click += delegate { ShowLanguages(target, false); };
            Controls.Add(target);

            status.AutoEllipsis = true;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Muted;
            status.Font = new Font("Microsoft YaHei UI", 11f);
            Controls.Add(status);

            ConfigureButton(copy, false);
            copy.Enabled = false;
            copy.Text = string.Empty;
            copy.Paint += delegate(object sender, PaintEventArgs args) { DrawCopyIcon(args.Graphics, copy.ClientRectangle, copy.Enabled); };
            copy.Click += delegate
            {
                if (string.IsNullOrEmpty(resultText)) return;
                try { Clipboard.SetText(resultText); status.Text = "结果已复制"; }
                catch { status.Text = "复制失败，请重试"; }
            };
            tips.SetToolTip(copy, "复制完整结果");
            Controls.Add(copy);

            ConfigureButton(retry, false);
            retry.Enabled = false;
            retry.Text = string.Empty;
            retry.Paint += delegate(object sender, PaintEventArgs args) { DrawRetryIcon(args.Graphics, retry.ClientRectangle, retry.Enabled); };
            retry.Click += delegate { if (RetryRequested != null) RetryRequested(this, EventArgs.Empty); };
            tips.SetToolTip(retry, "重新执行");
            Controls.Add(retry);
            SizeChanged += delegate { LayoutControls(); };
            LayoutControls();
        }

        static void ConfigureButton(Button button, bool dropdown)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = dropdown ? 1 : 0;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 244, 250);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(230, 237, 246);
            button.BackColor = Color.White;
            button.ForeColor = Ink;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            button.Margin = Padding.Empty;
        }

        void LayoutControls()
        {
            if (source == null || target == null || status == null) return;
            int x = 5;
            int y = 5;
            int height = Math.Max(32, Height - 10);
            int width = Math.Max(1, Width);
            bool translating = action == "Translate";
            source.Visible = swap.Visible = target.Visible = translating;
            if (translating)
            {
                int pickerWidth = Math.Min(120, Math.Max(110, (width - 280) / 3));
                source.SetBounds(x, y, pickerWidth, height); x = source.Right + 4;
                swap.SetBounds(x, y, 34, height); x = swap.Right + 4;
                target.SetBounds(x, y, pickerWidth, height); x = target.Right + 10;
            }
            int actionWidth = 34;
            copy.SetBounds(width - actionWidth - 5, y, actionWidth, height);
            retry.SetBounds(copy.Left - actionWidth - 4, y, actionWidth, height);
            status.SetBounds(x, y, Math.Max(1, retry.Left - x - 8), height);
        }

        void ShowLanguages(Button owner, bool isSource)
        {
            var menu = new ContextMenuStrip { Font = new Font("Microsoft YaHei UI", 10f) };
            foreach (string language in Languages)
            {
                if (!isSource && language == "自动检测语言") continue;
                string value = language;
                var item = new ToolStripMenuItem(value) { Checked = (isSource ? sourceLanguage : targetLanguage) == value, CheckOnClick = false };
                item.Click += delegate
                {
                    if (isSource) sourceLanguage = value; else targetLanguage = value;
                    owner.Text = value;
                    if (RetryRequested != null) RetryRequested(this, EventArgs.Empty);
                };
                menu.Items.Add(item);
            }
            menu.Show(owner, new Point(0, owner.Height));
        }

        static void DrawCopyIcon(Graphics graphics, Rectangle bounds, bool enabled)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = enabled ? Ink : Color.FromArgb(175, 181, 190);
            using (var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float x = bounds.Width / 2f;
                float y = bounds.Height / 2f;
                graphics.DrawRectangle(pen, x - 7, y - 7, 10, 10);
                graphics.DrawRectangle(pen, x - 2, y - 2, 10, 10);
            }
        }

        static void DrawRetryIcon(Graphics graphics, Rectangle bounds, bool enabled)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = enabled ? Ink : Color.FromArgb(175, 181, 190);
            float x = bounds.Width / 2f;
            float y = bounds.Height / 2f;
            float radius = 7.5f;
            using (var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                graphics.DrawArc(pen, x - radius, y - radius, radius * 2, radius * 2, 30, 285);
                graphics.DrawLines(pen, new[] { new PointF(x + 2, y - 8), new PointF(x + 8, y - 8), new PointF(x + 8, y - 2) });
            }
        }

        static void DrawChevron(Graphics graphics, Rectangle bounds)
        {
            float x = bounds.Right - 12;
            float y = bounds.Top + bounds.Height / 2f;
            using (var pen = new Pen(Ink, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                graphics.DrawLines(pen, new[] { new PointF(x - 3, y - 1), new PointF(x, y + 2), new PointF(x + 3, y - 1) });
        }

        internal void Begin(string actionName)
        {
            action = actionName;
            resultText = string.Empty;
            copy.Enabled = false;
            retry.Enabled = false;
            status.ForeColor = Muted;
            status.Text = actionName == "Translate" ? "正在翻译…" : actionName == "Explain" ? "正在分析截图…" : "正在提取文字…";
            tips.SetToolTip(status, status.Text);
            LayoutControls();
            BringToFront();
            Invalidate();
        }

        internal void SetResult(string text)
        {
            resultText = text ?? string.Empty;
            status.ForeColor = Ink;
            status.Text = string.IsNullOrWhiteSpace(resultText) ? "没有返回结果" : resultText.Replace("\r", " ").Replace("\n", "  ");
            tips.SetToolTip(status, resultText);
            copy.Enabled = resultText.Length > 0;
            retry.Enabled = true;
        }

        internal void SetFailure(string message)
        {
            resultText = string.Empty;
            status.ForeColor = Color.FromArgb(178, 48, 42);
            status.Text = "请求失败：" + (message ?? "未知错误");
            tips.SetToolTip(status, status.Text);
            copy.Enabled = false;
            retry.Enabled = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Border))
            using (GraphicsPath path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 8))
                e.Graphics.DrawPath(pen, path);
            if (action == "Translate")
            {
                using (var divider = new Pen(Border)) e.Graphics.DrawLine(divider, target.Right + 5, 10, target.Right + 5, Height - 10);
            }
        }

        static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
