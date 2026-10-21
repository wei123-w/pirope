using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("PicRope · 图绳")]
[assembly: AssemblyProduct("PicRope")]
[assembly: AssemblyDescription("图绳：把截图挂在屏幕上沿，随手复制、拖拽和保存。")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]

namespace ScreenLine
{
    internal sealed class DesktopController : IDisposable
    {
        public readonly Gallery Gallery;
        public readonly CapturePipeline Captures;
        public readonly TextCollection Texts;
        public readonly ShelfWindow Shelf;
        readonly WindowsEvents events;
        readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
        LightNotice notice;
        readonly CheckBox collect;
        readonly CheckBox collectText;
        readonly CheckBox autoHang;
        internal readonly TrayPanel TrayMenu;
        internal AppearanceWindow Appearance { get; private set; }
        internal SettingsWindow Settings { get; private set; }
        internal WebCaptureWindow WebCapture { get; private set; }
        internal RecentCapturesWindow RecentCaptures { get; private set; }
        internal BasketWindow Basket { get; private set; }
        internal TextWindow TextWindow { get; private set; }
        internal readonly List<ImageEditorWindow> Editors = new List<ImageEditorWindow>();
        readonly AppUpdates updates = new AppUpdates();
        readonly CancellationTokenSource updateLifetime = new CancellationTokenSource();
        readonly DispatcherTimer updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        readonly MenuItem updateMenu;
        MenuItem startupMenu;
        readonly bool automaticUpdates;
        UpdateRelease availableUpdate;
        UpdateWindow updateWindow;
        bool checkingUpdates, disposed;
        IntPtr previousForegroundWindow;
        uint textClipboardSequence;
        int textClipboardVersion;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public DesktopController(StateStore store, string screenshots, bool automaticUpdates = true)
        {
            this.automaticUpdates = automaticUpdates;
            Gallery = new Gallery(store);
            Texts = new TextCollection(store);
            Captures = new CapturePipeline(Gallery, Application.Current.Dispatcher, screenshots);
            Captures.Captured += RecordSystemCapture;
            Shelf = new ShelfWindow(Gallery, Act, EnableClipboard);
            Shelf.BasketRequested += ShowBasket;
            Shelf.TextRequested += ShowTexts;
            Texts.Changed += Shelf.UpdateTexts;
            events = new WindowsEvents(store.State);
            events.ToggleRequested += Shelf.Toggle;
            events.HotkeyRequested += HandleHotkey;
            events.ExitRequested += Exit;
            events.ClipboardChanged += Captures.ClipboardChanged;
            textClipboardSequence = WindowsNative.GetClipboardSequenceNumber();
            events.ClipboardChanged += CollectClipboardText;
            Captures.Arrived += Shelf.ShowNewCapture;
            Captures.Failed += Notice;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PicRope.Icon"))
                tray.Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            tray.Text = "PicRope · 图绳";
            TrayMenu = new TrayPanel();
            TrayMenu.OnAction(TrayMenu.VisibilityButton, delegate { Shelf.Toggle(); });
            if (events.ShortcutAvailable) TrayMenu.Shortcut.Text = store.State.ToggleShortcut;
            else Notice("Ctrl+Alt+T 已被占用，可点击托盘图标打开。");
            if (!string.IsNullOrEmpty(events.InitialShortcutError))
            {
                try { store.Save(); } catch (Exception error) { store.Log(error); }
                Notice("检测到快捷键配置冲突，已恢复默认快捷键。" + events.InitialShortcutError);
            }
            TrayMenu.OnAction(TrayMenu.BasketButton, ShowBasket);
            TrayMenu.OnAction(TrayMenu.TextButton, ShowTexts);
            autoHang = TrayMenu.AutoHang;
            TrayMenu.OnToggle(autoHang, Captures.AutoHang, delegate(bool value) { if (Captures.AutoHang != value) Captures.AutoHang = value; });
            Gallery.Changed += SyncAutoHang;
            collect = TrayMenu.Pictures;
            TrayMenu.OnToggle(collect, Captures.CollectClipboard, delegate(bool value) { Captures.CollectClipboard = value; Shelf.Update(); });
            collectText = TrayMenu.Text;
            TrayMenu.OnToggle(collectText, Texts.CollectClipboard, delegate(bool value)
            {
                textClipboardVersion++;
                textClipboardSequence = WindowsNative.GetClipboardSequenceNumber();
                Texts.CollectClipboard = value;
            });
            TrayMenu.OnAction(TrayMenu.AppearanceButton, ShowAppearance);
            var more = TrayMenu.MoreMenu;
            more.AddAction("框选截图…", "picture", delegate { CaptureScreenshot(0); });
            more.AddAction("连续截图拼接…", "add", delegate { CaptureScreenshot(4); });
            more.AddAction("全屏截图…", "show", delegate { CaptureScreenshot(1); });
            more.AddAction("活动窗口截图…", "open", delegate { CaptureScreenshot(2, previousForegroundWindow); });
            more.AddAction("延时截图…", "save", delegate { CaptureScreenshot(3); });
            more.AddAction("网页整页截图…", "open", OpenWebCapture);
            more.AddAction("最近截图…", "text", ShowRecentCaptures);
            more.AddAction("使用截图预设…", "more", ShowWorkflowPresets);
            more.Separator();
            more.AddAction("添加图片…", "add", async delegate
            {
                var picker = new OpenFileDialog { Title = "添加图片", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", Multiselect = true };
                if (picker.ShowDialog() != true) return;
                foreach (string file in picker.FileNames)
                    if (!await Captures.Import(file, null, true)) Notice("无法读取图片：" + Path.GetFileName(file));
            });
            more.AddAction("打开收集文件夹", "folder", delegate
            {
                try { WindowsShell.OpenInbox(store); }
                catch (Exception error) { store.Log(error); Notice("无法打开收集文件夹：" + error.Message); }
            });
            more.AddAction("全部取下（保留文件）", "clear", delegate { Gallery.Clear(); });
            more.Separator();
            more.AddAction("设置…", "appearance", ShowSettings);
            startupMenu = more.AddAction("开机启动", "startup", delegate
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (startupMenu.IsChecked) key.SetValue("PicRope", Quote(Assembly.GetEntryAssembly().Location));
                    else key.DeleteValue("PicRope", false);
                }
                Gallery.Store.State.LaunchOnStartup = startupMenu.IsChecked;
                Gallery.Store.Save();
            });
            startupMenu.IsCheckable = true;
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) startupMenu.IsChecked = key != null && key.GetValue("PicRope") != null;
            Gallery.Store.State.LaunchOnStartup = startupMenu.IsChecked;
            updateMenu = more.AddAction("检查更新…", "save", async delegate { await CheckForUpdates(true); });
            updateMenu.InputGestureText = "v" + AppUpdates.CurrentVersion;
            updateTimer.Tick += async delegate { updateTimer.Interval = TimeSpan.FromHours(12); await CheckForUpdates(false); };
            tray.BalloonTipClicked += delegate { ShowUpdate(); };
            TrayMenu.OnAction(TrayMenu.ExitButton, Exit);
            tray.MouseUp += delegate(object sender, Forms.MouseEventArgs args) { if (args.Button == Forms.MouseButtons.Right) ShowTrayMenu(); };
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs args) { if (args.Button == Forms.MouseButtons.Left) Shelf.Toggle(); };
            tray.Visible = true;
        }

        async void Exit()
        {
            foreach (var editor in Editors.ToArray()) if (!await editor.ConfirmClose()) return;
            Application.Current.Shutdown();
        }

        public async Task Start()
        {
            Shelf.Show();
            if (Gallery.Store.State.RevealOnStartup) Shelf.Reveal(true, false, 4);
            await Captures.Start();
            if (automaticUpdates) updateTimer.Start();
        }

        async Task CheckForUpdates(bool manual)
        {
            if (disposed || checkingUpdates) return;
            if (manual && availableUpdate != null) { ShowUpdate(); return; }
            checkingUpdates = true; updateMenu.IsEnabled = false; updateMenu.Header = "正在检查更新…";
            try
            {
                availableUpdate = await updates.CheckAsync(updateLifetime.Token);
                if (disposed) return;
                if (availableUpdate == null) { if (manual) Notice("当前已是最新版 " + AppUpdates.CurrentVersion + "。"); }
                else if (manual) ShowUpdate();
                else if (Gallery.Store.State.LastUpdateNotification != availableUpdate.Version)
                {
                    tray.ShowBalloonTip(10000, "图绳有新版本 " + availableUpdate.Version, "点击查看更新并下载安装。", Forms.ToolTipIcon.Info);
                    Gallery.Store.State.LastUpdateNotification = availableUpdate.Version; Gallery.Store.Save();
                }
            }
            catch (Exception error)
            {
                if (!disposed && manual) Notice("暂时无法检查更新，请检查网络后重试。" + (error is System.IO.InvalidDataException ? "\n" + error.Message : ""));
            }
            finally
            {
                checkingUpdates = false; updateMenu.IsEnabled = true;
                updateMenu.Header = availableUpdate == null ? "检查更新…" : "更新到 " + availableUpdate.Version + "…";
            }
        }

        void ShowUpdate()
        {
            if (disposed || availableUpdate == null) return;
            if (updateWindow != null) { updateWindow.Activate(); return; }
            updateWindow = new UpdateWindow(availableUpdate, updates, async delegate(string work)
            {
                foreach (var editor in Editors.ToArray()) if (!await editor.ConfirmClose()) return false;
                AppUpdates.Install(work);
                Application.Current.Shutdown();
                return true;
            });
            updateWindow.Closed += delegate { updateWindow = null; };
            updateWindow.Show();
        }

        void EnableClipboard()
        {
            Captures.CollectClipboard = true;
            collect.IsChecked = true;
        }

        void SyncAutoHang() { autoHang.IsChecked = Captures.AutoHang; }

        async void CollectClipboardText()
        {
            uint sequence = WindowsNative.GetClipboardSequenceNumber();
            if (disposed || !Texts.CollectClipboard || sequence == textClipboardSequence) return;
            textClipboardSequence = sequence;
            int version = textClipboardVersion;
            try
            {
                string content = await WindowsClipboard.ReadText();
                if (disposed || !Texts.CollectClipboard || version != textClipboardVersion || sequence != WindowsNative.GetClipboardSequenceNumber()) return;
                Texts.Add(content);
            }
            catch (Exception error) { if (!disposed) Gallery.Store.Log(error); }
        }

        internal void ShowBasket()
        {
            if (Basket != null) { Basket.Activate(); return; }
            Gallery.Prune();
            Basket = new BasketWindow(Gallery, delegate(bool value) { Captures.AutoHang = value; }, Captures.Hang, Act);
            Basket.Owner = Shelf;
            Shelf.ChoosingBasket = true;
            Shelf.Reveal(true, false, 0);
            Basket.Closed += delegate { Basket = null; Shelf.ChoosingBasket = false; };
            Basket.Show();
        }

        internal void ShowTexts()
        {
            if (TextWindow != null) { TextWindow.Close(); return; }
            Shelf.Reveal(true, false, 0);
            Shelf.ChoosingTexts = true;
            TextWindow = new TextWindow(Texts, Shelf);
            TextWindow.Closed += delegate { TextWindow = null; Shelf.ChoosingTexts = false; };
            TextWindow.Show();
        }

        void ShowAppearance()
        {
            if (Appearance != null) { Appearance.Activate(); return; }
            Appearance = new AppearanceWindow(Shelf.Appearance, Gallery, delegate(LineTheme theme)
            {
                Gallery.Store.State.Theme = theme.Id;
                Gallery.Store.Save();
                Shelf.ApplyTheme(theme);
                Shelf.Reveal(true, false, 3);
            });
            Appearance.Owner = Shelf;
            Shelf.ChoosingAppearance = true;
            Shelf.Reveal(true, false, 0);
            Appearance.Closed += delegate { Appearance = null; Shelf.ChoosingAppearance = false; };
            Appearance.Show();
        }

        internal void ShowSettings()
        {
            if (Settings != null) { Settings.Activate(); return; }
            Settings = new SettingsWindow(Gallery.Store.State, SaveSettings);
            Settings.Owner = Shelf;
            Shelf.Reveal(true, false, 0);
            Settings.Closed += delegate { Settings = null; };
            Settings.Show();
        }

        void SaveSettings(SavedState draft)
        {
            string error;
            if (!events.ApplyShortcuts(draft, out error)) throw new InvalidOperationException(error);
            var state = Gallery.Store.State;
            var previous = SettingsWindow.Copy(state);
            state.ToggleShortcut = draft.ToggleShortcut; state.RegionShortcut = draft.RegionShortcut; state.FullScreenShortcut = draft.FullScreenShortcut;
            state.WindowShortcut = draft.WindowShortcut; state.DelayShortcut = draft.DelayShortcut;
            state.ScreenshotFolder = draft.ScreenshotFolder; state.ScreenshotFormat = draft.ScreenshotFormat;
            state.DelaySeconds = draft.DelaySeconds; state.ScreenshotAction = draft.ScreenshotAction; state.RevealOnStartup = draft.RevealOnStartup;
            state.LaunchOnStartup = draft.LaunchOnStartup;
            state.WorkflowPresets = draft.WorkflowPresets;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (state.LaunchOnStartup) key.SetValue("PicRope", Quote(Assembly.GetEntryAssembly().Location));
                    else key.DeleteValue("PicRope", false);
                }
                Gallery.Store.Save();
            }
            catch
            {
                state.ToggleShortcut = previous.ToggleShortcut; state.RegionShortcut = previous.RegionShortcut; state.FullScreenShortcut = previous.FullScreenShortcut;
                state.WindowShortcut = previous.WindowShortcut; state.DelayShortcut = previous.DelayShortcut;
                state.ScreenshotFolder = previous.ScreenshotFolder; state.ScreenshotFormat = previous.ScreenshotFormat;
                state.DelaySeconds = previous.DelaySeconds; state.ScreenshotAction = previous.ScreenshotAction;
                state.RevealOnStartup = previous.RevealOnStartup; state.LaunchOnStartup = previous.LaunchOnStartup;
                state.WorkflowPresets = previous.WorkflowPresets;
                string ignored; events.ApplyShortcuts(previous, out ignored);
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (previous.LaunchOnStartup) key.SetValue("PicRope", Quote(Assembly.GetEntryAssembly().Location));
                        else key.DeleteValue("PicRope", false);
                    }
                }
                catch { }
                throw;
            }
            startupMenu.IsChecked = state.LaunchOnStartup;
            TrayMenu.Shortcut.Text = state.ToggleShortcut;
        }

        void HandleHotkey(int id)
        {
            if (id == 1) return;
            if (id == 2) CaptureScreenshot(0);
            else if (id == 3) CaptureScreenshot(1);
            else if (id == 4) CaptureScreenshot(2, WindowsNative.GetForegroundWindow());
            else if (id == 5) CaptureScreenshot(3);
        }

        async void CaptureScreenshot(int mode, IntPtr targetWindow = default(IntPtr), string actionOverride = null, int? delayOverride = null)
        {
            try
            {
                if (mode == 3)
                {
                    int selectedMode;
                    if (!CaptureDialog.Delay(Shelf, delayOverride ?? Gallery.Store.State.DelaySeconds, out selectedMode)) return;
                    mode = selectedMode == 0 ? 1 : selectedMode == 1 ? 0 : 2;
                }
                bool allDisplays = false;
                if (mode == 1 && !CaptureDialog.ChooseFullScreenTarget(Shelf, out allDisplays)) return;
                using (var bitmap = mode == 0 ? ScreenCapture.Region() : mode == 4 ? ScreenCapture.Continuous() : mode == 2 ? ScreenCapture.ActiveWindow(targetWindow == IntPtr.Zero ? WindowsNative.GetForegroundWindow() : targetWindow) : ScreenCapture.FullScreen(allDisplays))
                {
                    if (bitmap == null) return;
                    string modeName = mode == 0 ? "框选截图" : mode == 4 ? "连续截图拼接" : mode == 2 ? "活动窗口截图" : allDisplays ? "全部屏幕截图" : "全屏截图";
                    await ProcessCapturedFile(SaveCapture(bitmap), modeName, actionOverride);
                }
            }
            catch (Exception error) { Gallery.Store.Log(error); Notice("截图失败：" + error.Message); }
        }

        string SaveCapture(System.Drawing.Bitmap bitmap)
        {
            string folder = Gallery.Store.State.ScreenshotFolder;
            if (string.IsNullOrWhiteSpace(folder)) folder = WindowsNative.ScreenshotFolder();
            Directory.CreateDirectory(folder);
            string format = Gallery.Store.State.ScreenshotFormat ?? "PNG";
            string extension = format == "JPG" ? ".jpg" : format == "BMP" ? ".bmp" : ".png";
            System.Drawing.Imaging.ImageFormat encoder = format == "JPG" ? System.Drawing.Imaging.ImageFormat.Jpeg : format == "BMP" ? System.Drawing.Imaging.ImageFormat.Bmp : System.Drawing.Imaging.ImageFormat.Png;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                string path = Path.Combine(folder, "图绳截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + (attempt == 0 ? "" : "_" + attempt) + extension);
                try { using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) bitmap.Save(stream, encoder); return path; }
                catch (IOException) { if (!File.Exists(path)) throw; }
            }
            throw new IOException("无法生成不重名的截图文件名。");
        }

        void OpenWebCapture()
        {
            if (WebCapture != null) { WebCapture.Activate(); return; }
            WebCapture = new WebCaptureWindow(Gallery.Store, async delegate(byte[] png)
            {
                try
                {
                    using (var memory = new MemoryStream(png))
                    using (var bitmap = new System.Drawing.Bitmap(memory))
                        await ProcessCapturedFile(SaveCapture(bitmap), "网页整页截图");
                }
                catch (Exception error) { Gallery.Store.Log(error); Notice("网页截图保存失败：" + error.Message); }
            });
            WebCapture.Owner = Shelf;
            WebCapture.Closed += delegate { WebCapture = null; };
            WebCapture.Show();
        }

        async Task ProcessCapturedFile(string path, string mode = "截图", string actionOverride = null)
        {
            bool imported = await Captures.Import(path, CaptureSource.Folder, true);
            if (!imported) { Notice("截图已保留，但无法加入图绳：" + path); return; }
            RecordCapture(path, mode);
            var item = Gallery.Items.Concat(Gallery.Collected).FirstOrDefault(candidate => FileRules.SameFile(candidate.Path, path));
            if (item == null) return;
            string action = actionOverride ?? Gallery.Store.State.ScreenshotAction;
            if (action == "Copy") Act(ShotAction.Copy, item);
            else if (action == "Edit") Act(ShotAction.Edit, item);
            else if (action == "Hang") Captures.Hang(item);
            else if (action == "Basket") ShowBasket();
        }

        void RecordSystemCapture(string path, CaptureSource source)
        {
            RecordCapture(path, source == CaptureSource.Clipboard ? "剪贴板截图" : "系统截图");
        }

        void RecordCapture(string path, string mode)
        {
            try
            {
            Gallery.Store.State.RecentCaptures.RemoveAll(record => FileRules.SameFile(record.Path, path));
            Gallery.Store.State.RecentCaptures.Insert(0, new CaptureHistoryItem { Path = path, Mode = mode, CreatedUtc = DateTime.UtcNow });
            if (Gallery.Store.State.RecentCaptures.Count > 200) Gallery.Store.State.RecentCaptures.RemoveRange(200, Gallery.Store.State.RecentCaptures.Count - 200);
            Gallery.Store.Save();
            }
            catch (Exception error) { Gallery.Store.Log(error); Notice("截图已保存，但历史记录保存失败：" + error.Message); }
        }

        void ShowRecentCaptures()
        {
            if (RecentCaptures != null) { RecentCaptures.Activate(); return; }
            RecentCaptures = new RecentCapturesWindow(Gallery.Store, async delegate(string action, CaptureHistoryItem record)
            {
                try
                {
                    if (!File.Exists(record.Path)) { Notice("截图文件已不存在；记录仍保留，可手动移除。"); return; }
                    if (action == "open") Process.Start(new ProcessStartInfo(record.Path) { UseShellExecute = true });
                    else
                    {
                        var item = Gallery.Items.Concat(Gallery.Collected).FirstOrDefault(candidate => FileRules.SameFile(candidate.Path, record.Path));
                        if (item == null)
                        {
                            await Captures.Import(record.Path, null, false);
                            item = Gallery.Items.Concat(Gallery.Collected).FirstOrDefault(candidate => FileRules.SameFile(candidate.Path, record.Path));
                        }
                        if (item != null) Act(ShotAction.Copy, item);
                    }
                }
                catch (Exception error) { Gallery.Store.Log(error); Notice("操作未完成：" + error.Message); }
            });
            RecentCaptures.Owner = Shelf;
            RecentCaptures.Closed += delegate { RecentCaptures = null; };
            RecentCaptures.Show();
        }

        void ShowWorkflowPresets()
        {
            var presets = Gallery.Store.State.WorkflowPresets.ToArray();
            if (presets.Length == 0) { Notice("还没有截图流程预设。可在“设置”→“截图”中创建。 "); return; }
            var window = new CaptureWorkflowWindow(presets, delegate(CaptureWorkflowPreset preset)
            {
                int mode = preset.Mode == "Region" ? 0 : preset.Mode == "FullScreen" ? 1 : preset.Mode == "Window" ? 2 : preset.Mode == "Delay" ? 3 : preset.Mode == "Continuous" ? 4 : 0;
                CaptureScreenshot(mode, IntPtr.Zero, preset.Action, preset.DelaySeconds);
            });
            window.Owner = Shelf; window.ShowDialog();
        }

        internal void ShowTrayMenu()
        {
            previousForegroundWindow = WindowsNative.GetForegroundWindow();
            WindowsNative.SetForegroundWindow(Shelf.Handle);
            var cursor = WindowsNative.Cursor();
            var point = Shelf.PointFromScreen(new Point(cursor.X, cursor.Y));
            TrayMenu.PlacementTarget = Shelf;
            TrayMenu.Placement = PlacementMode.RelativePoint;
            TrayMenu.HorizontalOffset = point.X - 16;
            TrayMenu.VerticalOffset = point.Y - 16;
            TrayMenu.IsOpen = true;
        }

        void Notice(string message)
        {
            if (notice != null) notice.Close();
            notice = new LightNotice(message);
            var current = notice;
            current.Closed += delegate { if (notice == current) notice = null; };
            notice.Show();
        }

        static string Quote(string path) { return "\"" + path + "\""; }

        public async void Act(ShotAction action, CaptureItem item)
        {
            try
            {
                if (!File.Exists(item.Path)) { Gallery.Prune(); return; }
                switch (action)
                {
                    case ShotAction.Copy:
                        var payload = new DataObject();
                        byte[] png;
                        using (var stream = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            var image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                            payload.SetImage(image);
                            png = ImageContent.EncodeClipboard(image);
                        }
                        payload.SetData("PNG", new MemoryStream(png));
                        payload.SetFileDropList(new StringCollection { item.Path });
                        await WindowsClipboard.Write(payload);
                        Shelf.ShowCopied(item.Path);
                        break;
                    case ShotAction.Open:
                        Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                        Shelf.FollowImageViewer(item.Path);
                        break;
                    case ShotAction.Edit:
                        var existing = Editors.Find(editor => FileRules.SameFile(editor.ImagePath, item.Path));
                        if (existing != null)
                        {
                            existing.Present();
                            break;
                        }
                        var window = new ImageEditorWindow(item.Path, Gallery.Store, RefreshEditedImage, Captures.IgnoreCurrentClipboard);
                        Editors.Add(window);
                        window.Closed += delegate
                        {
                            Editors.Remove(window);
                            if (!window.ReturnBounds.IsEmpty) Shelf.ShowEditedImage(window.ImagePath, window.ReturnBounds, window.ReturnPicture);
                        };
                        Shelf.Reveal(false, false, 0);
                        window.Present();
                        break;
                    case ShotAction.Save:
                        var dialog = new SaveFileDialog { Title = "保存副本", FileName = Path.GetFileName(item.Path), Filter = "图片|*" + Path.GetExtension(item.Path), OverwritePrompt = true };
                        if (dialog.ShowDialog() == true && !FileRules.SameFile(item.Path, dialog.FileName)) File.Copy(item.Path, dialog.FileName, true);
                        break;
                    case ShotAction.Locate:
                        Process.Start("explorer.exe", "/select," + Quote(item.Path));
                        break;
                    case ShotAction.Remove:
                        Shelf.ShowCollect(item);
                        Gallery.Remove(item);
                        break;
                    case ShotAction.Trash:
                        FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        Shelf.ShowDiscard(item);
                        Gallery.Remove(item);
                        Gallery.Prune();
                        break;
                }
            }
            catch (Exception error) { Gallery.Store.Log(error); Notice("操作未完成：" + error.Message); }
        }

        public void Dispose()
        {
            disposed = true; updateTimer.Stop(); updateLifetime.Cancel();
            if (updateWindow != null) updateWindow.Close();
            updates.Dispose();
            if (Basket != null) Basket.Close();
            if (TextWindow != null) TextWindow.Close();
            if (Appearance != null) Appearance.Close();
            if (Settings != null) Settings.Close();
            if (WebCapture != null) WebCapture.Close();
            if (RecentCaptures != null) RecentCaptures.Close();
            if (notice != null) notice.Close();
            foreach (var editor in Editors.ToArray()) editor.CloseWithoutPrompt();
            TrayMenu.Close();
            Gallery.Changed -= SyncAutoHang;
            Texts.Changed -= Shelf.UpdateTexts;
            Captures.Dispose();
            Captures.Captured -= RecordSystemCapture;
            events.Dispose();
            tray.Visible = false;
            var icon = tray.Icon;
            tray.Dispose();
            icon.Dispose();
            Shelf.Close();
        }

        async Task RefreshEditedImage(string path)
        {
            ImageContent image = await Task.Run(() => ImageContent.Read(path));
            var item = Gallery.Items.Find(candidate => FileRules.SameFile(candidate.Path, path));
            if (item == null) Gallery.Add(path, image);
            else
            {
                item.Image = image;
                item.Written = File.GetLastWriteTimeUtc(path);
                Gallery.Notify();
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        public static void Main(string[] arguments)
        {
            if (Array.IndexOf(arguments, "--quit") >= 0) { WindowsNative.RequestExit(); return; }
            bool first;
            using (var instance = new Mutex(true, @"Local\TangHao.ScreenLine.Windows", out first))
            {
                if (!first) { WindowsNative.ShowExisting(); return; }
                var store = new StateStore(StateStore.DefaultRoot);
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                LightTheme.Install(app);
                DesktopController controller = null;
                app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args) { store.Log(args.Exception); args.Handled = true; };
                app.Startup += async delegate { controller = new DesktopController(store, WindowsNative.ScreenshotFolder()); await controller.Start(); };
                app.Exit += delegate { if (controller != null) controller.Dispose(); };
                app.Run();
            }
        }
    }
}
