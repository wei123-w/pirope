using System.Runtime.InteropServices;

namespace SnipLite;

/// <summary>Message-only window that owns the global hotkeys.</summary>
internal sealed class HotkeyHost : NativeWindow, IDisposable
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    private const int WM_HOTKEY = 0x0312;

    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public HotkeyHost()
    {
        CreateHandle(new CreateParams
        {
            Caption = "SnipLite.HotkeyHost",
            Parent = new IntPtr(-3) // HWND_MESSAGE
        });
    }

    private static uint Modifiers(Keys binding)
    {
        uint modifiers = 0;
        if (binding.HasFlag(Keys.Control)) modifiers |= MOD_CONTROL;
        if (binding.HasFlag(Keys.Alt)) modifiers |= MOD_ALT;
        if (binding.HasFlag(Keys.Shift)) modifiers |= MOD_SHIFT;
        if (binding.HasFlag(HotkeyDefinitions.WindowsModifier)) modifiers |= MOD_WIN;
        return modifiers;
    }

    public bool CanRegister(Keys binding)
    {
        var id = _nextId++;
        if (!RegisterHotKey(Handle, id, Modifiers(binding) | MOD_NOREPEAT, (uint)(binding & Keys.KeyCode)))
            return false;
        UnregisterHotKey(Handle, id);
        return true;
    }

    public bool Register(Keys binding, Action handler) =>
        Register(Modifiers(binding), binding & Keys.KeyCode, handler);

    public bool Register(uint modifiers, Keys key, Action handler)
    {
        var id = _nextId++;
        if (!RegisterHotKey(Handle, id, modifiers | MOD_NOREPEAT, (uint)key))
            return false;

        _handlers[id] = handler;
        return true;
    }

    /// <summary>Drop every registration but keep the window alive, so "禁用快捷键" can be undone.</summary>
    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys)
            UnregisterHotKey(Handle, id);

        _handlers.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _handlers.TryGetValue(m.WParam.ToInt32(), out var handler))
        {
            try { handler(); } catch { /* never let a hotkey kill the loop */ }
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys)
            UnregisterHotKey(Handle, id);

        _handlers.Clear();

        if (Handle != IntPtr.Zero)
            DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
