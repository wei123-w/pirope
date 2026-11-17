namespace SnipLite;

/// <summary>One fixed-size gallery avoids ToolStrip's empty-label item sizing.</summary>
internal sealed class ArrowStyleGallery : Control
{
    private readonly float _scale;
    private readonly ArrowStyle _selected;
    private readonly ToolTip _hint = new();
    private int _hover = -1;
    private int RowHeight => (int)Math.Round(40 * _scale);
    private static readonly ArrowStyle[] Styles = { ArrowStyle.Line, ArrowStyle.Solid, ArrowStyle.Open, ArrowStyle.Outline, ArrowStyle.Double, ArrowStyle.TaperedSolid, ArrowStyle.TaperedOutline };
    private static readonly string[] Names = { "直线", "实心箭头", "开放箭头", "空心箭头", "双向箭头", "楔形实心箭头", "楔形空心箭头" };
    public event Action<ArrowStyle>? Chosen;
    public ArrowStyleGallery(float scale, ArrowStyle selected)
    {
        _scale = scale; _selected = selected; DoubleBuffered = true;
        Size = new Size((int)Math.Round(220 * scale), RowHeight * Styles.Length);
        BackColor = SnipMenuRenderer.Back; Margin = Padding.Empty; AccessibleName = UiText.T("箭头样式");
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        for (var i = 0; i < Styles.Length; i++)
            DrawPreview(e.Graphics, new Rectangle(0, i * RowHeight, Width, RowHeight), Styles[i], Styles[i] == _selected, i == _hover, _scale);
    }
    internal static void DrawPreview(Graphics g, Rectangle row, ArrowStyle style, bool selected, bool hover, float scale)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (hover || selected) { using var back = new SolidBrush(SnipMenuRenderer.Hover); g.FillRectangle(back, row); }
        var cy = row.Top + row.Height / 2;
        if (selected)
        {
            using var tick = new Pen(Color.DimGray, 1.8f * scale);
            g.DrawLines(tick, new[] { new PointF(row.Left + 10*scale, cy), new PointF(row.Left + 14*scale, cy + 4*scale), new PointF(row.Left + 21*scale, cy - 5*scale) });
        }
        var arrow = new ArrowShape
        {
            Start = new Point(row.Left + (int)(38*scale), cy), End = new Point(row.Right - (int)(22*scale), cy),
            Width = 2.2f * scale, Color = Color.FromArgb(45, 45, 45), Style = style
        };
        using var dummy = new Bitmap(1, 1); arrow.Render(g, dummy);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var row = Math.Clamp(e.Y / RowHeight, 0, Styles.Length - 1);
        if (_hover == row) return;
        _hover = row; _hint.SetToolTip(this, UiText.T(Names[row])); Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) Chosen?.Invoke(Styles[Math.Clamp(e.Y / RowHeight, 0, Styles.Length - 1)]);
    }
    protected override void Dispose(bool disposing) { if (disposing) _hint.Dispose(); base.Dispose(disposing); }
}
