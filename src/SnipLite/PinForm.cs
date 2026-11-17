using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SnipLite;

internal sealed class PinForm : Form
{
    private static readonly List<PinForm> Open = new();
    private sealed record HistoryEntry(byte[] Png)
    {
        public Point Location { get; set; }
    }

    private static readonly List<HistoryEntry> History = new();
    private HistoryEntry? _historyEntry;
    private static int _group = 1;
    private readonly Bitmap _image;
    private readonly AppSettings _settings;
    private Point _location;
    private Size _windowSize;
    private float _scale = 1;
    private const float MinZoom = .1f;
    private const float MaxZoom = 5f;
    private readonly System.Windows.Forms.Timer _zoomBadgeTimer = new() { Interval = 1000 };
    private float MaxScale => Math.Min(MaxZoom, _settings.PinMaxSize / (float)Math.Max(_image.Width, _image.Height));
    private double _opacity;
    private bool _dragging;
    private Point _grab;
    private Point _dragStart;
    private bool _exporting;
    private bool _thumbnail;
    private bool _mouseThrough;
    private bool _suppressContext;
    private int _pinGroup;
    private int _flashTicks;
    private readonly System.Windows.Forms.Timer _flashTimer = new() { Interval = 120 };
    private PinShadowWindow? _shadow;
    private Size _shadowSize;
    private int _shadowColor;
    public Bitmap Image => _image;
    public static bool AnyVisible => Open.Any(form => form.Visible);
    public static int OpenCount => Open.Count;
    public static int RecoverableCount => History.Count(entry => !Open.Any(pin => pin._historyEntry == entry));

    public PinForm(Bitmap image, Point? screenPoint = null, int opacityPercent = 100, AppSettings? settings = null)
    {
        _image = image; _settings = settings ?? AppSettings.Load(); _pinGroup = _group;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        TopMost = true; DoubleBuffered = true; BackColor = Color.Black; KeyPreview = true; Text = UiText.T("SnipLite 贴图");
        Font = _settings.CreateInterfaceFont(); _opacity = Math.Clamp(opacityPercent, 10, 100) / 100d; Opacity = _opacity;
        var anchor = screenPoint ?? Win32.CursorPosition();
        _scale = Math.Min(1, _settings.PinMaxSize / (float)Math.Max(image.Width, image.Height));
        _windowSize = new Size(Math.Max(1, (int)(image.Width * _scale)), Math.Max(1, (int)(image.Height * _scale)));
        _location = new Point(anchor.X - _windowSize.Width / 2, anchor.Y - _windowSize.Height / 2); ClampIntoVirtualScreen();
        ContextMenuStrip = BuildMenu(); Open.Add(this);
        _flashTimer.Tick += (_, _) =>
        {
            _flashTicks--;
            Opacity = _settings.PinTranslucentFlash && _flashTicks > 0 && _flashTicks % 2 == 0 ? Math.Min(.65, _opacity) : _opacity;
            Invalidate(); if (_flashTicks <= 0) _flashTimer.Stop();
        };
        _zoomBadgeTimer.Tick += (_, _) => { _zoomBadgeTimer.Stop(); Invalidate(); };
        VisibleChanged += (_, _) =>
        {
            if (!Visible) { _shadow?.Hide(); return; }
            RecordHistory();
            if (IsHandleCreated) UpdateShadow();
        };
    }
    protected override bool ShowWithoutActivation => !_settings.ActivateNewPin;
    public static void ApplySettingsToAll()
    {
        foreach (var pin in Open.ToArray())
        {
            pin.Text = UiText.T("SnipLite 贴图");
            pin.Font = pin._settings.CreateInterfaceFont(); pin.UpdateShadow(); pin.Invalidate();
            var previous = pin.ContextMenuStrip; pin.ContextMenuStrip = pin.BuildMenu();
            if (previous is not null) SynchronizationContext.Current?.Post(_ => previous.Dispose(), null);
        }
    }
    public static void CloseAll() { foreach (var pin in Open.ToArray()) pin.Close(); }
    public static void CloseAllForExit() { CloseAll(); History.Clear(); }
    public static void HideAll() { foreach (var pin in Open.ToArray()) pin.Hide(); }
    public static void ShowAll()
    {
        foreach (var pin in Open.Where(pin => pin._pinGroup == _group).ToArray()) pin.Show();
        Open.LastOrDefault(pin => pin._pinGroup == _group)?.Activate();
    }
    public static void ToggleAll() { if (Open.Any(pin => pin.Visible)) HideAll(); else ShowAll(); }
    public static void SwitchGroup()
    {
        _group = _group % 3 + 1;
        foreach (var pin in Open.ToArray())
        {
            if (pin._pinGroup == _group) { pin.Show(); if (pin._settings.FlashOnGroupSwitch) pin.Flash(); }
            else pin.Hide();
        }
        Toast.Show(Localization.LanguageService.Format("贴图分组 {0}", _group));
    }
    public static void ToggleMouseThroughActive()
    {
        var pin = Open.LastOrDefault(pin => pin.ContainsFocus) ?? Open.LastOrDefault(pin => pin.Visible);
        if (pin is null) { Toast.Show(UiText.T("当前没有贴图")); return; }
        pin.ToggleMouseThrough();
    }
    public static bool RestoreHidden()
    {
        if (Open.Any(pin => !pin.Visible && pin._pinGroup == _group)) { ShowAll(); return true; }
        return false;
    }
    public static bool ActivateExisting()
    {
        var pin = Open.LastOrDefault(pin => pin._pinGroup == _group);
        if (pin is null) return false; ShowAll(); pin.Activate(); return true;
    }
    public static void TrimHistory(int limit)
    {
        if (History.Count > limit) History.RemoveRange(limit, History.Count - limit);
    }

    public static bool RestoreLast(AppSettings settings)
    {
        TrimHistory(settings.HistoryImageLimit);
        var entry = History.FirstOrDefault(item => !Open.Any(pin => pin._historyEntry == item));
        if (entry is null) return false;

        using var stream = new MemoryStream(entry.Png);
        using var decoded = new Bitmap(stream);
        var pin = new PinForm(new Bitmap(decoded), opacityPercent: settings.PinOpacityPercent, settings: settings)
        {
            _historyEntry = entry,
            _location = entry.Location
        };
        pin.ShowBesideExisting();
        return true;
    }

    private void RecordHistory()
    {
        if (_historyEntry is not null || _settings.HistoryImageLimit <= 0) return;
        using var stream = new MemoryStream();
        _image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        _historyEntry = new HistoryEntry(stream.ToArray()) { Location = _location };
        History.Insert(0, _historyEntry);
        TrimHistory(_settings.HistoryImageLimit);
    }

    internal void ShowBesideExisting()
    {
        var area = Screen.FromRectangle(new Rectangle(_location, _windowSize)).WorkingArea;
        var step = Math.Max(20, (int)Math.Round(24 * DeviceDpi / 96f));
        var occupied = Open.Where(pin => pin != this && pin.Visible)
            .Select(pin => pin._location).ToHashSet();
        var origin = _location;
        for (var i = 1; occupied.Any(point => Math.Abs(point.X - _location.X) < step / 2 &&
             Math.Abs(point.Y - _location.Y) < step / 2) && i <= occupied.Count * 2; i++)
        {
            var offset = ((i + 1) / 2) * step * (i % 2 == 0 ? -1 : 1);
            _location = new Point(
                Math.Clamp(origin.X + offset, area.Left, Math.Max(area.Left, area.Right - _windowSize.Width)),
                Math.Clamp(origin.Y + offset, area.Top, Math.Max(area.Top, area.Bottom - _windowSize.Height)));
        }
        Show();
    }
    public static void ResetAllScale() { foreach (var pin in Open.ToArray()) pin.Reset(); }
    public static void ResetAllOpacity() { foreach (var pin in Open.ToArray()) { pin._opacity = 1; pin.Opacity = 1; } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyLayout(); }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e); ApplyLayout();
        if (_settings.ActivateNewPin) Win32.SetForegroundWindow(Handle);
        if (_settings.FlashNewPin) Flash();
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Open.Remove(this); _flashTimer.Dispose(); _zoomBadgeTimer.Dispose(); _shadow?.Close(); _shadow?.Dispose();
        if (_historyEntry is not null) _historyEntry.Location = _location;
        _image.Dispose(); base.OnFormClosed(e);
    }
    private void Flash() { _flashTicks = 6; _flashTimer.Start(); Invalidate(); }
    private void ApplyLayout()
    {
        _scale = Math.Min(_scale, MaxScale);
        _windowSize = new Size(Math.Max(1, (int)Math.Round(_image.Width * _scale)), Math.Max(1, (int)Math.Round(_image.Height * _scale)));
        ClampIntoVirtualScreen();
        Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, _location.X, _location.Y, _windowSize.Width, _windowSize.Height, Win32.SWP_NOACTIVATE);
        UpdateShadow(); Invalidate();
    }
    private void UpdateShadow()
    {
        if (!_settings.PinShadow || !Visible) { _shadow?.Hide(); return; }
        _shadow ??= new PinShadowWindow();
        if (!_shadow.Visible) _shadow.Show(this);
        var margin = (int)(16 * DeviceDpi / 96f);
        if (_shadowSize != _windowSize || _shadowColor != _settings.PinShadowColor || _dragging == false)
        {
            _shadow.UpdateShadow(new Rectangle(_location, _windowSize), Color.FromArgb(_settings.PinShadowColor), DeviceDpi / 96f);
            _shadowSize = _windowSize; _shadowColor = _settings.PinShadowColor;
        }
        else Win32.SetWindowPos(_shadow.Handle, Win32.HWND_TOPMOST, _location.X - margin, _location.Y - margin, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }
    private void ClampIntoVirtualScreen()
    {
        var screen = SystemInformation.VirtualScreen;
        _location.X = Math.Clamp(_location.X, screen.Left, Math.Max(screen.Left, screen.Right - _windowSize.Width));
        _location.Y = Math.Clamp(_location.Y, screen.Top, Math.Max(screen.Top, screen.Bottom - _windowSize.Height));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = _settings.PinSmoothScaling ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half; g.DrawImage(_image, new Rectangle(Point.Empty, _windowSize));
        if (_zoomBadgeTimer.Enabled)
        {
            var label = $"{_scale:P0}";
            var padding = Math.Max(3, DeviceDpi / 24);
            var size = TextRenderer.MeasureText(label, Font);
            var badge = new Rectangle(padding, padding, size.Width + padding * 2, size.Height + padding);
            using var background = new SolidBrush(Color.FromArgb(220, 25, 25, 25));
            g.FillRectangle(background, badge);
            TextRenderer.DrawText(g, label, Font, badge, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        if (_flashTicks > 0 && _flashTicks % 2 == 0)
        { using var border = new Pen(_settings.Accent, Math.Max(2, 2 * DeviceDpi / 96f)); g.DrawRectangle(border, 1, 1, Math.Max(1, _windowSize.Width - 2), Math.Max(1, _windowSize.Height - 2)); }
    }
    private bool Gesture(MouseBinding binding, PointerGesture gesture) => binding.Gesture == gesture && binding.Modifiers == ModifierKeys;
    private bool HandleGesture(PointerGesture gesture)
    {
        if (Gesture(_settings.PinCloseMouse, gesture)) { Close(); return true; }
        if (Gesture(_settings.PinResetMouse, gesture)) { Reset(); return true; }
        if (Gesture(_settings.PinThumbnailMouse, gesture)) { ToggleThumbnail(); return true; }
        if (Gesture(_settings.PinCopyTextMouse, gesture)) { _ = RecognizeText(); return true; }
        return false;
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        var gesture = e.Button == MouseButtons.Middle ? PointerGesture.MiddleClick : e.Button == MouseButtons.Right ? PointerGesture.RightClick : e.Clicks >= 2 ? PointerGesture.LeftDoubleClick : PointerGesture.LeftClick;
        if (HandleGesture(gesture)) { _suppressContext = e.Button == MouseButtons.Right; return; }
        if (e.Button != MouseButtons.Left) return;
        _dragging = true; _grab = Win32.CursorPosition(); _dragStart = _grab; Capture = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging || _exporting) return;
        var now = Win32.CursorPosition();
        if (ModifierKeys.HasFlag(Keys.Alt) && Math.Abs(now.X - _dragStart.X) + Math.Abs(now.Y - _dragStart.Y) > 6)
        {
            _exporting = true; _dragging = false; Capture = false;
            try
            {
                var folder = Path.Combine(Path.GetTempPath(), "SnipLite", "drag"); Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png"); _image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                var data = new DataObject(DataFormats.FileDrop, new[] { path });
                var effect = DoDragDrop(data, DragDropEffects.Copy);
                if (effect != DragDropEffects.None && !_settings.KeepOriginalOnDrag) Close();
            }
            finally { _exporting = false; }
            return;
        }
        _location.Offset(now.X - _grab.X, now.Y - _grab.Y); _grab = now; ApplyLayout();
    }
    protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; Capture = false; UpdateShadow(); }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x007B && _suppressContext) { _suppressContext = false; return; }
        base.WndProc(ref m);
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (ModifierKeys == _settings.PinOpacityModifiers) { AdjustOpacity(Math.Sign(e.Delta)); return; }
        if (_settings.PinWheelZoom && ModifierKeys == _settings.PinZoomModifiers) Zoom(e.Delta > 0 ? 1.1f : 1 / 1.1f);
    }
    private void AdjustOpacity(int direction) { _opacity = Math.Clamp(_opacity + direction * .1, .1, 1); Opacity = _opacity; }
    private void Zoom(float factor)
    {
        var previous = _scale; var anchor = _settings.PinZoomAtCursor ? Win32.CursorPosition() : new Point(_location.X + _windowSize.Width / 2, _location.Y + _windowSize.Height / 2);
        var max = MaxScale;
        _scale = Math.Clamp(_scale * factor, Math.Min(MinZoom, max), max);
        _location = new Point((int)Math.Round(anchor.X - (anchor.X - _location.X) * _scale / previous), (int)Math.Round(anchor.Y - (anchor.Y - _location.Y) * _scale / previous));
        _thumbnail = false;
        _zoomBadgeTimer.Stop();
        _zoomBadgeTimer.Start();
        ApplyLayout();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { Close(); return; }
        if (e.Control && e.KeyCode == Keys.C) { Copy(); return; }
        if (e.Control && e.KeyCode == Keys.S) { SaveAs(); return; }
        if (e.Control && e.KeyCode == Keys.D0) { Reset(); return; }
        if (e.KeyCode is Keys.Add or Keys.Oemplus or Keys.Subtract or Keys.OemMinus)
        {
            var direction = e.KeyCode is Keys.Add or Keys.Oemplus ? 1 : -1;
            if (e.Modifiers == _settings.PinOpacityModifiers) AdjustOpacity(direction);
            else if (e.Modifiers == _settings.PinZoomModifiers) Zoom(direction > 0 ? 1.1f : 1 / 1.1f);
            return;
        }
        base.OnKeyDown(e);
    }
    private void Reset() { if (_thumbnail && !_settings.ThumbnailAcceptReset) return; _thumbnail = false; _scale = 1; ApplyLayout(); }
    private void ToggleThumbnail()
    {
        _thumbnail = !_thumbnail;
        _scale = _thumbnail ? Math.Min(_settings.ThumbnailWidth / (float)_image.Width, _settings.ThumbnailHeight / (float)_image.Height) : 1;
        ApplyLayout();
    }
    private void ToggleMouseThrough()
    {
        _mouseThrough = !_mouseThrough;
        var style = GetWindowLongPtr(Handle, -20).ToInt64();
        SetWindowLongPtr(Handle, -20, new IntPtr(_mouseThrough ? style | 0x20 : style & ~0x20));
        Toast.Show(_mouseThrough ? UiText.T("贴图鼠标穿透已启用，可用全局快捷键关闭") : UiText.T("贴图鼠标穿透已关闭"));
    }
    private void Copy()
    {
        try { Services.CopyImage(_image, _settings.PinBitmapClipboard); Toast.Show(UiText.T("图像已复制")); }
        catch (Exception ex) { Toast.Error(UiText.T("复制失败：") + ex.Message); }
    }
    private void SaveAs()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = UiText.T("PNG 图片|*.png|JPEG 图片|*.jpg"), FileName = Services.ImageFileName(_settings, _settings.RememberImageExtension ? _settings.LastImageExtension : ".png"),
            FilterIndex = _settings.RememberImageExtension && _settings.LastImageExtension != ".png" ? 2 : 1
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Services.SaveImage(_image, dialog.FileName, dialog.FilterIndex == 2, _settings.JpegQuality);
            if (_settings.RememberImageExtension) { _settings.LastImageExtension = dialog.FilterIndex == 2 ? ".jpg" : ".png"; _settings.Save(); }
            Toast.Show(UiText.T("已保存：") + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) { Toast.Error(UiText.T("保存失败：") + ex.Message); }
    }
    private async Task RecognizeText()
    {
        using var snapshot = new Bitmap(_image);
        string text;
        try { text = (await Task.Run(() => Services.RecognizeTextAsync(snapshot))).Text; }
        catch (Exception ex) { Toast.Error(UiText.T("识别失败：") + ex.Message); return; }
        if (string.IsNullOrWhiteSpace(text)) { Toast.Show(UiText.T("没有识别到文字")); return; }
        try { Clipboard.SetText(text); Toast.Show(UiText.T("文字已复制")); } catch { Toast.Error(UiText.T("复制失败，请重试")); }
    }
    private async void ScanQr()
    {
        using var snapshot = new Bitmap(_image); var text = await Task.Run(() => Services.DecodeQr(snapshot));
        if (IsDisposed) return;
        if (string.IsNullOrEmpty(text)) { Toast.Show(UiText.T("没有检测到二维码 / 条码")); return; }
        using var dialog = new RecognitionResultForm(text); dialog.ShowDialog(this);
    }
    private ContextMenuStrip BuildMenu()
    {
        var menu = SnipMenu.Create(); menu.Font = _settings.CreateInterfaceFont();
        ToolStripMenuItem Item(string text, Action action, string key = "") => SnipMenu.Item(text, action, _settings.PinMenuShortcuts ? key : "");
        menu.Items.Add(Item("复制图像", Copy, "Ctrl+C")); menu.Items.Add(Item("另存为…", SaveAs, "Ctrl+S"));
        menu.Items.Add(Item("识别文字", () => _ = RecognizeText())); menu.Items.Add(Item("识别二维码", ScanQr));
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(Item("原始大小", Reset, "Ctrl+0"));
        menu.Items.Add(Item("快速缩略图", ToggleThumbnail)); menu.Items.Add(Item("鼠标穿透开关", ToggleMouseThrough));
        var groups = new ToolStripMenuItem(UiText.T("移动到分组"));
        for (var i = 1; i <= 3; i++) { var group = i; groups.DropDownItems.Add(Item(Localization.LanguageService.Format("分组 {0}", group), () => { _pinGroup = group; if (_pinGroup != _group) Hide(); })); }
        menu.Items.Add(groups); menu.Items.Add(Item("关闭", Close, "Esc")); menu.Items.Add(Item("关闭全部贴图", CloseAll));
        menu.Items.Add(Item("隐藏 / 显示所有贴图", ToggleAll)); menu.Items.Add(Item("恢复 100% 不透明度", () => { _opacity = 1; Opacity = 1; }));
        UiText.Apply(menu, _settings.DisplayLanguage); return menu;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
