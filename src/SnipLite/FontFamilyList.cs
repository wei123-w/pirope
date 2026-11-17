namespace SnipLite;

/// <summary>A native scrolling list keeps large font collections in one menu item.</summary>
internal sealed class FontFamilyList : ListBox
{
    private readonly Font _displayFont;

    public event Action<string>? Chosen;
    public event Action? Dismissed;

    public FontFamilyList(float scale, string selected)
    {
        _displayFont = new Font("Microsoft YaHei UI", 12 * scale, GraphicsUnit.Pixel);
        Font = _displayFont;
        BackColor = SnipMenuRenderer.Back;
        ForeColor = AppTheme.Current.Text;
        BorderStyle = BorderStyle.FixedSingle;
        IntegralHeight = false;
        ScrollAlwaysVisible = true;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = (int)Math.Round(28 * scale);
        AccessibleName = UiText.T("选择字体");

        var families = FontFamily.Families;
        BeginUpdate();
        try
        {
            Items.AddRange(families.Select(f => f.Name).Distinct().OrderBy(name => name).Cast<object>().ToArray());
            SelectedIndex = FindStringExact(selected);
        }
        finally
        {
            foreach (var family in families) family.Dispose();
            EndUpdate();
        }
        AppTheme.Changed += ApplyTheme;
    }

    private void ApplyTheme() { BackColor = SnipMenuRenderer.Back; ForeColor = AppTheme.Current.Text; Invalidate(); }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? SnipMenuRenderer.Hover : BackColor);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var text = Rectangle.Inflate(e.Bounds, -(int)Math.Round(8 * DeviceDpi / 96f), 0);
        TextRenderer.DrawText(e.Graphics, (string)Items[e.Index], Font, text, ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        Focus();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var index = IndexFromPoint(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0)
            Chosen?.Invoke((string)Items[index]);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Escape)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape) Dismissed?.Invoke();
            else if (SelectedItem is string family) Chosen?.Invoke(family);
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { AppTheme.Changed -= ApplyTheme; _displayFont.Dispose(); }
    }
}
