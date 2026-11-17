namespace SnipLite;

/// <summary>Thin compositor windows move without repainting the frozen desktop.</summary>
internal sealed class CaptureCrosshair : IDisposable
{
    private sealed class LineWindow : Form
    {
        private readonly bool _vertical;
        public LineWindow(Color color, bool vertical)
        {
            _vertical = vertical;
            FormBorderStyle = FormBorderStyle.None; AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            BackColor = color; Opacity = .8;
        }
        protected override bool ShowWithoutActivation => true;
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ClientSize.Width < 1 || ClientSize.Height < 1) return;
            // WinForms enforces a DPI-dependent minimum top-level window
            // thickness. Clip its visible region to one physical pixel.
            var previous = Region;
            Region = new Region(new Rectangle(0, 0, _vertical ? 1 : ClientSize.Width, _vertical ? ClientSize.Height : 1));
            previous?.Dispose();
        }
        internal RectangleF InkBounds()
        {
            using var matrix = new System.Drawing.Drawing2D.Matrix();
            return Region!.GetRegionScans(matrix)[0];
        }
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            // Line thickness and screen coordinates are physical pixels.
            e.Cancel = true; base.OnDpiChanged(e);
        }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020 | 0x00080000; return cp; }
        }
    }
    private readonly LineWindow _horizontal;
    private readonly LineWindow _vertical;
    private readonly Form _owner;
    private readonly Rectangle _screen;
    internal Rectangle HorizontalBounds => _horizontal.Bounds;
    internal Rectangle VerticalBounds => _vertical.Bounds;
    internal RectangleF HorizontalInkBounds => _horizontal.InkBounds();
    internal RectangleF VerticalInkBounds => _vertical.InkBounds();
    public CaptureCrosshair(Form owner, Rectangle screen, Color color)
    {
        _owner = owner; _screen = screen;
        _horizontal = new(color, false); _vertical = new(color, true);
    }
    public void Move(Point local)
    {
        var firstShow = !_horizontal.Visible || !_vertical.Visible;
        Position(local);
        if (!_horizontal.Visible) _horizontal.Show(_owner);
        if (!_vertical.Visible) _vertical.Show(_owner);
        // Initial display can apply WinForms' DPI rounding after HWND sizing.
        if (firstShow) Position(local);
    }
    private void Position(Point local)
    {
        var batch = BeginDeferWindowPos(2);
        if (batch != IntPtr.Zero) batch = DeferWindowPos(batch, _horizontal.Handle, Win32.HWND_TOPMOST, _screen.X, _screen.Y + local.Y, _screen.Width, 1, Win32.SWP_NOACTIVATE);
        if (batch != IntPtr.Zero) batch = DeferWindowPos(batch, _vertical.Handle, Win32.HWND_TOPMOST, _screen.X + local.X, _screen.Y, 1, _screen.Height, Win32.SWP_NOACTIVATE);
        if (batch == IntPtr.Zero || !EndDeferWindowPos(batch))
        {
            Win32.SetWindowPos(_horizontal.Handle, Win32.HWND_TOPMOST, _screen.X, _screen.Y + local.Y, _screen.Width, 1, Win32.SWP_NOACTIVATE);
            Win32.SetWindowPos(_vertical.Handle, Win32.HWND_TOPMOST, _screen.X + local.X, _screen.Y, 1, _screen.Height, Win32.SWP_NOACTIVATE);
        }
    }
    public void Hide() { _horizontal.Hide(); _vertical.Hide(); }
    public void Dispose() { _horizontal.Dispose(); _vertical.Dispose(); }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr BeginDeferWindowPos(int count);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr DeferWindowPos(IntPtr batch, IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EndDeferWindowPos(IntPtr batch);
}
