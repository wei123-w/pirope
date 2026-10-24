using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenLine
{
    internal abstract class AiResultWindow : Form
    {
        static readonly Color Surface = Color.FromArgb(246, 248, 251);
        static readonly Color Ink = Color.FromArgb(28, 39, 54);
        static readonly Color Muted = Color.FromArgb(99, 114, 133);
        static readonly Color Blue = Color.FromArgb(43, 105, 195);
        static readonly Color Line = Color.FromArgb(224, 231, 240);
        static readonly Color Green = Color.FromArgb(35, 139, 91);
        static readonly Color Red = Color.FromArgb(190, 64, 64);
        readonly Timer entrance = new Timer { Interval = 15 };
        readonly Timer activity = new Timer { Interval = 380 };
        readonly ToolTip details = new ToolTip();
        readonly Label status = new Label();
        readonly RichTextBox output = new RichTextBox();
        readonly Button copyButton;
        readonly Button retryButton;
        Point finalLocation;
        int entranceStep;
        int activityStep;
        string workingStatus;
        bool requestBusy;

        protected AiResultWindow(string titleText, string subtitleText, string retryText, string workingText)
        {
            Text = titleText;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ShowIcon = false;
            DoubleBuffered = true;
            BackColor = Surface;
            ForeColor = Ink;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            ClientSize = new Size(760, 540);
            MinimumSize = new Size(560, 440);
            workingStatus = workingText;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(26, 22, 26, 20), ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            Controls.Add(root);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Surface, ColumnCount = 2, RowCount = 2, Margin = new Padding(0), Padding = new Padding(0, 0, 0, 8) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var title = new Label { Text = titleText, Font = new Font("Microsoft YaHei UI", 19, FontStyle.Bold), ForeColor = Ink, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 1) };
            var subtitle = new Label { Text = subtitleText, Font = new Font("Microsoft YaHei UI", 9.5f), ForeColor = Muted, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(2, 0, 8, 0) };
            var close = CreateButton("×", false); close.Size = new Size(40, 38); close.Anchor = AnchorStyles.Top | AnchorStyles.Right; close.Font = new Font("Segoe UI Symbol", 17); close.AccessibleName = titleText; close.Click += delegate { Close(); };
            header.Controls.Add(title, 0, 0); header.Controls.Add(subtitle, 0, 1); header.Controls.Add(close, 1, 0); header.SetRowSpan(close, 2);
            root.Controls.Add(header, 0, 0);

            var contentFrame = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(17), Margin = new Padding(0, 7, 0, 0) };
            output.Dock = DockStyle.Fill;
            output.Multiline = true;
            output.ReadOnly = true;
            output.DetectUrls = false;
            output.ScrollBars = RichTextBoxScrollBars.Vertical;
            output.WordWrap = true;
            output.BorderStyle = BorderStyle.None;
            output.BackColor = Color.White;
            output.ForeColor = Ink;
            output.Font = new Font("Microsoft YaHei UI", 11.5f);
            output.SelectionIndent = 2;
            output.SelectionRightIndent = 8;
            output.Text = workingText;
            contentFrame.Controls.Add(output);
            root.Controls.Add(contentFrame, 0, 2);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 12, 0, 0), BackColor = Surface };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            status.Text = workingText; status.ForeColor = Muted; status.AutoEllipsis = true; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.Padding = new Padding(1, 0, 12, 0);
            retryButton = CreateButton(retryText, true); retryButton.Size = new Size(116, 40); retryButton.Click += async delegate { await RetryAsync(); };
            copyButton = CreateButton("复制内容", false); copyButton.Size = new Size(104, 40); copyButton.Margin = new Padding(0, 0, 9, 0); copyButton.Enabled = false;
            copyButton.Click += delegate
            {
                try { Clipboard.SetText(output.Text); SetStatus("内容已复制到剪贴板", Green); }
                catch (Exception error) { SetStatus("复制失败：" + error.Message, Red); details.SetToolTip(status, error.Message); }
            };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, BackColor = Surface };
            buttons.Controls.Add(retryButton); buttons.Controls.Add(copyButton);
            footer.Controls.Add(status, 0, 0); footer.Controls.Add(buttons, 1, 0);
            root.Controls.Add(footer, 0, 3);

            entrance.Tick += EntranceTick;
            activity.Tick += ActivityTick;
            SizeChanged += UpdateShape;
        }

        protected void AddTopContent(Control content)
        {
            var root = Controls[0] as TableLayoutPanel;
            if (root == null) return;
            root.Controls.Add(content, 0, 1);
        }

        protected void StartWhenShown(Func<Task<string>> request)
        {
            Shown += async delegate
            {
                finalLocation = Location;
                Location = new Point(finalLocation.X, finalLocation.Y + 14);
                Opacity = 0;
                entrance.Start();
                await RunRequestAsync(request);
            };
        }

        protected void SetStatus(string text, Color color)
        {
            status.Text = text;
            status.ForeColor = color;
        }

        protected static Color PrimaryColor { get { return Blue; } }
        protected static Color PrimaryInk { get { return Ink; } }
        protected static Color SecondaryInk { get { return Muted; } }
        protected static Color BorderColor { get { return Line; } }

        static Button CreateButton(string text, bool primary)
        {
            Color baseColor = primary ? Blue : Color.White;
            var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = baseColor, ForeColor = primary ? Color.White : Ink, Font = new Font("Microsoft YaHei UI", 9.5f, primary ? FontStyle.Bold : FontStyle.Regular), Cursor = Cursors.Hand, UseVisualStyleBackColor = false, TabStop = true };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Line;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(56, 119, 207) : Color.FromArgb(239, 244, 250);
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(34, 88, 165) : Color.FromArgb(229, 236, 246);
            return button;
        }

        async Task RetryAsync()
        {
            Func<Task<string>> request = Request;
            await RunRequestAsync(request);
        }

        protected abstract Task<string> Request();

        async Task RunRequestAsync(Func<Task<string>> request)
        {
            if (requestBusy || request == null || IsDisposed) return;
            requestBusy = true;
            retryButton.Enabled = false;
            copyButton.Enabled = false;
            output.Text = workingStatus;
            SetStatus(workingStatus, Muted);
            details.SetToolTip(status, "");
            activityStep = 0;
            activity.Start();
            try
            {
                string value = await request();
                if (IsDisposed) return;
                output.Text = value ?? string.Empty;
                copyButton.Enabled = output.TextLength > 0;
                SetStatus("已完成", Green);
            }
            catch (Exception error)
            {
                if (IsDisposed) return;
                output.Text = "这次请求没有完成。请根据下方提示检查配置或网络，然后重试。";
                SetStatus("请求失败：" + error.Message, Red);
                details.SetToolTip(status, error.Message);
            }
            finally
            {
                requestBusy = false;
                if (!IsDisposed) { activity.Stop(); retryButton.Enabled = true; }
            }
        }

        void EntranceTick(object sender, EventArgs args)
        {
            entranceStep++;
            double progress = Math.Min(1.0, entranceStep / 15.0);
            double eased = 1 - Math.Pow(1 - progress, 3);
            Opacity = eased;
            Location = new Point(finalLocation.X, finalLocation.Y + (int)Math.Round(14 * (1 - eased)));
            if (progress >= 1) { entrance.Stop(); Location = finalLocation; }
        }

        void ActivityTick(object sender, EventArgs args)
        {
            activityStep = (activityStep + 1) % 4;
            SetStatus(workingStatus + new string('·', activityStep), Muted);
        }

        void UpdateShape(object sender, EventArgs args)
        {
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            using (GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 18))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
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

        protected override CreateParams CreateParams
        {
            get { CreateParams parameters = base.CreateParams; parameters.ClassStyle |= 0x00020000; return parameters; }
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Line))
            using (GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), 18))
                args.Graphics.DrawPath(pen, path);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { entrance.Dispose(); activity.Dispose(); details.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class AiTranslationWindow : AiResultWindow
    {
        static readonly string[] Languages = { "自动检测语言", "中文简体", "中文繁体", "英语", "日语", "韩语", "法语", "德语", "西班牙语", "俄语", "葡萄牙语", "意大利语" };
        readonly Func<string, string, Task<string>> translate;
        readonly ComboBox source = LanguagePicker(true);
        readonly ComboBox target = LanguagePicker(false);
        readonly Button swap;

        internal string SelectedSourceLanguage { get { return Convert.ToString(source.SelectedItem); } }
        internal string SelectedTargetLanguage { get { return Convert.ToString(target.SelectedItem); } }

        internal AiTranslationWindow(Func<string, string, Task<string>> translate)
            : base("翻译结果", "识别截图文字并翻译，可调整语言后重新生成", "重新翻译", "正在翻译截图内容")
        {
            if (translate == null) throw new ArgumentNullException("translate");
            this.translate = translate;
            source.SelectedIndex = 0;
            target.SelectedItem = "中文简体";
            var languageGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.White, Padding = new Padding(14, 11, 14, 11), Margin = new Padding(0, 0, 0, 0) };
            languageGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            languageGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            languageGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            languageGrid.Controls.Add(LanguagePanel("原文语言", source), 0, 0);
            swap = new Button { Text = "⇄", Dock = DockStyle.Fill, Margin = new Padding(7, 18, 7, 0), Font = new Font("Segoe UI Symbol", 17), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(243, 247, 252), ForeColor = PrimaryColor, UseVisualStyleBackColor = false, AccessibleName = "交换原文语言和目标语言" };
            swap.FlatAppearance.BorderSize = 0;
            swap.FlatAppearance.MouseOverBackColor = Color.FromArgb(229, 238, 250);
            swap.Enabled = false;
            swap.Click += delegate
            {
                if (source.SelectedIndex <= 0) return;
                object oldSource = source.SelectedItem;
                source.SelectedItem = target.SelectedItem;
                target.SelectedItem = oldSource;
            };
            source.SelectedIndexChanged += delegate { swap.Enabled = source.SelectedIndex > 0; };
            languageGrid.Controls.Add(swap, 1, 0);
            languageGrid.Controls.Add(LanguagePanel("翻译为", target), 2, 0);
            AddTopContent(languageGrid);
            source.SelectedIndexChanged += LanguageChanged;
            target.SelectedIndexChanged += LanguageChanged;
            StartWhenShown(() => this.translate(SelectedSourceLanguage, SelectedTargetLanguage));
        }

        protected override Task<string> Request() { return translate(SelectedSourceLanguage, SelectedTargetLanguage); }

        void LanguageChanged(object sender, EventArgs args)
        {
            SetStatus("语言已调整，点击“重新翻译”以应用", SecondaryInk);
        }

        static Panel LanguagePanel(string caption, ComboBox picker)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(0, 0, 0, 0) };
            var label = new Label { Text = caption, ForeColor = SecondaryInk, Font = new Font("Microsoft YaHei UI", 9f), Dock = DockStyle.Top, Height = 23, TextAlign = ContentAlignment.MiddleLeft };
            picker.Dock = DockStyle.Fill;
            panel.Controls.Add(picker); panel.Controls.Add(label);
            return panel;
        }

        static ComboBox LanguagePicker(bool includeAuto)
        {
            var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, DrawMode = DrawMode.OwnerDrawFixed, BackColor = Color.White, ForeColor = PrimaryInk, Font = new Font("Microsoft YaHei UI", 10f), IntegralHeight = true, DropDownHeight = 250, ItemHeight = 30, Margin = new Padding(0, 3, 0, 0) };
            foreach (string language in Languages) if (includeAuto || language != "自动检测语言") picker.Items.Add(language);
            picker.DrawItem += delegate(object sender, DrawItemEventArgs args)
            {
                if (args.Index < 0) return;
                bool selected = (args.State & DrawItemState.Selected) == DrawItemState.Selected;
                Color back = selected ? Color.FromArgb(232, 241, 253) : Color.White;
                using (var brush = new SolidBrush(back)) args.Graphics.FillRectangle(brush, args.Bounds);
                TextRenderer.DrawText(args.Graphics, Convert.ToString(picker.Items[args.Index]), args.Font, Rectangle.Inflate(args.Bounds, -10, 0), PrimaryInk, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                args.DrawFocusRectangle();
            };
            return picker;
        }
    }

    internal sealed class AiTextExtractionWindow : AiResultWindow
    {
        readonly Func<Task<string>> extract;

        internal AiTextExtractionWindow(Func<Task<string>> extract)
            : base("\u6587\u5B57\u63D0\u53D6", "\u63D0\u53D6\u6846\u9009\u533A\u57DF\u4E2D\u7684\u6587\u5B57\uFF0C\u4FDD\u7559\u6BB5\u843D\u7ED3\u6784", "\u91CD\u65B0\u63D0\u53D6", "\u6B63\u5728\u63D0\u53D6\u622A\u56FE\u6587\u5B57")
        {
            if (extract == null) throw new ArgumentNullException("extract");
            this.extract = extract;
            var context = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(15, 12, 15, 12) };
            var marker = new Label { Text = "OCR", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Color.White, BackColor = PrimaryColor, Size = new Size(42, 34), Location = new Point(15, 14) };
            var heading = new Label { Text = "\u6587\u5B57\u8BC6\u522B", AutoSize = true, Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold), ForeColor = PrimaryInk, Location = new Point(68, 10) };
            var hint = new Label { Text = "\u4EC5\u8BC6\u522B\u5F53\u524D\u6846\u9009\u533A\u57DF\u4E2D\u53EF\u89C1\u7684\u6587\u5B57\uFF0C\u4FDD\u7559\u539F\u6709\u6BB5\u843D\u4E0E\u6362\u884C", AutoSize = true, Font = new Font("Microsoft YaHei UI", 9), ForeColor = SecondaryInk, Location = new Point(68, 34) };
            context.Controls.Add(marker); context.Controls.Add(heading); context.Controls.Add(hint);
            context.Resize += delegate { hint.MaximumSize = new Size(Math.Max(100, context.ClientSize.Width - 85), 0); };
            AddTopContent(context);
            StartWhenShown(() => this.extract());
        }

        protected override Task<string> Request() { return extract(); }
    }
    internal sealed class AiExplanationWindow : AiResultWindow
    {
        readonly Func<Task<string>> explain;

        internal AiExplanationWindow(Func<Task<string>> explain)
            : base("截图解释", "分析图片内容与可见文字，整理重点信息", "重新解释", "正在分析截图内容")
        {
            if (explain == null) throw new ArgumentNullException("explain");
            this.explain = explain;
            var context = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(15, 12, 15, 12) };
            var marker = new Label { Text = "AI", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.FromArgb(43, 105, 195), Size = new Size(38, 34), Location = new Point(15, 14) };
            var heading = new Label { Text = "本次解释", AutoSize = true, Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold), ForeColor = PrimaryInk, Location = new Point(65, 10) };
            var hint = new Label { Text = "仅分析当前框选区域；你可以复制结果，或重新请求一次。", AutoSize = true, Font = new Font("Microsoft YaHei UI", 9), ForeColor = SecondaryInk, Location = new Point(65, 34) };
            context.Controls.Add(marker); context.Controls.Add(heading); context.Controls.Add(hint);
            context.Resize += delegate { hint.MaximumSize = new Size(Math.Max(100, context.ClientSize.Width - 82), 0); };
            AddTopContent(context);
            StartWhenShown(() => this.explain());
        }

        protected override Task<string> Request() { return explain(); }
    }
}
