namespace SnipLite;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);
        AppTheme.Refresh();

        if (args.Any(arg => arg.StartsWith("--startup-admin:")))
        {
            Environment.ExitCode = Startup.ConfigureAdminTask(args.Contains("--startup-admin:on"));
            return;
        }

        Localization.LanguageService.Configure(AppSettings.Load().DisplayLanguage);
        using var mutex = new Mutex(false, @"Global\PicRope.SnippetCapture");
        try
        {
            if (!mutex.WaitOne(args.Contains("--wait-for-instance") ? 5000 : 0))
            {
                MessageBox.Show(Localization.LanguageService.Format("SnipLite 已有实例正在运行，本次没有启动新版。\n\n请先从托盘右键菜单退出旧版，再启动本文件。\n\n本文件版本：{0}\n本文件位置：{1}", AppInfo.Version, Application.ExecutablePath), UiText.T("SnipLite 已在运行"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        catch (AbandonedMutexException) { }

        // A capture overlay must never die behind a modal exception dialog;
        // log it and show a passive toast instead.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);

        // --capture starts a snip as soon as the message loop runs. It gives
        // other tools (and the test scripts) a way in when F1 belongs to
        // another screenshot program. --prefs[:N] and --menu open the
        // preferences dialog (optionally on tab N) and the tray menu for the
        // same reason.
        var prefsTab = -1;

        foreach (var arg in args)
        {
            if (!arg.StartsWith("--prefs", StringComparison.OrdinalIgnoreCase)) continue;

            prefsTab = 0;
            var colon = arg.IndexOf(':');

            if (colon > 0 && int.TryParse(arg[(colon + 1)..], out var index))
                prefsTab = Math.Clamp(index, 0, 7);
        }

        var bridgeArg = args.FirstOrDefault(arg => arg.StartsWith("--picrope-bridge:", StringComparison.OrdinalIgnoreCase));
        var bridgeDirectory = bridgeArg is null ? null : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(bridgeArg.Substring("--picrope-bridge:".Length)));
        var captureMode = args.Contains("--translation") ? CaptureMode.Translation : args.Contains("--ocr") ? CaptureMode.Ocr : args.Contains("--qr") ? CaptureMode.Qr : CaptureMode.Snip;

        Application.Run(new TrayContext(
            args.Contains("--capture"),
            prefsTab,
            args.Contains("--menu"),
            bridgeDirectory,
            captureMode,
            args.Contains("--explain")));
    }

    internal static void Report(Exception? ex)
    {
        AppLog.Write("未处理异常", ex);
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "SnipLite");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] version={AppInfo.Version} exe={Application.ExecutablePath}\r\n{ex}\r\n\r\n");
        }
        catch
        {
            // logging must never throw
        }

        try
        {
            Toast.Show(UiText.T("SnipLite 出错，详情见 %TEMP%\\SnipLite\\error.log"), 4000);
        }
        catch
        {
            // nothing else we can do
        }
    }
}
