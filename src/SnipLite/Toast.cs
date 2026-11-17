namespace SnipLite;

/// <summary>Small borderless message that closes itself.</summary>
internal sealed class Toast : Form
{
    private static Toast? _current;
    private readonly System.Windows.Forms.Timer _timer;

    /// <summary>Snipaste's "显示通知" option; every toast respects it.</summary>
    public static bool Notifications { get; set; } = true;

    private Toast(string text, int milliseconds)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(28, 28, 28);
        Padding = new Padding(14, 9, 14, 9);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Microsoft YaHei UI", 9f);

        Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            MaximumSize = new Size(560, 0)
        });

        _timer = new System.Windows.Forms.Timer { Interval = milliseconds };
        _timer.Tick += (_, _) => Close();
        ApplyTheme();
        AppTheme.Changed += ApplyTheme;
    }

    private void ApplyTheme() { AppTheme.Apply(this); AppTheme.ApplyWindow(this); }

    /// <summary>Toasts must never steal keyboard focus from a pin or the snip overlay.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW (stay out of Alt+Tab)
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        AppTheme.ApplyWindow(this);

        var area = Screen.FromPoint(Win32.CursorPosition()).WorkingArea;
        Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);

        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(AppTheme.Current.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnClick(EventArgs e) => Close();

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        AppTheme.Changed -= ApplyTheme;
        _timer.Stop();
        _timer.Dispose();
        if (ReferenceEquals(_current, this)) _current = null;
        base.OnFormClosed(e);
    }

    public static void Show(string text, int milliseconds = 2200)
    {
        if (!Notifications) return;
        ShowCore(text, milliseconds);
    }

    /// <summary>Failures are reported even when the user turned notifications off.</summary>
    public static void Error(string text) => ShowCore(text, 4000);

    private static void ShowCore(string text, int milliseconds)
    {
        _current?.Close();

        var toast = new Toast(text, milliseconds);
        _current = toast;
        toast.Show();
    }
}
