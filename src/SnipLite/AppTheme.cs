using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace SnipLite;

internal sealed record ThemePalette(bool Dark, bool TaskbarDark, bool HighContrast = false)
{
    public Color Window => HighContrast ? SystemColors.Control : Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(247, 247, 249);
    public Color Sidebar => HighContrast ? SystemColors.Control : Dark ? Color.FromArgb(27, 27, 29) : Color.FromArgb(240, 241, 244);
    public Color Card => HighContrast ? SystemColors.Window : Dark ? Color.FromArgb(43, 43, 46) : Color.White;
    public Color Field => HighContrast ? SystemColors.Window : Dark ? Color.FromArgb(51, 51, 55) : Color.FromArgb(253, 253, 253);
    public Color Text => HighContrast ? SystemColors.WindowText : Dark ? Color.FromArgb(245, 245, 245) : Color.FromArgb(32, 32, 34);
    public Color Muted => HighContrast ? SystemColors.WindowText : Dark ? Color.FromArgb(184, 184, 190) : Color.FromArgb(100, 100, 108);
    public Color Border => HighContrast ? SystemColors.WindowText : Dark ? Color.FromArgb(65, 65, 70) : Color.FromArgb(225, 225, 231);
    public Color Hover => HighContrast ? SystemColors.Highlight : Dark ? Color.FromArgb(58, 58, 63) : Color.FromArgb(232, 233, 237);
    public Color Accent => HighContrast ? SystemColors.Highlight : Dark ? Color.FromArgb(117, 185, 255) : Color.FromArgb(0, 103, 192);
    public Color AccentSoft => HighContrast ? SystemColors.Highlight : Dark ? Color.FromArgb(43, 60, 78) : Color.FromArgb(229, 240, 251);
    public Color OnAccent => HighContrast ? SystemColors.HighlightText : Dark ? Color.FromArgb(12, 33, 52) : Color.White;
    public Color Disabled => HighContrast ? SystemColors.GrayText : Dark ? Color.FromArgb(126, 126, 134) : Color.FromArgb(156, 156, 164);
}

internal static class AppTheme
{
    private static ThemePalette? _override;
    public static ThemePalette Current { get; private set; } = ReadSystem();
    public static event Action? Changed;

    public static Color AdaptAccent(Color color)
    {
        if (Current.HighContrast) return SystemColors.Highlight;
        if (!Current.Dark || color.GetBrightness() >= .62f) return color;
        return Color.FromArgb(color.R + (255 - color.R) / 2, color.G + (255 - color.G) / 2, color.B + (255 - color.B) / 2);
    }

    internal static ThemePalette ReadSystem()
    {
        var dark = false;
        var taskbarDark = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int app && app == 0;
            taskbarDark = key?.GetValue("SystemUsesLightTheme") is int system && system == 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        return new ThemePalette(dark, taskbarDark, SystemInformation.HighContrast);
    }

    public static void Refresh()
    {
        var next = _override ?? ReadSystem();
        if (Current == next) return;
        Current = next;
        // WinForms does not automatically re-evaluate System color mode at runtime.
        Application.SetColorMode(next.Dark && !next.HighContrast ? SystemColorMode.Dark : SystemColorMode.Classic);
        Changed?.Invoke();
    }

    // Diagnostics exercise both modes without changing the user's Windows preferences.
    internal static IDisposable Override(bool dark, bool? taskbarDark = null, bool highContrast = false)
    {
        var previous = _override;
        _override = new ThemePalette(dark, taskbarDark ?? dark, highContrast);
        Refresh();
        return new RestoreTheme(() => { _override = previous; Refresh(); });
    }

    private sealed class RestoreTheme(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }

    public static void Apply(Control control)
    {
        var p = Current;
        var swatch = control.Tag as string == "color-swatch";
        if (!swatch) control.ForeColor = p.Text;
        if (!swatch) control.BackColor = control switch
        {
            FluentSettingRow => p.Card,
            FluentInputFrame => p.Field,
            TextBoxBase or UpDownBase => p.Field,
            Button => p.Field,
            Label => Color.Transparent,
            _ when control.Parent is FluentInputFrame => p.Field,
            _ when control.Parent is FluentSettingRow => p.Card,
            _ => p.Window
        };
        if (control is LinkLabel link) link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = p.Accent;
        if (control is Button button)
        {
            button.FlatAppearance.BorderColor = p.Border;
            button.FlatAppearance.MouseOverBackColor = p.Hover;
            button.FlatAppearance.MouseDownBackColor = p.AccentSoft;
        }
        if (control.IsHandleCreated && control is TextBoxBase or UpDownBase or ScrollableControl)
            SetWindowTheme(control.Handle, p.Dark && !p.HighContrast ? "DarkMode_Explorer" : "Explorer", null);
        foreach (Control child in control.Controls) Apply(child);
        control.Invalidate();
    }

    public static void ApplyWindow(Form form)
    {
        if (!form.IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        var dark = Current.Dark && !Current.HighContrast ? 1 : 0;
        DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var round = 2;
            DwmSetWindowAttribute(form.Handle, 33, ref round, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr window, string? subApp, string? subId);
}

/// <summary>A hidden top-level window receives theme broadcasts even while only the tray is visible.</summary>
internal sealed class ThemeMonitor : NativeWindow, IDisposable
{
    public ThemeMonitor() => CreateHandle(new CreateParams { Caption = "SnipLite.ThemeMonitor", ExStyle = 0x80 });
    protected override void WndProc(ref Message m)
    {
        if (m.Msg is 0x001A or 0x031A or 0x0015) AppTheme.Refresh(); // settings, theme, system colors
        base.WndProc(ref m);
    }
    public void Dispose() { if (Handle != IntPtr.Zero) DestroyHandle(); }
}
