using System.Drawing.Drawing2D;

namespace SnipLite;

internal static class FluentDrawing
{
    public static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Keeps native text editing and number validation inside a shared Fluent field.</summary>
internal sealed class FluentInputFrame : UserControl
{
    private readonly Control _editor;
    private readonly Panel _clip = new();
    private readonly NumericUpDown? _number;
    private bool _hover;
    private int _hoverStep;

    public FluentInputFrame(Control editor)
    {
        var editorWidth = editor.Width;
        _editor = editor;
        _number = editor as NumericUpDown;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        TabStop = false;
        AccessibleName = editor.AccessibleName;
        Size = new Size(editorWidth, (int)Math.Round(32 * editor.DeviceDpi / 96f));
        if (editor is TextBoxBase text) text.BorderStyle = BorderStyle.None;
        if (_number is not null) _number.BorderStyle = BorderStyle.None;
        Controls.Add(_clip);
        _clip.Controls.Add(editor);
        editor.Margin = Padding.Empty;
        editor.Enter += EditorStateChanged;
        editor.Leave += EditorStateChanged;
        editor.MouseEnter += EditorMouseEnter;
        editor.MouseLeave += EditorMouseLeave;
        editor.FontChanged += EditorFontChanged;
        if (_number is not null) _number.ValueChanged += EditorStateChanged;
        PerformLayout();
    }

    private void EditorStateChanged(object? sender, EventArgs e) => Invalidate();
    private void EditorMouseEnter(object? sender, EventArgs e) { _hover = true; Invalidate(); }
    private void EditorMouseLeave(object? sender, EventArgs e) { _hover = false; Invalidate(); }
    private void EditorFontChanged(object? sender, EventArgs e) => PerformLayout();
    private int Px(float value) => (int)Math.Round(value * DeviceDpi / 96f);
    private Rectangle StepBounds(int step) => new(Width - Px(step < 0 ? 54 : 28), Px(3), Px(25), Height - Px(6));

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_editor is null || _editor.Parent != _clip || _clip.Parent != this) return;
        var reserve = _number is null ? 0 : Px(54);
        var editorHeight = _editor.PreferredSize.Height;
        _clip.Bounds = new Rectangle(Px(10), Math.Max(Px(3), (Height - editorHeight) / 2), Math.Max(1, Width - Px(20) - reserve), editorHeight);
        // Clip the native spinner; the editor still handles typing, arrows, wheel and validation.
        var spinner = _number?.Controls.Cast<Control>().FirstOrDefault(c => c is not TextBoxBase)?.Width ?? 0;
        _editor.Bounds = new Rectangle(0, 0, _clip.Width + spinner, editorHeight);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _hover = true;
        var step = _number is null ? 0 : StepBounds(-1).Contains(e.Location) ? -1 : StepBounds(1).Contains(e.Location) ? 1 : 0;
        if (_hoverStep != step) { _hoverStep = step; Invalidate(); }
        Cursor = step == 0 ? Cursors.IBeam : Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _hoverStep = 0; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _editor.Focus();
        if (_number is null) return;
        if (StepBounds(-1).Contains(e.Location)) _number.DownButton();
        else if (StepBounds(1).Contains(e.Location)) _number.UpButton();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var p = AppTheme.Current;
        e.Graphics.Clear(Parent?.BackColor ?? p.Card);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = FluentDrawing.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), Px(5));
        using var fill = new SolidBrush(p.Field); e.Graphics.FillPath(fill, path);
        using var edge = new Pen(ContainsFocus ? p.Accent : _hover ? p.Muted : p.Border); e.Graphics.DrawPath(edge, path);
        if (ContainsFocus)
        {
            using var accent = new Pen(p.Accent, Math.Max(1, Px(2)));
            e.Graphics.DrawLine(accent, Px(5), Height - Px(1), Width - Px(5), Height - Px(1));
        }
        if (_number is null) return;
        foreach (var step in new[] { -1, 1 })
        {
            var bounds = StepBounds(step);
            var enabled = Enabled && (step < 0 ? _number.Value > _number.Minimum : _number.Value < _number.Maximum);
            if (_hoverStep == step && enabled)
            {
                using var hoverPath = FluentDrawing.Rounded(bounds, Px(3));
                using var hover = new SolidBrush(p.Hover); e.Graphics.FillPath(hover, hoverPath);
            }
            using var pen = new Pen(enabled ? p.Text : p.Disabled, Math.Max(1, DeviceDpi / 96f));
            var x = bounds.Left + bounds.Width / 2; var y = bounds.Top + bounds.Height / 2;
            e.Graphics.DrawLine(pen, x - Px(4), y, x + Px(4), y);
            if (step > 0) e.Graphics.DrawLine(pen, x, y - Px(4), x, y + Px(4));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor.Enter -= EditorStateChanged; _editor.Leave -= EditorStateChanged;
            _editor.MouseEnter -= EditorMouseEnter; _editor.MouseLeave -= EditorMouseLeave;
            _editor.FontChanged -= EditorFontChanged;
            if (_number is not null) _number.ValueChanged -= EditorStateChanged;
        }
        base.Dispose(disposing);
    }
}

internal sealed class FluentButton : Button
{
    private bool _hover;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool DropDown { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Selected { get; set; }
    public FluentButton()
    {
        FlatStyle = FlatStyle.Flat; UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p = AppTheme.Current; var s = DeviceDpi / 96f; var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? p.Window); g.SmoothingMode = SmoothingMode.AntiAlias;
        var surface = Selected ? p.AccentSoft : _hover ? p.Hover : BackColor;
        using var path = FluentDrawing.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 5 * s);
        using var fill = new SolidBrush(surface); g.FillPath(fill, path);
        using var edge = new Pen(Selected ? p.Accent : p.Border); g.DrawPath(edge, path);
        var text = Rectangle.Inflate(ClientRectangle, -(int)(9 * s), 0);
        if (DropDown) text.Width -= (int)(20 * s);
        var alignment = TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
        TextRenderer.DrawText(g, Text, Font, text, Enabled ? Selected ? p.Accent : ForeColor : p.Disabled,
            alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (DropDown) FluentIcons.Draw(g, "chevron", new RectangleF(Width - 23 * s, (Height - 16 * s) / 2, 16 * s, 16 * s), p.Muted);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4), p.Text, surface);
    }
}

internal sealed class FluentToggle : CheckBox
{
    public FluentToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleRole = AccessibleRole.CheckButton;
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p = AppTheme.Current; var s = DeviceDpi / 96f; var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? p.Card); g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(Width - 39 * s, (Height - 20 * s) / 2, 36 * s, 20 * s);
        using var path = FluentDrawing.Rounded(track, 10 * s);
        using var fill = new SolidBrush(Checked ? p.Accent : p.Field); g.FillPath(fill, path);
        using var edge = new Pen(Checked ? p.Accent : p.Muted); g.DrawPath(edge, path);
        using var knob = new SolidBrush(Checked ? p.OnAccent : p.Muted);
        g.FillEllipse(knob, track.X + (Checked ? 20 : 4) * s, track.Y + 4 * s, 12 * s, 12 * s);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Math.Max(0, Width - (int)(51 * s)), Height),
            Enabled ? p.Text : p.Disabled, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2), p.Text, p.Card);
    }
}

internal sealed class FluentSettingRow : Panel
{
    public FluentSettingRow() { DoubleBuffered = true; BackColor = AppTheme.Current.Card; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var s = DeviceDpi / 96f; var p = AppTheme.Current;
        e.Graphics.Clear(Parent?.BackColor ?? p.Window); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = FluentDrawing.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 7 * s);
        using var fill = new SolidBrush(p.Card); e.Graphics.FillPath(fill, path);
        using var border = new Pen(p.Border); e.Graphics.DrawPath(border, path);
    }
}
