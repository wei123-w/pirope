using System.Drawing.Drawing2D;

namespace SnipLite;

/// <summary>A complete, selectable result. Copy is always an explicit action.</summary>
internal sealed class RecognitionResultForm : Form
{
    private static Color Ink => AppTheme.Current.Text;
    private static Color Muted => AppTheme.Current.Muted;
    private static Color Accent => AppTheme.Current.Accent;
    private readonly TextBox _content;
    private readonly Label _status;
    private readonly Button _copy;
    private readonly Button _close;
    private readonly Panel _header;
    private readonly Panel _card;
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly Label _kind;
    private readonly Label _count;
    private readonly Panel _icon;
    private readonly string _result;

    public RecognitionResultForm(string result)
    {
        _result = result;
        Text = UiText.T("识别结果");
        Font = new Font("Microsoft YaHei UI", 9.5f);
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.White;
        ForeColor = Ink;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;
        MaximizeBox = false;
        TopMost = true;
        DoubleBuffered = true;

        var isLink = Uri.TryCreate(result.Trim(), UriKind.Absolute, out var uri)
                     && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        _header = new Panel { BackColor = Color.White };
        _icon = new Panel { BackColor = Color.FromArgb(234, 245, 255) };
        _icon.Paint += DrawQrIcon;
        _title = new Label { Text = UiText.T("识别成功"), Font = new Font(Font.FontFamily, 15f, FontStyle.Bold) };
        _subtitle = new Label { Text = UiText.T("二维码 / 条码内容"), ForeColor = Muted };
        _header.Controls.AddRange(new Control[] { _icon, _title, _subtitle });

        _card = new Panel { BackColor = Color.FromArgb(247, 249, 252) };
        _card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var border = new Pen(AppTheme.Current.Border);
            using var path = FluentDrawing.Rounded(new RectangleF(.5f, .5f, _card.Width - 1, _card.Height - 1), Px(8));
            e.Graphics.DrawPath(border, path);
        };
        _kind = new Label { Text = isLink ? UiText.T("链接") : UiText.T("文本内容"), ForeColor = Muted };
        _count = new Label { Text = Localization.LanguageService.Format("{0:N0} 字符", new System.Globalization.StringInfo(result).LengthInTextElements), ForeColor = Muted, TextAlign = ContentAlignment.TopRight };
        _content = new TextBox
        {
            Text = result, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.None, WordWrap = true, Font = Font,
            BackColor = _card.BackColor, ForeColor = Ink,
            BorderStyle = BorderStyle.None, HideSelection = false,
            TabIndex = 2, AccessibleName = UiText.T("识别内容")
        };
        _card.Controls.AddRange(new Control[] { _kind, _count, _content });
        _close = ActionButton(UiText.T("关闭"), false);
        _close.DialogResult = DialogResult.Cancel;
        _close.TabIndex = 1;
        _copy = ActionButton(UiText.T("复制内容"), true);
        _copy.TabIndex = 0;
        _copy.Click += (_, _) => CopyResult();
        _status = new Label { Text = UiText.T("Esc 关闭"), ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft };

        Controls.AddRange(new Control[] { _header, _card, _status, _close, _copy });
        AcceptButton = _copy;
        CancelButton = _close;
        ActiveControl = _copy;
        ClientSize = new Size(560, 340);
        ApplyTheme();
        AppTheme.Changed += ApplyTheme;
    }

    private void ApplyTheme()
    {
        AppTheme.Apply(this);
        BackColor = _header.BackColor = AppTheme.Current.Window;
        _card.BackColor = _content.BackColor = AppTheme.Current.Card;
        _icon.BackColor = AppTheme.Current.AccentSoft;
        _subtitle.ForeColor = _kind.ForeColor = _count.ForeColor = _status.ForeColor = Muted;
        _copy.BackColor = Accent; _copy.ForeColor = AppTheme.Current.OnAccent;
        AppTheme.ApplyWindow(this);
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); AppTheme.ApplyWindow(this); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) AppTheme.Changed -= ApplyTheme;
        base.Dispose(disposing);
    }

    private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

    protected override void OnLoad(EventArgs e)
    {
        var area = Screen.FromPoint(Win32.CursorPosition()).WorkingArea;
        // Short results stay compact; longer payloads gain reading space and
        // wrap vertically, without stretching the popup across the screen.
        var lines = _result.Replace("\r", "").Split('\n').Sum(line => Math.Max(1, (line.Length + 47) / 48));
        ClientSize = new Size(Math.Min(Px(560), area.Width - Px(32)),
            Math.Min(Px(Math.Clamp(300 + Math.Max(0, lines - 3) * 22, 300, 480)), area.Height - Px(80)));
        MinimumSize = new Size(Math.Min(Px(400), Width), Math.Min(Px(280), Height));
        Location = new Point(area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2);
        base.OnLoad(e);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_header is null || _status is null) return;
        var pad = Px(24);
        var width = ClientSize.Width - pad * 2;
        _header.SetBounds(pad, Px(22), width, Px(56));
        _icon.SetBounds(0, Px(2), Px(48), Px(48));
        _title.SetBounds(Px(64), 0, width - Px(64), Px(32));
        _subtitle.SetBounds(Px(64), Px(36), width - Px(64), Px(20));

        var footer = ClientSize.Height - Px(60);
        _card.SetBounds(pad, Px(96), width, Math.Max(Px(70), footer - Px(116)));
        _kind.SetBounds(Px(16), Px(14), width / 2, Px(22));
        _count.SetBounds(width / 2, Px(14), width / 2 - Px(16), Px(22));
        _content.SetBounds(Px(16), Px(46), width - Px(32), Math.Max(Px(22), _card.Height - Px(62)));
        var textHeight = TextRenderer.MeasureText(_result, _content.Font,
            new Size(Math.Max(1, _content.Width - SystemInformation.VerticalScrollBarWidth), int.MaxValue),
            TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;
        _content.ScrollBars = textHeight > _content.Height ? ScrollBars.Vertical : ScrollBars.None;

        _copy.SetBounds(ClientSize.Width - pad - Px(112), footer, Px(112), Px(36));
        _close.SetBounds(_copy.Left - Px(88), footer, Px(76), Px(36));
        _status.SetBounds(pad, footer, Math.Max(0, _close.Left - pad - Px(12)), Px(36));
    }

    private static Button ActionButton(string text, bool primary)
    {
        var button = new FluentButton
        {
            Text = text, FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Color.White,
            ForeColor = primary ? Color.White : Ink,
            UseVisualStyleBackColor = false, Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(221, 227, 234);
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0, 113, 200) : Color.FromArgb(247, 249, 252);
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(0, 98, 180) : Color.FromArgb(237, 241, 246);
        return button;
    }

    private void CopyResult()
    {
        try
        {
            Clipboard.SetText(_result);
            _copy.Text = UiText.T("已复制");
            _status.Text = UiText.T("已复制到剪贴板");
            _status.ForeColor = Color.FromArgb(30, 132, 88);
        }
        catch
        {
            _status.Text = UiText.T("复制失败，请重试");
            _status.ForeColor = Color.FromArgb(190, 62, 57);
        }
    }

    private void DrawQrIcon(object? sender, PaintEventArgs e)
    {
        FluentIcons.Draw(e.Graphics, "qr", new Rectangle(Px(10), Px(10), Px(28), Px(28)), Accent);
    }
}
