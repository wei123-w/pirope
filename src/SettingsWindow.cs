using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace ScreenLine
{
    internal sealed class SettingsWindow : Window
    {
        readonly SavedState original;
        readonly Action<SavedState> save;
        readonly Dictionary<string, TextBox> shortcuts = new Dictionary<string, TextBox>();
        readonly CheckBox revealOnStartup;
        readonly CheckBox launchOnStartup;
        readonly TextBox folder;
        readonly ComboBox format, action;
        readonly ComboBox delay;
        readonly TextBox workflowName = new TextBox { Height = 34, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 3, 8, 3), BorderBrush = Brush("#DCE2EA"), Background = Brush("#FAFBFC"), Foreground = Brush("#283343") };
        readonly ComboBox workflowMode;
        readonly ComboBox workflowAction;
        readonly ListBox workflowList = new ListBox { Height = 82, DisplayMemberPath = "Name", BorderBrush = Brush("#DCE2EA"), BorderThickness = new Thickness(1), Background = Brush("#FAFBFC"), Padding = new Thickness(6) };
        readonly List<CaptureWorkflowPreset> workflowPresets;
        readonly TextBlock error;
        readonly TextBlock shortcutHint;

        public SettingsWindow(SavedState state, Action<SavedState> save)
        {
            original = state; this.save = save;
            workflowPresets = (state.WorkflowPresets ?? new List<CaptureWorkflowPreset>()).Select(ClonePreset).ToList();
            Title = "图绳设置"; Width = 720; Height = 650; MinWidth = 640; MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brush("#F3F5F8");
            FontFamily = new FontFamily("Segoe UI"); FontSize = 13; Foreground = Brush("#283343");
            var root = new DockPanel { Margin = new Thickness(18) };
            Content = root;
            var heading = new StackPanel { Margin = new Thickness(4, 0, 4, 14) };
            heading.Children.Add(new TextBlock { Text = "设置", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brush("#283343") });
            heading.Children.Add(new TextBlock { Text = "自定义图绳的启动方式、快捷键和截图行为。", FontSize = 12, Foreground = Brush("#65717F"), Margin = new Thickness(0, 3, 0, 0) });
            DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 2, 0) };
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            error = new TextBlock { Foreground = Brush("#B84343"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Width = 300, Margin = new Thickness(0, 0, 12, 0) };
            footer.Children.Add(error);
            var reset = LightTheme.Button("恢复默认", delegate { Reset(null, null); }); reset.Margin = new Thickness(0, 0, 8, 0); footer.Children.Add(reset);
            var cancel = LightTheme.Button("取消", Close); cancel.Margin = new Thickness(0, 0, 8, 0); footer.Children.Add(cancel);
            var apply = LightTheme.Button("保存设置", delegate { Save(null, null); }, "primary"); footer.Children.Add(apply);

            var tabStyle = new Style(typeof(TabItem));
            tabStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(18, 9, 18, 9)));
            tabStyle.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            tabStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#65717F")));
            var selectedTab = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            selectedTab.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#2463AE")));
            selectedTab.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            tabStyle.Triggers.Add(selectedTab);
            var tabs = new TabControl { Background = Brushes.White, BorderBrush = Brush("#E0E4EA"), BorderThickness = new Thickness(0), Padding = new Thickness(0), ItemContainerStyle = tabStyle };
            var surface = new Border { Background = Brushes.White, BorderBrush = Brush("#E2E7EE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = tabs };
            root.Children.Add(surface);
            var general = Page(); tabs.Items.Add(new TabItem { Header = "常规", Content = general });
            general.Children.Add(SectionTitle("启动与行为", "按你的使用习惯设置图绳的启动方式。"));
            revealOnStartup = new CheckBox { Content = "启动后自动展开绳子", IsChecked = state.RevealOnStartup, Margin = new Thickness(4, 8, 4, 14) };
            general.Children.Add(revealOnStartup);
            launchOnStartup = new CheckBox { Content = "登录 Windows 时自动启动图绳", IsChecked = state.LaunchOnStartup, Margin = new Thickness(4, 4, 4, 14) };
            general.Children.Add(launchOnStartup);
            general.Children.Add(Note("原有剪贴板收集与“新截图自动挂上”开关继续由托盘面板独立控制。"));

            var hotkeyPage = Page(); tabs.Items.Add(new TabItem { Header = "快捷键", Content = hotkeyPage });
            hotkeyPage.Children.Add(SectionTitle("全局快捷键", "点击快捷键框后直接按下组合键。"));
            AddShortcut(hotkeyPage, "显示 / 收起绳子", "ToggleShortcut", state.ToggleShortcut, false);
            AddShortcut(hotkeyPage, "框选截图", "RegionShortcut", state.RegionShortcut, false);
            AddShortcut(hotkeyPage, "全屏截图", "FullScreenShortcut", state.FullScreenShortcut, false);
            AddShortcut(hotkeyPage, "活动窗口截图", "WindowShortcut", state.WindowShortcut, false);
            AddShortcut(hotkeyPage, "延时截图", "DelayShortcut", state.DelayShortcut, false);
            shortcutHint = Note("快捷键至少包含 Ctrl、Alt、Shift 或 Win 中的一项。聚焦输入框后直接按键；Esc 或退格可清除。");
            hotkeyPage.Children.Add(shortcutHint);

            var capturePage = Page(); tabs.Items.Add(new TabItem { Header = "截图" , Content = new ScrollViewer { Content = capturePage, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            capturePage.Children.Add(SectionTitle("截图偏好", "设置默认保存方式和截图完成后的操作。"));
            format = Choice(new[] { "PNG", "JPG", "BMP" }, state.ScreenshotFormat);
            capturePage.Children.Add(Row("默认保存格式", format));
            folder = new TextBox { Text = state.ScreenshotFolder ?? WindowsNative.ScreenshotFolder(), MinWidth = 250, Height = 34, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 3, 8, 3), BorderBrush = Brush("#DCE2EA"), Background = Brush("#FAFBFC"), Foreground = Brush("#283343") };
            var browse = Button("浏览…"); browse.Margin = new Thickness(8, 0, 0, 0);
            browse.Click += delegate
            {
                using (var picker = new Forms.FolderBrowserDialog { Description = "选择截图保存目录", SelectedPath = folder.Text })
                    if (picker.ShowDialog() == Forms.DialogResult.OK) folder.Text = picker.SelectedPath;
            };
            var folderRow = new DockPanel { Margin = new Thickness(0, 4, 0, 10) }; DockPanel.SetDock(browse, Dock.Right); folderRow.Children.Add(browse); folderRow.Children.Add(folder);
            capturePage.Children.Add(Labeled("截图保存目录", folderRow));
            delay = Choice(new[] { "3", "5", "10", "15", "30", "60" }, Math.Max(1, state.DelaySeconds).ToString());
            capturePage.Children.Add(Row("延时截图倒计时（秒）", delay));
            action = Choice(new[] { "不弹出窗口", "打开图片篮子", "复制到剪贴板", "进入编辑器", "自动挂上绳子" }, ActionLabel(state.ScreenshotAction));
            capturePage.Children.Add(Row("截图完成后的默认操作", action));
            capturePage.Children.Add(new TextBlock { Text = "截图流程预设", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 18, 0, 6) });
            workflowList.ItemsSource = workflowPresets.ToArray(); capturePage.Children.Add(workflowList);
            workflowName.Text = "我的截图流程";
            workflowMode = Choice(new[] { "框选截图", "全屏截图", "活动窗口截图", "延时截图", "连续截图拼接" }, "框选截图");
            workflowAction = Choice(new[] { "不弹出窗口", "打开图片篮子", "复制到剪贴板", "进入编辑器", "自动挂上绳子" }, "复制到剪贴板");
            capturePage.Children.Add(Row("预设名称", workflowName));
            capturePage.Children.Add(Row("截图方式", workflowMode));
            capturePage.Children.Add(Row("完成后操作", workflowAction));
            var presetButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
            var addPreset = Button("保存预设"); addPreset.Click += AddPreset; presetButtons.Children.Add(addPreset);
            var removePreset = Button("删除选中"); removePreset.Margin = new Thickness(8, 0, 0, 0); removePreset.Click += RemovePreset; presetButtons.Children.Add(removePreset);
            capturePage.Children.Add(presetButtons);
            capturePage.Children.Add(Note("截图和编辑均在本机处理。恢复默认设置不会删除图片或历史文件。"));
        }

        void AddShortcut(Panel panel, string label, string name, string value, bool fixedDefault)
        {
            var box = new TextBox { Text = value, IsReadOnly = fixedDefault, MinWidth = 190, Height = 36, Tag = name, VerticalContentAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(10, 0, 4, 0), FontSize = 13, Foreground = Brush("#283343") };
            box.PreviewKeyDown += CaptureKey;
            shortcuts[name] = box;
            var frame = new Border { BorderBrush = Brush("#DCE2EA"), BorderThickness = new Thickness(1), Background = Brush("#FAFBFC"), CornerRadius = new CornerRadius(7), Child = box };
            box.GotKeyboardFocus += delegate { frame.BorderBrush = Brush("#4B83C3"); frame.BorderThickness = new Thickness(2); frame.Background = Brushes.White; shortcutHint.Text = "正在录入“" + label + "”：按下组合键，Esc 或退格可清除。"; shortcutHint.Foreground = Brush("#2463AE"); };
            box.LostKeyboardFocus += delegate { frame.BorderBrush = Brush("#DCE2EA"); frame.BorderThickness = new Thickness(1); frame.Background = Brush("#FAFBFC"); };
            var record = LightTheme.Button("录入", delegate { box.Focus(); error.Text = ""; }, "quiet"); record.MinWidth = 48; record.Height = 30; record.Margin = new Thickness(2);
            var entry = new DockPanel(); DockPanel.SetDock(record, Dock.Right); entry.Children.Add(record); entry.Children.Add(frame);
            var row = Row(label, entry); row.Margin = new Thickness(0, 4, 0, 4);
            var card = new Border { Child = row, Background = Brushes.White, BorderBrush = Brush("#E8EBEF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 5, 8, 5), Margin = new Thickness(0, 0, 0, 7) };
            panel.Children.Add(card);
        }

        void CaptureKey(object sender, KeyEventArgs args)
        {
            var box = (TextBox)sender;
            if (box.IsReadOnly) return;
            args.Handled = true;
            Key key = args.Key == Key.System ? args.SystemKey : args.Key;
            if (key == Key.Escape || key == Key.Back)
            {
                box.Clear(); error.Text = "";
                return;
            }
            bool modifierKey = key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin;
            var parts = new List<string>();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 || key == Key.LeftCtrl || key == Key.RightCtrl) parts.Add("Ctrl");
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 || key == Key.LeftAlt || key == Key.RightAlt) parts.Add("Alt");
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 || key == Key.LeftShift || key == Key.RightShift) parts.Add("Shift");
            if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0 || key == Key.LWin || key == Key.RWin) parts.Add("Win");
            if (modifierKey) { box.Text = string.Join("+", parts) + "+…"; error.Text = ""; return; }
            if (parts.Count == 0) { error.Text = "快捷键必须包含至少一个修饰键。"; return; }
            string keyName = key.ToString();
            if (keyName.Length == 2 && keyName[0] == 'D' && char.IsDigit(keyName[1])) keyName = keyName.Substring(1);
            parts.Add(keyName);
            box.Text = string.Join("+", parts);
            error.Text = ""; shortcutHint.Text = "已录入“" + box.Text + "”。点击其他输入框可继续设置。"; shortcutHint.Foreground = Brush("#65717F");
        }

        void Save(object sender, RoutedEventArgs args)
        {
            var values = shortcuts.ToDictionary(pair => pair.Key, pair => pair.Value.Text.Trim(), StringComparer.Ordinal);
            if (values.Values.Any(value => string.IsNullOrWhiteSpace(value))) { error.Text = "快捷键不能为空。"; return; }
            if (values.Values.Any(value => value.EndsWith("+…", StringComparison.Ordinal))) { error.Text = "组合键还未录入完成，请再按一个主键。"; return; }
            if (values.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count) { error.Text = "快捷键不能重复，请修改冲突项。"; return; }
            var draft = Copy(original);
            draft.ToggleShortcut = values["ToggleShortcut"]; draft.RegionShortcut = values["RegionShortcut"];
            draft.FullScreenShortcut = values["FullScreenShortcut"]; draft.WindowShortcut = values["WindowShortcut"]; draft.DelayShortcut = values["DelayShortcut"];
            draft.RevealOnStartup = revealOnStartup.IsChecked == true;
            draft.LaunchOnStartup = launchOnStartup.IsChecked == true;
            draft.ScreenshotFolder = folder.Text.Trim(); draft.ScreenshotFormat = format.SelectedItem as string;
            int seconds; if (!int.TryParse(delay.SelectedItem as string, out seconds)) seconds = 3;
            draft.DelaySeconds = seconds; draft.ScreenshotAction = ActionValue(action.SelectedItem as string);
            draft.WorkflowPresets = workflowPresets.Select(ClonePreset).ToList();
            try { save(draft); original.ToggleShortcut = draft.ToggleShortcut; Close(); }
            catch (Exception exception) { error.Text = "保存失败：" + exception.Message; }
        }

        void Reset(object sender, RoutedEventArgs args)
        {
            var defaults = new SavedState();
            foreach (var pair in shortcuts) pair.Value.Text = (string)typeof(SavedState).GetProperty(pair.Key).GetValue(defaults, null);
            shortcutHint.Text = "快捷键至少包含 Ctrl、Alt、Shift 或 Win 中的一项。聚焦输入框后直接按键；Esc 或退格可清除。"; shortcutHint.Foreground = Brush("#65717F");
            revealOnStartup.IsChecked = defaults.RevealOnStartup; launchOnStartup.IsChecked = false; folder.Text = WindowsNative.ScreenshotFolder();
            format.SelectedItem = "PNG"; delay.SelectedItem = "3"; action.SelectedItem = "不弹出窗口"; error.Text = "默认值已恢复；点击保存后生效。";
            workflowPresets.Clear(); workflowList.ItemsSource = workflowPresets.ToArray();
        }

        void AddPreset(object sender, RoutedEventArgs args)
        {
            string name = workflowName.Text.Trim();
            if (name.Length == 0) { error.Text = "请填写预设名称。"; return; }
            if (workflowPresets.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) { error.Text = "预设名称已存在。"; return; }
            string modeValue = new[] { "Region", "FullScreen", "Window", "Delay", "Continuous" }[Math.Max(0, workflowMode.SelectedIndex)];
            int seconds; if (!int.TryParse(delay.SelectedItem as string, out seconds)) seconds = 3;
            workflowPresets.Insert(0, new CaptureWorkflowPreset { Name = name, Mode = modeValue, Action = ActionValue(workflowAction.SelectedItem as string), DelaySeconds = seconds });
            workflowList.ItemsSource = workflowPresets.ToArray(); error.Text = "预设已加入草稿，点击保存设置后生效。";
        }

        void RemovePreset(object sender, RoutedEventArgs args)
        {
            var selected = workflowList.SelectedItem as CaptureWorkflowPreset;
            if (selected == null) return;
            workflowPresets.Remove(selected); workflowList.ItemsSource = workflowPresets.ToArray(); error.Text = "预设已从草稿中移除，点击保存设置后生效。";
        }

        static CaptureWorkflowPreset ClonePreset(CaptureWorkflowPreset source)
        {
            return new CaptureWorkflowPreset { Name = source.Name, Mode = source.Mode, Action = source.Action, DelaySeconds = source.DelaySeconds };
        }

        internal static SavedState Copy(SavedState source)
        {
            return new SavedState
            {
                CollectClipboard = source.CollectClipboard, CollectTextClipboard = source.CollectTextClipboard, AutoHang = source.AutoHang,
                Theme = source.Theme, LastUpdateNotification = source.LastUpdateNotification, ToggleShortcut = source.ToggleShortcut,
                RegionShortcut = source.RegionShortcut, FullScreenShortcut = source.FullScreenShortcut, WindowShortcut = source.WindowShortcut,
                DelayShortcut = source.DelayShortcut, ScreenshotFolder = source.ScreenshotFolder, ScreenshotFormat = source.ScreenshotFormat,
                DelaySeconds = source.DelaySeconds, ScreenshotAction = source.ScreenshotAction, RevealOnStartup = source.RevealOnStartup, LaunchOnStartup = source.LaunchOnStartup,
                Images = source.Images, Collection = source.Collection, Texts = source.Texts, RecentCaptures = source.RecentCaptures, WorkflowPresets = source.WorkflowPresets
            };
        }

        static string ActionLabel(string value)
        {
            if (value == "Basket") return "打开图片篮子"; if (value == "Copy") return "复制到剪贴板"; if (value == "Edit") return "进入编辑器"; if (value == "Hang") return "自动挂上绳子"; return "不弹出窗口";
        }
        static string ActionValue(string value)
        {
            if (value == "打开图片篮子") return "Basket"; if (value == "复制到剪贴板") return "Copy"; if (value == "进入编辑器") return "Edit"; if (value == "自动挂上绳子") return "Hang"; return "Preview";
        }
        static StackPanel Page() { return new StackPanel { Margin = new Thickness(12, 8, 12, 12) }; }
        static UIElement SectionTitle(string title, string description)
        {
            var group = new StackPanel { Margin = new Thickness(2, 0, 2, 12) };
            group.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#283343") });
            group.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = Brush("#65717F"), Margin = new Thickness(0, 3, 0, 0) });
            return group;
        }
        static Grid Row(string title, UIElement control)
        {
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(label, 0); grid.Children.Add(label); Grid.SetColumn(control, 1); grid.Children.Add(control); return grid;
        }
        static UIElement Labeled(string title, UIElement control)
        {
            var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 5) }); panel.Children.Add(control); return panel;
        }
        static ComboBox Choice(IEnumerable<string> values, string selected)
        {
            var box = new ComboBox { ItemsSource = values, SelectedItem = selected, MinWidth = 130, Height = 34, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 2, 8, 2), BorderBrush = Brush("#DCE2EA"), Background = Brush("#FAFBFC"), Foreground = Brush("#283343") };
            return box;
        }
        static TextBlock Note(string text) { return new TextBlock { Text = text, Foreground = Brush("#65717F"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 4), FontSize = 11 }; }
        static Button Button(string text) { return LightTheme.Button(text, delegate { }); }
        static SolidColorBrush Brush(string color) { return (SolidColorBrush)new BrushConverter().ConvertFromString(color); }
    }
}
