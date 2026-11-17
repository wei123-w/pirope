namespace SnipLite.Localization;

/// <summary>Receive Windows language/settings broadcasts on the application's UI thread.</summary>
internal sealed class LanguageMonitor(Action changed) : NativeWindow, IDisposable
{
    public void Start() => CreateHandle(new CreateParams { Caption = "SnipLite.LanguageMonitor", ExStyle = 0x80 });
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x001A)
        {
            var previous = LanguageService.CurrentLanguage;
            LanguageService.RefreshSystemLanguage();
            if (previous != LanguageService.CurrentLanguage) changed();
        }
        base.WndProc(ref m);
    }
    public void Dispose() { if (Handle != IntPtr.Zero) DestroyHandle(); }
}
