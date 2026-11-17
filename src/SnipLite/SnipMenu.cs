using System.Drawing.Drawing2D;

namespace SnipLite;

/// <summary>
/// Theme-aware rounded menus shared by the tray, setting pickers and capture tools.
/// </summary>
internal sealed class SnipMenuRenderer : ToolStripProfessionalRenderer
{
    internal static Color Back => AppTheme.Current.Card;
    internal static Color Hover => AppTheme.Current.Hover;

    public SnipMenuRenderer() : base(new SnipMenuColors())
    {
        RoundedEdges = true;
    }

    /// <summary>
    /// Render callbacks use item-local coordinates, even for rows below a separator.
    /// Fill the complete item so the highlight and its centered text share a row.
    /// </summary>
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new SolidBrush(Back)) g.FillRectangle(background, new Rectangle(Point.Empty, e.Item.Size));
        if (e.Item.Selected && e.Item.Enabled)
        {
            using var path = FluentDrawing.Rounded(new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2), 5 * (e.ToolStrip?.DeviceDpi ?? 96) / 96f);
            using var brush = new SolidBrush(Hover); g.FillPath(brush, path);
        }
        if (e.Item is ToolStripMenuItem { Checked: false, Tag: string icon } && icon.StartsWith("fluent:", StringComparison.Ordinal))
        {
            var size = 16 * (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
            FluentIcons.Draw(g, icon[7..], new RectangleF(8, (e.Item.Height - size) / 2, size, size), AppTheme.Current.Text);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? AppTheme.Current.Text : AppTheme.Current.Disabled;
        e.TextRectangle = new Rectangle(e.TextRectangle.X, 0, e.TextRectangle.Width, e.Item.Height);
        e.TextFormat = (e.TextFormat & ~TextFormatFlags.Bottom) | TextFormatFlags.VerticalCenter;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // A plain dark tick instead of the system's blue checkbox: these menus
        // are light grey strips, and the accent belongs to the annotation bar.
        var box = e.ImageRectangle;
        DrawTick(e.Graphics,
            box.Left + box.Width / 2,
            box.Top + box.Height / 2,
            Math.Max(4, box.Width / 3));
    }

    private static void DrawTick(Graphics g, int x, int y, int arm)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new Pen(AppTheme.Current.Accent, 1.6f);
        g.DrawLines(pen, new[]
        {
            new Point(x - arm, y),
            new Point(x - arm / 3, y + arm * 2 / 3),
            new Point(x + arm, y - arm)
        });
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(AppTheme.Current.Border, 1f);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(AppTheme.Current.Border, 1f);
        var bounds = new Rectangle(Point.Empty, e.ToolStrip.Size);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = FluentDrawing.Rounded(new RectangleF(.5f, .5f, bounds.Width - 1, bounds.Height - 1), 7);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = AppTheme.Current.Text;
        base.OnRenderArrow(e);
    }

    private sealed class SnipMenuColors : ProfessionalColorTable
    {
        private static Color Border => AppTheme.Current.Border;

        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientMiddle => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Back;
        public override Color ButtonSelectedHighlight => Hover;
        public override Color ButtonSelectedHighlightBorder => Border;
    }
}

internal static class SnipMenu
{
    public static ContextMenuStrip Create()
    {
        var menu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 9f),
            BackColor = SnipMenuRenderer.Back,
            ForeColor = AppTheme.Current.Text,
            ShowImageMargin = false,
            ShowCheckMargin = true,
            DropShadowEnabled = true,
            Renderer = new SnipMenuRenderer(),
            Padding = new Padding(4)
        };
        void ApplyTheme() { if (!menu.IsDisposed) { menu.BackColor = SnipMenuRenderer.Back; menu.ForeColor = AppTheme.Current.Text; menu.Invalidate(true); } }
        AppTheme.Changed += ApplyTheme;
        menu.Disposed += (_, _) => AppTheme.Changed -= ApplyTheme;
        menu.Opening += (_, _) => { ApplyTheme(); UiText.Apply(menu); };
        return menu;
    }

    public static ToolStripMenuItem Item(string text, Action action, string shortcut = "", bool check = false)
    {
        var item = new ToolStripMenuItem(text)
        {
            CheckOnClick = false,
            Checked = check,
            TextAlign = ContentAlignment.MiddleLeft,
            Tag = "fluent:" + FluentIcons.ForLabel(text),
            Padding = new Padding(0, 5, 0, 5)
        };

        if (shortcut.Length > 0)
            item.ShortcutKeyDisplayString = shortcut;

        item.Click += (_, _) => action();
        Localization.LocalizedControls.Apply(item);
        return item;
    }
}
