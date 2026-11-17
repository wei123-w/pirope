using System.Drawing.Drawing2D;

namespace SnipLite;

/// <summary>A single options row below the unchanged annotation toolbar.</summary>
internal sealed class TranslationPanel : UserControl
{
    internal static Color Surface => AppTheme.Current.Card;
    private static Color Ink => AppTheme.Current.Text;
    private readonly Color _accent;
    private readonly Button _providerButton;
    private readonly Button _source;
    private readonly Button _target;
    private readonly Button _swap;
    private readonly Button _view;
    private readonly Button _copy;
    private readonly Button _retry;
    private readonly Label _status;
    private readonly List<ContextMenuStrip> _menus = new();
    private readonly float _scale;
    private readonly Font _contentFont;
    private readonly Font _smallFont;
    private readonly ToolTip _tips = new();
    private string _originalText = "";
    private Func<string>? _statusText;

    public string SourceLanguage { get; private set; }
    public string TargetLanguage { get; private set; }
    public string? DetectedLanguage { get; private set; }
    public string ResultText { get; private set; } = "";
    public bool ShowOriginal { get; private set; }
    public TranslationProviderKind Provider { get; private set; }
    private bool AllowAutoSource => Provider != TranslationProviderKind.MyMemory;
    public event Action<bool>? LanguagesChanged;
    public event Action<TranslationProviderKind>? ProviderRequested;
    public event Action? ViewChanged;
    public event Action? RetryRequested;
    public event Action<ContextMenuStrip>? MenuOpening;
    private int Px(int value) => (int)Math.Round(value * _scale);

    public TranslationPanel(float scale, string source, string target, Color? accent = null, TranslationProviderKind provider = TranslationProviderKind.Google)
    {
        _scale = scale; _accent = accent ?? AnnotationBar.DefaultAccent;
        AutoScaleMode = AutoScaleMode.None; BackColor = Surface; ForeColor = Ink;
        Cursor = Cursors.Default; DoubleBuffered = true;
        _contentFont = new Font("Microsoft YaHei UI", 12 * scale, GraphicsUnit.Pixel);
        _smallFont = new Font("Microsoft YaHei UI", 11 * scale, GraphicsUnit.Pixel);
        Font = _contentFont;
        SourceLanguage = TranslationLanguages.Normalize(source, true, "en");
        TargetLanguage = TranslationLanguages.Normalize(target, false, "zh-CN");
        _source = MakeDropdown("源语言", () => ShowLanguages(true));
        _target = MakeDropdown("目标语言", () => ShowLanguages(false));
        _swap = MakeIconButton("交换源语言和目标语言", SwapLanguages, DrawSwapIcon);
        _tips.SetToolTip(_swap, UiText.T("交换源语言和目标语言"));
        _view = MakeIconButton("切换原文 / 译文", ToggleView, DrawViewIcon);
        _copy = MakeIconButton("复制识别文字", CopyText, DrawCopyIcon);
        _retry = MakeIconButton("重新翻译", () => RetryRequested?.Invoke(), DrawRetryIcon);
        _tips.SetToolTip(_retry, UiText.T("重新翻译"));
        _providerButton = MakeDropdown("翻译服务", ShowProviders);
        _tips.SetToolTip(_providerButton, UiText.T("切换本次截图的翻译服务"));
        _status = new Label { ForeColor = AppTheme.Current.Muted, Font = _smallFont, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        Controls.Add(_status);
        Width = Px(680); Height = Px(44);
        SetProvider(provider); SetBusy(() => UiText.T("正在识别…"));
        AppTheme.Changed += ApplyTheme;
        Localization.LanguageService.Changed += RefreshLanguage;
    }

    private void RefreshLanguage()
    {
        if (IsDisposed) return;
        UpdateLanguages();
        _providerButton.Text = TranslationProviders.Label(Provider);
        foreach (var button in Controls.OfType<Button>())
            if (button.Tag is string source) button.AccessibleName = UiText.T(source);
        _tips.SetToolTip(_swap, UiText.T("交换源语言和目标语言"));
        _tips.SetToolTip(_providerButton, UiText.T("切换本次截图的翻译服务"));
        _tips.SetToolTip(_retry, UiText.T("重新翻译"));
        if (_statusText is not null) { _status.Text = _statusText(); _tips.SetToolTip(_status, _status.Text); }
        RefreshView();
    }

    private void ApplyTheme()
    {
        if (IsDisposed) return;
        BackColor = Surface; ForeColor = Ink;
        foreach (var button in Controls.OfType<Button>())
        {
            button.BackColor = Surface; button.ForeColor = Ink;
            button.FlatAppearance.BorderColor = AppTheme.Current.Border;
            button.FlatAppearance.MouseOverBackColor = AppTheme.Current.Hover;
            button.FlatAppearance.MouseDownBackColor = AppTheme.Current.AccentSoft;
            button.Invalidate();
        }
        _status.ForeColor = AppTheme.Current.Muted;
        RefreshView(); Invalidate();
    }

    private Button MakeButton(Action click)
    {
        var button = new Button
        {
            FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Ink,
            Cursor = Cursors.Hand, Font = _contentFont, UseVisualStyleBackColor = false,
            TextAlign = ContentAlignment.MiddleCenter, Padding = Padding.Empty, Margin = Padding.Empty
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = SnipMenuRenderer.Hover;
        button.FlatAppearance.MouseDownBackColor = AppTheme.Current.AccentSoft;
        button.Click += (_, _) => click(); Controls.Add(button); return button;
    }

    private Button MakeIconButton(string label, Action click, Action<Graphics, Rectangle, bool> draw)
    {
        var button = MakeButton(click);
        button.Tag = label;
        button.AccessibleName = UiText.T(label);
        button.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            draw(e.Graphics, button.ClientRectangle, button.Enabled);
        };
        return button;
    }

    private Button MakeDropdown(string label, Action click)
    {
        var button = MakeButton(click);
        button.Tag = label;
        button.AccessibleName = UiText.T(label);
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = new Padding(Px(8), 0, Px(24), 0);
        button.AutoEllipsis = true;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = AppTheme.Current.Border;
        button.Paint += (_, e) =>
        {
            var x = button.ClientSize.Width - Px(13);
            var y = button.ClientSize.Height / 2;
            var arm = Px(4);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Ink, 1.4f * _scale)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            e.Graphics.DrawLines(pen, new[] { new Point(x - arm, y - Px(2)),
                new Point(x, y + Px(2)), new Point(x + arm, y - Px(2)) });
        };
        return button;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (_status is null) return;
        var x = Px(5); var y = Px(5); var button = Px(34); var gap = Px(2);
        var dropdownWidth = Math.Clamp((Width - x * 2 - button * 4 - gap * 4 - Px(26) - Px(84)) / 3, 1, Px(120));
        _source.SetBounds(x, y, dropdownWidth, button); x = _source.Right + gap;
        _swap.SetBounds(x, y, button, button); x = _swap.Right + gap;
        _target.SetBounds(x, y, dropdownWidth, button); x = _target.Right + Px(8);
        _view.SetBounds(x, y, button, button); x = _view.Right + Px(12);
        _copy.SetBounds(Width - Px(5) - button, y, button, button);
        _retry.SetBounds(_copy.Left - gap - button, y, button, button);
        _providerButton.SetBounds(x, y, dropdownWidth, button);
        x = _providerButton.Right + gap;
        _status.SetBounds(x, y, Math.Max(1, _retry.Left - x - Px(6)), button);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(AppTheme.Current.Border);
        using var path = FluentDrawing.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), Px(8));
        e.Graphics.DrawPath(border, path);
        using var divider = new Pen(AppTheme.Current.Border);
        e.Graphics.DrawLine(divider, _view.Right + Px(5), Px(10), _view.Right + Px(5), Height - Px(10));
    }

    private void DrawViewIcon(Graphics g, Rectangle rect, bool enabled)
    {
        var x = rect.Width / 2f; var y = rect.Height / 2f;
        using var pen = MakeIconPen(enabled, ShowOriginal ? _accent : Ink);
        using var path = new GraphicsPath();
        path.AddBezier(x - Px(9), y, x - Px(4), y - Px(9), x + Px(4), y - Px(9), x + Px(9), y);
        path.AddBezier(x + Px(9), y, x + Px(4), y + Px(9), x - Px(4), y + Px(9), x - Px(9), y);
        g.DrawPath(pen, path); g.DrawEllipse(pen, x - Px(2), y - Px(2), Px(4), Px(4));
    }

    private void DrawCopyIcon(Graphics g, Rectangle rect, bool enabled)
    {
        using var pen = MakeIconPen(enabled, Ink);
        var x = rect.Width / 2; var y = rect.Height / 2;
        g.DrawRectangle(pen, x - Px(7), y - Px(7), Px(10), Px(10));
        g.DrawRectangle(pen, x - Px(2), y - Px(2), Px(10), Px(10));
    }

    private void DrawSwapIcon(Graphics g, Rectangle rect, bool enabled)
    {
        var x = rect.Width / 2f;
        var y = rect.Height / 2f;
        var left = x - 8 * _scale;
        var right = x + 8 * _scale;
        var upper = y - 4 * _scale;
        var lower = y + 4 * _scale;
        var arm = 3.5f * _scale;
        using var pen = MakeIconPen(enabled, Ink);
        g.DrawLine(pen, left, upper, right, upper);
        g.DrawLines(pen, new[] { new PointF(right - arm, upper - arm), new PointF(right, upper), new PointF(right - arm, upper + arm) });
        g.DrawLine(pen, right, lower, left, lower);
        g.DrawLines(pen, new[] { new PointF(left + arm, lower - arm), new PointF(left, lower), new PointF(left + arm, lower + arm) });
    }

    private void DrawRetryIcon(Graphics g, Rectangle rect, bool enabled)
    {
        var x = rect.Width / 2f;
        var y = rect.Height / 2f + .5f * _scale;
        var radius = 7.5f * _scale;
        var tip = new PointF(x + radius / MathF.Sqrt(2), y - radius / MathF.Sqrt(2));
        var arm = 4 * _scale;
        using var pen = MakeIconPen(enabled, Ink);
        g.DrawArc(pen, x - radius, y - radius, radius * 2, radius * 2, 30, 285);
        g.DrawLines(pen, new[] { new PointF(tip.X - arm, tip.Y), tip, new PointF(tip.X, tip.Y - arm) });
    }

    private Pen MakeIconPen(bool enabled, Color color)
        => new(enabled ? AppTheme.AdaptAccent(color) : AppTheme.Current.Disabled, 1.5f * _scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

    internal void SelectLanguage(bool source, string code)
    {
        code = TranslationLanguages.Normalize(code, source && AllowAutoSource, source ? "en" : "zh-CN");
        if (source ? SourceLanguage == code : TargetLanguage == code) return;
        if (source) SourceLanguage = code; else TargetLanguage = code;
        DetectedLanguage = null; UpdateLanguages(); LanguagesChanged?.Invoke(false);
    }

    internal void SwapLanguages()
    {
        var source = SourceLanguage == "auto" ? TranslationLanguages.Normalize(DetectedLanguage, false, "en") : SourceLanguage;
        (SourceLanguage, TargetLanguage) = (TargetLanguage, source);
        DetectedLanguage = null; UpdateLanguages(); LanguagesChanged?.Invoke(true);
    }

    private void UpdateLanguages()
    {
        var sourceLabel = TranslationLanguages.Name(SourceLanguage);
        if (SourceLanguage == "auto")
        {
            if (!AllowAutoSource) sourceLabel = UiText.T("选择源语言");
            else if (DetectedLanguage is not null) sourceLabel = UiText.T("检测到") + TranslationLanguages.Name(DetectedLanguage);
        }
        _source.Text = sourceLabel;
        _target.Text = TranslationLanguages.Name(TargetLanguage);
        _tips.SetToolTip(_source, _source.Text);
        _tips.SetToolTip(_target, _target.Text);
    }

    internal void ShowLanguages(bool source)
    {
        var menu = SnipMenu.Create();
        foreach (var language in TranslationLanguages.All.Where(l => (source && AllowAutoSource) || l.Code != "auto"))
        {
            menu.Items.Add(SnipMenu.Item(language.Name, () => SelectLanguage(source, language.Code),
                check: language.Code == (source ? SourceLanguage : TargetLanguage)));
        }
        OpenDropdown(source ? _source : _target, menu);
    }

    public void SetProvider(TranslationProviderKind provider)
    {
        Provider = provider;
        _providerButton.Text = TranslationProviders.Label(provider);
        DetectedLanguage = null;
        UpdateLanguages();
    }

    private void ShowProviders()
    {
        var menu = SnipMenu.Create();
        foreach (var (kind, label) in TranslationProviders.Choices)
            menu.Items.Add(SnipMenu.Item(label, () => ProviderRequested?.Invoke(kind), check: Provider == kind));
        OpenDropdown(_providerButton, menu);
    }

    private void OpenDropdown(Button owner, ContextMenuStrip menu)
    {
        var width = Px(168);
        menu.Font = _contentFont;
        menu.MinimumSize = new Size(width, 0);
        menu.MaximumSize = new Size(width, Px(284));
        foreach (ToolStripItem item in menu.Items)
        {
            item.AutoSize = false;
            item.Size = new Size(width - menu.Padding.Horizontal, Px(32));
            item.Padding = Padding.Empty;
        }
        _menus.Add(menu);
        MenuOpening?.Invoke(menu);
        menu.Show(owner, new Point(0, owner.Height + Px(3)));
    }

    public void SetOriginal(string text) => _originalText = text;
    public void SetBusy(string status)
    {
        ResultText = ""; ShowOriginal = false;
        _view.Enabled = _copy.Enabled = _retry.Enabled = false;
        _status.ForeColor = AppTheme.Current.Muted; SetStatus(status); RefreshView();
    }
    public void SetBusy(Func<string> status) { SetBusy(status()); _statusText = status; }
    public void SetProgress(int completed, int total) => SetStatus(() => Localization.LanguageService.Format("翻译中 {0}/{1}", completed, total));
    public void SetResult(TranslationResult result)
    {
        ResultText = result.Text; DetectedLanguage = result.DetectedLanguage;
        _view.Enabled = _copy.Enabled = _retry.Enabled = true;
        SetStatus(() => UiText.T("已翻译")); _tips.SetToolTip(_retry, UiText.T("重新翻译"));
        UpdateLanguages(); RefreshView();
    }
    public void SetError(string error)
    {
        ResultText = ""; _view.Enabled = _copy.Enabled = false; _retry.Enabled = true;
        _status.ForeColor = AppTheme.Current.Dark ? Color.FromArgb(255, 160, 155) : Color.FromArgb(178, 48, 42); SetStatus(error);
        _tips.SetToolTip(_retry, UiText.T("重试翻译")); RefreshView();
    }
    public void SetError(Func<string> error) { SetError(error()); _statusText = error; }
    internal void SetStatus(Func<string> status) { SetStatus(status()); _statusText = status; }
    internal void SetStatus(string status)
    {
        _statusText = null;
        _status.Text = status; _tips.SetToolTip(_status, status);
    }
    internal void ToggleView()
    {
        if (!_view.Enabled) return;
        ShowOriginal = !ShowOriginal; SetStatus(() => ShowOriginal ? UiText.T("显示原文") : UiText.T("已翻译"));
        RefreshView(); ViewChanged?.Invoke();
    }
    private void RefreshView()
    {
        _view.BackColor = ShowOriginal ? SnipMenuRenderer.Hover : Surface;
        _tips.SetToolTip(_view, ShowOriginal ? UiText.T("查看译文（当前显示原文）") : UiText.T("查看原文（当前显示译文）"));
        _tips.SetToolTip(_copy, ShowOriginal ? UiText.T("复制原文") : UiText.T("复制译文"));
        _view.Invalidate(); _copy.Invalidate();
    }
    public void CopyText()
    {
        var text = ShowOriginal ? _originalText : ResultText; if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); SetStatus(() => ShowOriginal ? UiText.T("原文已复制") : UiText.T("译文已复制")); }
        catch { SetStatus(() => UiText.T("复制失败，请重试")); }
    }
    public void CloseMenus() { foreach (var menu in _menus) if (menu.Visible) menu.Close(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AppTheme.Changed -= ApplyTheme;
            Localization.LanguageService.Changed -= RefreshLanguage;
            CloseMenus(); var menus = _menus.ToArray(); _menus.Clear();
            var context = SynchronizationContext.Current;
            if (context is not null) context.Post(_ => { foreach (var menu in menus) menu.Dispose(); }, null);
            else foreach (var menu in menus) menu.Dispose();
            _tips.Dispose(); _contentFont.Dispose(); _smallFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
