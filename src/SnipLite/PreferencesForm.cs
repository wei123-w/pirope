using System.Drawing.Drawing2D;

namespace SnipLite;

/// <summary>
/// Windows 11-style navigation and setting cards, bound directly to AppSettings.
/// </summary>
internal sealed partial class PreferencesForm : Form
{
    // ---- design tokens (96 dpi units) -------------------------------------
    private const int WinW = 960;
    private const int WinH = 720;
    private const int SidebarW = 182;
    private const int FooterH = 56;         // footer zone below the page
    private const int PagePad = 6;
    private const int LabelW = 244;
    private const int ControlGap = 12;
    private const int RowH = 58;
    private const int CheckH = 54;
    private const int CaptionH = 24;        // group caption
    private const int SectionGap = 22;      // between groups
    private const int HintH = 24;           // hint line
    private const int ControlH = 32;

    private static Color Page => AppTheme.Current.Window;
    private static Color Chrome => AppTheme.Current.Window;
    private static Color Line => AppTheme.Current.Border;
    private static Color Ink => AppTheme.Current.Text;
    private static Color Muted => AppTheme.Current.Muted;
    private static Color Hint => AppTheme.Current.Muted;
    private static Color Field => AppTheme.Current.Field;
    private static Color FieldEdge => AppTheme.Current.Border;
    private static readonly string[] TabIcons = { "settings", "palette", "capture", "pin", "output", "keyboard", "update", "about" };

    private readonly string[] _tabs = { "常规", "界面", "截屏", "贴图", "输出", "控制", "更新", "关于" };
    private readonly List<(Rectangle Rect, int Index)> _tabRects = new();

    private readonly AppSettings _s;
    private sealed class BufferedPage : Panel
    {
        public BufferedPage() { DoubleBuffered = true; ResizeRedraw = true; }
    }
    private readonly Panel _page = new BufferedPage();
    private readonly Button _reset = new FluentButton();

    private float _ui = 1f;
    private int _selected;
    private int _startTab;
    private int _hoverTab = -1;
    private bool _captions;
    private int _groups;

    private int _y;          // current row top, page coordinates
    private int _labelX;     // x of the label column
    private int _controlX;   // x of the first control

    private bool _ready;

    /// <summary>Raised after an option changed, so the tray can re-apply hotkeys etc.</summary>
    public event Action? Applied;

    public PreferencesForm(AppSettings settings, int startTab = 0)
    {
        _s = settings;
        Localization.LanguageService.Configure(settings.DisplayLanguage);
        _startTab = startTab;

        Text = "SnipLite 首选项";
        Icon = AppIcon.Window;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Chrome;
        Font = settings.CreateInterfaceFont();
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _page.BackColor = Page;
        _page.AutoScroll = true;

        StyleButton(_reset, "恢复默认");
        _reset.Click += (_, _) => ResetAll();

        Controls.Add(_page);
        Controls.Add(_reset);
        AppTheme.Changed += ApplyTheme;
        Localization.LanguageService.Changed += RefreshLanguage;
    }

    private bool _languageRefreshPending;
    private void RefreshLanguage()
    {
        if (IsDisposed || !IsHandleCreated || _languageRefreshPending) return;
        _languageRefreshPending = true;
        BeginInvoke(() =>
        {
            _languageRefreshPending = false;
            if (IsDisposed) return;
            Text = UiText.T("SnipLite 首选项");
            StyleButton(_reset, "恢复默认");
            PlaceChrome();
            ShowTab(_selected);
        });
    }

    private void ApplyTheme()
    {
        if (IsDisposed) return;
        AppTheme.Apply(this);
        AppTheme.ApplyWindow(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AppTheme.ApplyWindow(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AppTheme.Changed -= ApplyTheme;
            Localization.LanguageService.Changed -= RefreshLanguage;
        }
        base.Dispose(disposing);
    }

    // ---------------------------------------------------------------- layout

    private int Px(int value) => (int)Math.Round(value * _ui);

    private Rectangle PageRect() => new(
        Px(SidebarW + 22),
        Px(86),
        ClientSize.Width - Px(SidebarW + 44),
        ClientSize.Height - Px(86 + FooterH));

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Text = UiText.T("SnipLite 首选项");

        _ui = DeviceDpi / 96f;
        StyleButton(_reset, UiText.T("恢复默认", _s.DisplayLanguage));

        var area = Screen.FromControl(this).WorkingArea;
        ClientSize = new Size(Math.Min(Px(WinW), area.Width - Px(32)), Math.Min(Px(WinH), area.Height - Px(64)));
        PlaceChrome();

        _ready = true;
        ShowTab(Math.Clamp(_startTab, 0, _tabs.Length - 1));
        ApplyTheme();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // No control should wear a focus highlight when the dialog appears:
        // the page is a read-then-adjust surface, not a form to be typed into.
        ActiveControl = null;
    }

    private void PlaceChrome()
    {
        _page.Bounds = PageRect();

        var footer = ClientSize.Height - Px(FooterH);
        _reset.Bounds = new Rectangle(
            ClientSize.Width - Px(28) - _reset.Width,
            footer + Px(8),
            _reset.Width,
            Px(32));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (!_ready) return;

        PlaceChrome();
        _tabRects.Clear();
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            return;
        }

        if (e.Control && e.KeyCode == Keys.Tab)
        {
            SelectTab((_selected + (e.Shift ? _tabs.Length - 1 : 1)) % _tabs.Length);
            e.Handled = e.SuppressKeyPress = true;
            return;
        }

        base.OnKeyDown(e);
    }

    // ----------------------------------------------------------------- paint

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (!_ready) return;

        var g = e.Graphics;
        var palette = AppTheme.Current;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var chrome = new SolidBrush(Chrome)) g.FillRectangle(chrome, e.ClipRectangle);
        using (var sidebar = new SolidBrush(palette.Sidebar)) g.FillRectangle(sidebar, 0, 0, Px(SidebarW), ClientSize.Height);
        FluentIcons.Draw(g, "capture", new Rectangle(Px(23), Px(26), Px(26), Px(26)), palette.Accent);
        using var brandFont = new Font(Font.FontFamily, Font.Size + 3, FontStyle.Bold);
        TextRenderer.DrawText(g, "SnipLite", brandFont, new Rectangle(Px(58), Px(25), Px(116), Px(30)), Ink, TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, UiText.T("首选项", _s.DisplayLanguage), Font, new Rectangle(Px(24), Px(65), Px(136), Px(24)), Muted);
        using var headingFont = new Font(Font.FontFamily, Font.Size + 10, FontStyle.Bold);
        TextRenderer.DrawText(g, UiText.T(_tabs[_selected], _s.DisplayLanguage), headingFont,
            new Rectangle(PageRect().X + Px(PagePad), Px(20), PageRect().Width, Px(44)), Ink, TextFormatFlags.VerticalCenter);

        if (_tabRects.Count == 0) LayoutTabs(g);

        foreach (var (rect, index) in _tabRects)
        {
            var active = index == _selected;

            if (active || index == _hoverTab)
            {
                using var path = FluentDrawing.Rounded(rect, Px(5));
                using var fill = new SolidBrush(active ? palette.Card : palette.Hover);
                g.FillPath(fill, path);
            }
            if (active)
            {
                using var indicator = new SolidBrush(palette.Accent);
                using var path = FluentDrawing.Rounded(new Rectangle(rect.Left, rect.Top + Px(10), Px(3), rect.Height - Px(20)), Px(2));
                g.FillPath(indicator, path);
            }
            FluentIcons.Draw(g, TabIcons[index], new Rectangle(rect.X + Px(12), rect.Y + Px(8), Px(20), Px(20)), Ink);
            TextRenderer.DrawText(g, UiText.T(_tabs[index], _s.DisplayLanguage), Font,
                new Rectangle(rect.X + Px(43), rect.Y, rect.Width - Px(47), rect.Height), Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        TextRenderer.DrawText(g, AppInfo.Version, Font, new Rectangle(Px(24), ClientSize.Height - Px(34), Px(130), Px(20)), Hint);
        var footerY = ClientSize.Height - Px(FooterH) + Px(13);
        FluentIcons.Draw(g, "check", new Rectangle(PageRect().X + Px(6), footerY, Px(17), Px(17)), palette.Accent);
        var themeText = palette.HighContrast ? "跟随 Windows · 高对比度" : palette.Dark ? "跟随 Windows · 深色" : "跟随 Windows · 浅色";
        TextRenderer.DrawText(g, UiText.T(themeText, _s.DisplayLanguage), Font,
            new Rectangle(PageRect().X + Px(30), footerY - Px(2), Px(260), Px(23)), Muted, TextFormatFlags.VerticalCenter);
    }

    private void LayoutTabs(Graphics g)
    {
        _tabRects.Clear();

        for (var i = 0; i < _tabs.Length; i++)
        {
            var y = i < 6 ? Px(105 + i * 40) : ClientSize.Height - Px(128 - (i - 6) * 40);
            _tabRects.Add((new Rectangle(Px(10), y, Px(SidebarW - 20), Px(36)), i));
        }
    }

    // ------------------------------------------------------------ hit testing

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var hover = _tabRects.FindIndex(t => t.Rect.Contains(e.Location));

        if (hover == _hoverTab) return;

        var previous = _hoverTab;
        _hoverTab = hover;
        Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
        InvalidateTab(previous);
        InvalidateTab(hover);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoverTab < 0) return;

        var previous = _hoverTab;
        _hoverTab = -1;
        InvalidateTab(previous);
    }

    private void InvalidateTab(int index)
    {
        if (index < 0 || index >= _tabRects.Count) return;
        var rect = _tabRects[index].Rect; rect.Inflate(2, 2); rect.Height++;
        Invalidate(rect);
    }
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (!_ready) return;
        _tabRects.Clear(); Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left) return;

        foreach (var (rect, index) in _tabRects)
        {
            if (!rect.Contains(e.Location)) continue;

            if (index != _selected) ShowTab(index);
            return;
        }
    }

    /// <summary>Switch pages from outside (the tray "帮助" entry opens 关于).</summary>
    public void SelectTab(int index)
    {
        if (!_ready)
        {
            _startTab = index;
            return;
        }

        index = Math.Clamp(index, 0, _tabs.Length - 1);
        if (index != _selected) ShowTab(index);
    }
    internal void SelectSubPage(int tab, int sub) { _subPages[tab] = sub; ShowTab(tab); }

    // ----------------------------------------------------------------- pages

    private void ShowTab(int index)
    {
        if (!_ready) return;

        _selected = index;
        _tabRects.Clear();
        _sectionProperties.Clear();
        _page.AutoScrollPosition = Point.Empty;

        _page.SuspendLayout();

        while (_page.Controls.Count > 0)
            _page.Controls[0].Dispose();

        _y = Px(PagePad);
        _labelX = Px(PagePad);
        _controlX = _labelX + Px(LabelW + ControlGap);
        _groups = 0;

        // Pages with a single group need no caption repeating the tab name.
        _captions = index is 0 or 1 or 4;

        switch (index)
        {
            case 0: BuildGeneral(); break;
            case 1: BuildInterface(); break;
            case 2: BuildCapture(); break;
            case 3: BuildPin(); break;
            case 4: BuildOutput(); break;
            case 5: BuildControl(); break;
            case 6: BuildUpdate(); break;
            default: BuildAbout(); break;
        }

        _page.AutoScrollMinSize = new Size(0, _y + Px(PagePad));
        _page.ResumeLayout();
        UiText.Apply(_page, _s.DisplayLanguage);
        AppTheme.Apply(_page);
        _reset.Visible = index <= 5;
        ActiveControl = null;
        Invalidate();
    }

    private void BuildGeneral() => GeneralPage();
    private void BuildInterface() => SubPages(new[] { "常规", "放大镜" }, InterfacePage, MagnifierPage);
    private void BuildCapture() => SubPages(new[] { "显示", "行为", "按键" }, CaptureDisplayPage, CaptureBehaviorPage, CaptureKeysPage);
    private void BuildPin() => SubPages(new[] { "显示", "行为", "转换" }, PinDisplayPage, PinBehaviorPage, PinConversionPage);
    private void BuildOutput() => SubPages(new[] { "文件", "剪贴板" }, OutputFilePage, OutputClipboardPage);
    private void BuildControl() => SubPages(new[] { "全局快捷键", "鼠标" }, HotkeysPage, MousePage);
    private void BuildUpdate()
    {
        Row("当前版本", MakeStatic(AppInfo.Version));
        Row("发布版本", MakeWebLink(AppInfo.ReleasesUrl));
        var status = MakeStatic("");
        var check = new FluentButton();
        var download = new FluentButton();
        string Local(string text) => UiText.T(text, _s.DisplayLanguage);
        StyleButton(check, Local("检查更新"));
        StyleButton(download, Local("下载安装包"));
        download.Enabled = false;
        Row("检查更新", check, download);
        Row("更新状态", status);

        (string Tag, Version Version)? release = null;
        check.Click += async (_, _) =>
        {
            check.Enabled = download.Enabled = false;
            status.Text = Local("正在检查更新…");
            try
            {
                var latest = await ReleaseUpdates.CheckAsync();
                if (status.IsDisposed) return;
                release = latest;
                var current = Version.Parse(AppInfo.Version.Split('-')[0]);
                download.Enabled = latest.Version > current ||
                    latest.Version == current && AppInfo.Version.Contains('-');
                status.Text = download.Enabled ? $"{Local("发现新版本")} {latest.Tag}" : Local("已是最新版本");
            }
            catch (Exception ex)
            {
                AppLog.Write("检查更新失败", ex);
                if (!status.IsDisposed) status.Text = Local("检查更新失败，请重试");
            }
            finally { if (!check.IsDisposed) check.Enabled = true; }
        };

        download.Click += async (_, _) =>
        {
            if (release is not { } latest) return;
            var file = $"SnipLite-{latest.Tag[1..]}-win-x64-setup.exe";
            using var dialog = new SaveFileDialog { FileName = file, Filter = "Windows installer|*.exe", AddExtension = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            check.Enabled = download.Enabled = false;
            status.Text = Local("正在下载安装包…");
            try
            {
                await ReleaseUpdates.DownloadAsync(latest.Tag, file, dialog.FileName);
                if (!status.IsDisposed) status.Text = Local("已保存安装包，请退出 SnipLite 后运行安装程序");
            }
            catch (Exception ex)
            {
                AppLog.Write("下载更新失败", ex);
                if (!status.IsDisposed) status.Text = Local("下载失败，请重试");
            }
            finally { if (!check.IsDisposed) check.Enabled = download.Enabled = true; }
        };
    }
    private void BuildAbout()
    {
        Row("程序", MakeStatic("SnipLite " + AppInfo.Version));
        var executable = MakeText(Application.ExecutablePath, FillWidth()); executable.ReadOnly = true; Row("程序文件", executable);
        Row("运行环境", MakeStatic(".NET 10 · WinForms")); Row("识别引擎", MakeStatic("SimdPaddleOCR · ZXing.Net"));
        Row("GitHub 项目", MakeWebLink(AppInfo.RepositoryUrl));
        Row("问题反馈", MakeWebLink(AppInfo.IssuesUrl));
        var license = new LinkLabel { Text = "MIT License / Third-party notices", AutoSize = true,
            LinkColor = _s.Accent, BackColor = Page, Cursor = Cursors.Hand, TabStop = true };
        license.LinkClicked += (_, _) => LicenseNotices.Show(this);
        Row("开源许可证", license);
        HintText("截图、贴图、标注、文字与二维码识别均在本机处理。");
    }

    private LinkLabel MakeWebLink(string url)
    {
        var link = new LinkLabel
        {
            Text = url, AutoSize = false, AutoEllipsis = true,
            Size = new Size(FillWidth(), Px(24)), TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Page, LinkColor = _s.Accent, ActiveLinkColor = _s.Accent,
            VisitedLinkColor = _s.Accent, LinkBehavior = LinkBehavior.HoverUnderline,
            Cursor = Cursors.Hand, TabStop = true
        };
        link.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { AppLog.Write("打开项目网址失败", ex); Toast.Error(UiText.T("无法打开浏览器，请手动访问：") + url); }
        };
        return link;
    }
    // ------------------------------------------------------------- sections

    /// <summary>Width of the page's content column.</summary>
    private int ContentWidth() => PageRect().Width - Px(PagePad * 2) - SystemInformation.VerticalScrollBarWidth;

    /// <summary>Width left for a field that starts in the control column.</summary>
    private int FillWidth() => ContentWidth() - Px(LabelW + ControlGap + 16);

    /// <summary>
    /// A named group of rows. Pages with more than one group show a caption and
    /// breathe a little; single-group pages start straight at the page padding.
    /// </summary>
    private void Section(string title, Action content)
    {
        if (_groups++ > 0) _y += Px(SectionGap);

        if (_captions)
        {
            _page.Controls.Add(new Label
            {
                Text = title,
                AutoSize = false,
                Bounds = new Rectangle(_labelX, _y, ContentWidth(), Px(18)),
                ForeColor = Muted,
                BackColor = Page,
                TextAlign = ContentAlignment.MiddleLeft
            });

            _y += Px(CaptionH);
        }

        content();
    }

    // --------------------------------------------------------------- helpers

    private void Change(Action apply)
    {
        apply();
        Persist();
    }

    private void Persist()
    {
        Localization.LanguageService.Configure(_s.DisplayLanguage);
        _s.Save();
        Applied?.Invoke();
    }

    private async Task<bool> ApplyStartupAsync(bool enabled, bool administrator)
    {
        Enabled = false;
        try
        {
            var applied = await Task.Run(() => Startup.TryApply(enabled, administrator));
            if (!applied && !IsDisposed) Toast.Error(UiText.T("开机启动设置未修改。"));
            return applied;
        }
        finally { if (!IsDisposed) Enabled = true; }
    }

    private async void ResetAll()
    {
        var defaults = AppSettings.Reset();
        if (_selected == 0 && !await ApplyStartupAsync(defaults.StartWithWindows, defaults.StartAsAdministrator)) return;
        if (IsDisposed) return;
        foreach (var name in _sectionProperties)
        {
            var property = typeof(AppSettings).GetProperty(name)!;
            property.SetValue(_s, property.GetValue(defaults));
        }
        Font = _s.CreateInterfaceFont();
        Toast.Notifications = _s.ShowToast;
        Persist();
        ShowTab(_selected);
    }
    private void Row(string label, params Control[] controls)
    {
        var height = Math.Max(Px(RowH), controls.Select(c => c.Height + Px(22)).DefaultIfEmpty(0).Max());
        var card = NewSettingRow(height);
        if (label.Length > 0)
        {
            AddRowIcon(card, label);
            card.Controls.Add(new Label { Text = label, AutoSize = false, AutoEllipsis = true, Bounds = new Rectangle(Px(48), Px(6), Px(LabelW - 52), height - Px(12)),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ink, BackColor = Color.Transparent });
        }
        var x = Px(LabelW + ControlGap);
        foreach (var original in controls)
        {
            if (string.IsNullOrEmpty(original.AccessibleName)) original.AccessibleName = label;
            var control = original is TextBoxBase or NumericUpDown ? new FluentInputFrame(original) { Height = Px(ControlH) } : original;
            control.Location = new Point(x, Math.Max(0, (height - control.Height) / 2));
            if (control.Width > card.Width - x - Px(14)) control.Width = Math.Max(Px(40), card.Width - x - Px(14));
            if (string.IsNullOrEmpty(control.AccessibleName)) control.AccessibleName = label;
            card.Controls.Add(control);
            x += control.Width + Px(ControlGap);
        }
        _y += height + Px(6);
    }

    private FluentSettingRow NewSettingRow(int height)
    {
        var row = new FluentSettingRow { Bounds = new Rectangle(_labelX, _y, ContentWidth(), height) };
        _page.Controls.Add(row);
        return row;
    }

    private void AddRowIcon(Control row, string label)
    {
        var icon = new Panel { Bounds = new Rectangle(Px(15), (row.Height - Px(20)) / 2, Px(20), Px(20)), BackColor = AppTheme.Current.Card };
        icon.Paint += (_, e) => FluentIcons.Draw(e.Graphics, FluentIcons.ForLabel(label), icon.ClientRectangle, Ink);
        row.Controls.Add(icon);
    }

    /// <summary>A row whose first control stretches to fill the remaining width.</summary>
    private void RowFill(string label, Control fill, params Control[] trailing)
    {
        var used = 0;

        foreach (var control in trailing)
            used += control.Width + Px(ControlGap);

        fill.Width = Math.Max(Px(120), FillWidth() - used);

        var all = new Control[trailing.Length + 1];
        all[0] = fill;
        trailing.CopyTo(all, 1);

        Row(label, all);
    }

    private void Check(string text, bool value, Action<bool> onChange)
    {
        var card = NewSettingRow(Px(CheckH));
        AddRowIcon(card, text);
        var box = new FluentToggle
        {
            Text = text,
            AutoSize = false,
            Bounds = new Rectangle(Px(48), Px(8), card.Width - Px(62), card.Height - Px(16)),
            BackColor = AppTheme.Current.Card,
            ForeColor = Ink,
            Checked = value
        };

        box.CheckedChanged += (_, _) => onChange(box.Checked);

        card.Controls.Add(box);
        _y += Px(CheckH + 6);
    }

    private void AddLabel(string text, int x, int y, int width)
    {
        _page.Controls.Add(new Label
        {
            Text = text,
            AutoSize = false,
            Bounds = new Rectangle(x, y, Px(width), Px(RowH)),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Ink,
            BackColor = Page
        });
    }

    private void HintText(string text)
    {
        _y += Px(4);
        text = UiText.T(text, _s.DisplayLanguage);
        var height = Math.Max(Px(22), TextRenderer.MeasureText(text, Font,
            new Size(ContentWidth(), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + Px(4));

        _page.Controls.Add(new Label
        {
            Text = text,
            AutoSize = false,
            Bounds = new Rectangle(_labelX, _y, ContentWidth(), height),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Hint,
            BackColor = Page
        });

        _y += height + Px(2);
    }

    private static Control MakeStatic(string text) => new Label
    {
        Text = text,
        AutoSize = true,
        ForeColor = Ink,
        BackColor = Color.Transparent,
        Margin = Padding.Empty
    };

    /// <summary>
    /// A combo box drawn the way the rest of the app looks: a flat grey field
    /// that opens the same menu the tray uses. The native flat ComboBox loses
    /// its text while unfocused, and its blue selection fights the palette.
    /// </summary>
    private Button MakeChooser(string[] options, int selected, Action<int> onChange)
    {
        // Language names stay in their native scripts so users can always find their language.
        var nativeLanguages = options.SequenceEqual(new[] { UiText.T("跟随系统") }
            .Concat(Localization.LanguageService.Languages.Select(l => l.NativeName)));
        if (!nativeLanguages) options = options.Select(option => UiText.T(option, _s.DisplayLanguage)).ToArray();
        var button = new FluentButton
        {
            Tag = nativeLanguages ? "localization:native" : null,
            DropDown = true,
            Text = options[Math.Clamp(selected, 0, options.Length - 1)],
            AutoSize = false,
            FlatStyle = FlatStyle.Flat,
            BackColor = Field,
            ForeColor = Ink,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(Px(8), 0, Px(26), 0),
            Width = Px(190),
            Height = Px(ControlH),
            TabStop = true
        };

        button.FlatAppearance.BorderColor = FieldEdge;
        button.FlatAppearance.MouseOverBackColor = AppTheme.Current.Hover;
        button.FlatAppearance.MouseDownBackColor = AppTheme.Current.AccentSoft;

        // One menu for the life of the chooser, owned by the button so it is
        // disposed together with it. Re-creating (and disposing) the menu on
        // every click upsets ToolStripManager's message filter.
        var menu = SnipMenu.Create();
        var items = new ToolStripMenuItem[options.Length];

        button.ContextMenuStrip = menu;

        for (var i = 0; i < options.Length; i++)
        {
            var index = i;

            items[i] = SnipMenu.Item(options[i], () =>
            {
                button.Text = options[index];
                onChange(index);
            });
            items[i].Tag = null;
            if (nativeLanguages) items[i].Tag = "localization:native";
            items[i].Text = options[i];

            menu.Items.Add(items[i]);
        }

        menu.Opening += (_, _) =>
        {
            for (var i = 0; i < options.Length; i++)
                items[i].Checked = options[i] == button.Text;

            menu.MinimumSize = new Size(button.Width, 0);
        };

        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height + Px(2)));
        button.KeyDown += (_, e) =>
        {
            if (e.KeyCode is not (Keys.Down or Keys.Up or Keys.F4)) return;
            menu.Show(button, new Point(0, button.Height + Px(2)));
            items[Math.Clamp(Array.FindIndex(options, option => option == button.Text), 0, items.Length - 1)].Select();
            e.Handled = e.SuppressKeyPress = true;
        };
        return button;
    }

    private NumericUpDown MakeNumber(int min, int max, int value, int width, Action<int> onChange)
    {
        var number = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = Px(width),
            BackColor = Field,
            ForeColor = Ink,
            TextAlign = HorizontalAlignment.Right
        };

        number.ValueChanged += (_, _) => onChange((int)number.Value);

        return number;
    }

    private TextBox MakeText(string value, int width) => new()
    {
        Text = value,
        Width = width,
        BackColor = Field,
        ForeColor = Ink,
        BorderStyle = BorderStyle.FixedSingle
    };

    private void StyleButton(Button button, string text)
    {
        text = UiText.T(text, _s.DisplayLanguage);
        button.Text = text;
        button.AutoSize = false;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Field;
        button.ForeColor = Ink;
        button.FlatAppearance.BorderColor = FieldEdge;
        button.FlatAppearance.MouseOverBackColor = AppTheme.Current.Hover;
        button.FlatAppearance.MouseDownBackColor = AppTheme.Current.AccentSoft;
        button.Height = Px(ControlH);
        using var metrics = IsHandleCreated ? CreateGraphics() : null;
        var measured = metrics is null ? TextRenderer.MeasureText(text, Font) : TextRenderer.MeasureText(metrics, text, Font);
        button.Width = measured.Width + Px(28);
    }

    private void AddButton(int x, string text, Action action)
    {
        var button = new FluentButton();
        StyleButton(button, text);
        button.Bounds = new Rectangle(x, _y, button.Width, Px(28));
        button.Click += (_, _) => action();

        _page.Controls.Add(button);
        _y += Px(RowH + 2);
    }

    private static string Hex(Color color) =>
        "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");

    private void Open(string path, bool revealFile)
    {
        try
        {
            if (revealFile)
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                return;
            }

            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start("explorer.exe", path);
        }
        catch
        {
        }
    }
}
