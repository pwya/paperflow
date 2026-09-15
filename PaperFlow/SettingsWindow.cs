using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PaperFlow;
public sealed class SettingsWindow : Window
{
    public Preferences Result { get; }
    private bool ready;
    private static readonly string[] Categories = { "外观", "视图与分页", "同步与启动", "关于与反馈" };

    public SettingsWindow(Preferences settings, string directory, Action export, Action import, Action<Preferences> preview, Func<int> estimate)
    {
        Result = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(settings))!;
        double scale = Appearance.DialogScale;
        // Inside a normal window the text stops at 250%, so the form always fits.
        Button B(string text, Action action, bool primary = false) => MainWindow.ActionButton(text, action, primary, scale);
        Title = "设置";
        Width = Math.Min(720 * scale, SystemParameters.WorkArea.Width - 40); Height = Math.Min(840 * scale, SystemParameters.WorkArea.Height - 30);
        MinWidth = Math.Min(600 * scale, SystemParameters.WorkArea.Width - 40); MinHeight = Math.Min(460 * scale, SystemParameters.WorkArea.Height - 30);
        ResizeMode = ResizeMode.CanResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // The widget itself stays out of the taskbar; a window the user opened should not.
        ShowInTaskbar = true;
        // Keep the settings form legible while a translucent/dark widget is previewed.
        Background = Brushes.White; Foreground = new SolidColorBrush(Color.FromRgb(36, 53, 47)); FontSize = 13 * scale;
        Resources["Ink"] = Foreground; Resources["Muted"] = Brushes.SlateGray; Resources["Card"] = Brushes.White;
        Resources["Soft"] = new SolidColorBrush(Color.FromRgb(233, 238, 234)); Resources["Line"] = Brushes.LightGray;
        Resources["Accent"] = new SolidColorBrush(Color.FromRgb(33, 132, 107)); Resources["AccentText"] = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(18 * scale) }; Content = root;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14 * scale, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);

        // Left navigation plus one panel per category, so a long form stops being one endless list.
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(138 * scale) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(layout);
        var nav = new ListBox { ItemsSource = Categories, SelectedIndex = 0, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 14 * scale, 0), FontSize = 13 * scale };
        var pages = new Panel[Categories.Length];
        var host = new Grid();
        for (int i = 0; i < pages.Length; i++) { pages[i] = new StackPanel { Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed }; host.Children.Add(pages[i]); }
        var scroll = new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(nav, 0); Grid.SetColumn(scroll, 1); layout.Children.Add(nav); layout.Children.Add(scroll);
        nav.SelectionChanged += (_, _) => { int picked = Math.Max(0, nav.SelectedIndex); for (int i = 0; i < pages.Length; i++) pages[i].Visibility = i == picked ? Visibility.Visible : Visibility.Collapsed; };

        TextBlock Label(Panel page, string text, double size = 13)
        {
            var label = new TextBlock { Text = text, FontSize = size * scale, Foreground = Foreground, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9 * Appearance.Scale, 0, 6 * Appearance.Scale) };
            page.Children.Add(label); return label;
        }
        void Preview() { if (ready) preview(JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(Result))!); }

        // ---------- 外观 ----------
        var look = pages[0];
        Label(look, "让外观像你自己的桌面", 20);
        Label(look, "所有改动都会立刻作用在挂件上；点取消会恢复打开设置前的样子。", 11);
        Label(look, "主题");
        var themes = new ComboBox { ItemsSource = Appearance.Presets.Select(p => p.Name).ToArray(), SelectedItem = Result.Theme }; look.Children.Add(themes);
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10 * Appearance.Scale, 0, 2) }; look.Children.Add(colors);
        void Pick(bool accent)
        {
            var current = accent ? Appearance.Current.Accent : Appearance.Current.Window;
            using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(current) };
            if (dialog.ShowDialog(new DialogOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle)) != System.Windows.Forms.DialogResult.OK) return;
            string value = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            if (accent) Result.AccentColor = value; else Result.BackgroundColor = value; Preview();
        }
        colors.Children.Add(B("主色…", () => Pick(true))); colors.Children.Add(B("底色…", () => Pick(false)));
        colors.Children.Add(B("恢复主题色", () => { Result.AccentColor = ""; Result.BackgroundColor = ""; Preview(); }));
        var opacityLabel = Label(look, "背景不透明度");
        var opacity = new Slider { Minimum = 5, Maximum = 100, Value = Result.BackgroundOpacity * 100, TickFrequency = 5, IsSnapToTickEnabled = true, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) }; look.Children.Add(opacity);
        void OpacityChanged() { Result.BackgroundOpacity = opacity.Value / 100; opacityLabel.Text = $"背景不透明度 · {opacity.Value:0}%（文字保持清晰）"; Preview(); }
        opacity.ValueChanged += (_, _) => OpacityChanged(); OpacityChanged();
        themes.SelectionChanged += (_, _) => { Result.Theme = themes.SelectedItem as string ?? "竹青"; Result.AccentColor = ""; Result.BackgroundColor = ""; opacity.Value = Result.Theme == "透明" ? 35 : 100; Preview(); };
        Label(look, "字体");
        var fonts = new ComboBox { ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).Append(Result.FontName).Distinct().OrderBy(n => n).ToList(), SelectedItem = Result.FontName, MaxDropDownHeight = 260 }; look.Children.Add(fonts);
        fonts.SelectionChanged += (_, _) => { Result.FontName = fonts.SelectedItem as string ?? "Microsoft YaHei UI"; Preview(); };

        var sizeLabel = Label(look, "字号（只改文字大小）");
        var size = new Slider { Minimum = 9, Maximum = 36, TickFrequency = 1, IsSnapToTickEnabled = true, Value = Result.TextSize, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) }; look.Children.Add(size);
        var zoomLabel = Label(look, "界面缩放（文字和间距一起缩放）");
        var zoom = new Slider { Minimum = 80, Maximum = 200, TickFrequency = 5, IsSnapToTickEnabled = true, Value = Result.UiScale * 100, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) }; look.Children.Add(zoom);
        var fit = new TextBlock { FontSize = 11 * scale, Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8 * Appearance.Scale) }; look.Children.Add(fit);
        var grow = new CheckBox { Content = "调整字号或界面缩放时，自动放大窗口（最多占屏幕工作区的一半）", IsChecked = Result.AutoGrowWindow, Margin = new Thickness(0, 4 * Appearance.Scale, 0, 8 * Appearance.Scale) }; look.Children.Add(grow);
        grow.Click += (_, _) => { Result.AutoGrowWindow = grow.IsChecked == true; Preview(); };
        void RefreshFit()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                int count = estimate();
                fit.Text = count > 0 ? $"按当前窗口和字号，大约能完整显示 {count} 篇论文。" : "当前窗口放不下整张卡片，可以把窗口调大或把缩放调小。";
            }), DispatcherPriority.Loaded);
        }
        void SizeChanged() { Result.TextSize = Math.Round(size.Value); sizeLabel.Text = $"字号 · {size.Value:0}（只改文字大小，不动间距）"; Preview(); RefreshFit(); }
        size.ValueChanged += (_, _) => SizeChanged(); SizeChanged();
        void ZoomChanged() { Result.UiScale = zoom.Value / 100; zoomLabel.Text = $"界面缩放 · {zoom.Value:0}%（文字和间距一起缩放）"; Preview(); RefreshFit(); }
        zoom.ValueChanged += (_, _) => ZoomChanged(); ZoomChanged();
        Label(look, "进度条厚度");
        var bars = new ComboBox { ItemsSource = new[] { "醒目 · 14", "粗 · 20", "特粗 · 28" }, SelectedIndex = Array.IndexOf(new[] { 14, 20, 28 }, Result.BarHeight) }; look.Children.Add(bars);
        bars.SelectionChanged += (_, _) => { Result.BarHeight = new[] { 14, 20, 28 }[Math.Max(0, bars.SelectedIndex)]; Preview(); };

        // ---------- 视图与分页 ----------
        var view = pages[1];
        Label(view, "列表怎么显示", 20);
        var bold = new CheckBox { Content = "论文标题加粗", IsChecked = Result.TitleBold, Margin = new Thickness(0, 6 * Appearance.Scale, 0, 6 * Appearance.Scale) }; view.Children.Add(bold);
        bold.Click += (_, _) => { Result.TitleBold = bold.IsChecked == true; Preview(); };
        Label(view, "搜索、筛选、排序、紧凑视图、隐藏哪些阶段、翻页方式和显示范围都在挂件右上角的“论文选项”里，那里改的是此刻看到什么。这里只放长期偏好。", 11);
        Label(view, "字号和界面缩放在“外观”里：字号只改文字大小，界面缩放把文字和间距一起放大。", 11);
        Label(view, "铺满屏幕时界面会不会挤，取决于字号和界面缩放的组合。字号很大时阶段标签会换行、卡片自然变高，一屏能看到的论文会变少，这是正常的。", 11);

        // ---------- 同步与启动 ----------
        var sync = pages[2];
        Label(sync, "论文同步与备份", 20);
        Label(sync, Result.SyncFolder == "" ? "当前为本机保存。通过 OneDrive 中的固定启动入口打开，即可连接其 data 目录。" : "正在使用 OneDrive 文件夹\n" + Result.SyncFolder, 11);
        if (Result.SyncFolder != "") sync.Children.Add(B("打开同步资料文件夹", () => OpenFolder(Result.SyncFolder)));
        Label(sync, "其他电脑收到文件后，小部件自动刷新。云端到达时间由 OneDrive 决定。字体、主题、字号、界面缩放和窗口位置只保存在本机。", 11);
        var backup = new StackPanel { Orientation = Orientation.Horizontal }; backup.Children.Add(B("导出备份", export)); backup.Children.Add(B("导入备份", import)); backup.Children.Add(B("本地资料", () => OpenFolder(directory))); sync.Children.Add(backup);
        Label(sync, "启动", 16);
        var startup = new CheckBox { Content = "登录 Windows 时自动打开", IsChecked = StartupEntry.IsEnabled(), Margin = new Thickness(0, 6 * Appearance.Scale, 0, 6 * Appearance.Scale) }; sync.Children.Add(startup);
        Label(sync, "× 收起到托盘，双击托盘图标恢复。更新时请先从托盘菜单退出，再打开固定启动入口。", 11);
        Label(sync, "更新通道和更新提示还没做（排在 1.6.0），现在更新靠 OneDrive 同步整个程序文件夹。", 11);

        // ---------- 关于与反馈 ----------
        var about = pages[3];
        Label(about, Product.Name, 20);
        Label(about, "版本 " + Product.Version + (Product.BuildCommit == "" ? " · 本地构建" : " · 提交 " + Product.BuildCommit), 12);
        Label(about, "MIT 许可 · Copyright (c) 2026 Panwang Yuang\n本程序不联网、不上传任何资料，论文数据只存在你自己的电脑和 OneDrive 文件夹里。", 11);
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6 * Appearance.Scale, 0, 0) };
        links.Children.Add(B("打开主页", () => OpenUrl("https://panwangyuang.com")));
        links.Children.Add(B("写邮件反馈", () => OpenUrl("mailto:pwya1998@126.com?subject=PaperFlow%20%E5%8F%8D%E9%A6%88")));
        links.Children.Add(B("复制邮箱", () => { try { Clipboard.SetText("pwya1998@126.com"); } catch (Exception) { } }));
        about.Children.Add(links);
        Label(about, "遇到问题发邮件到 pwya1998@126.com，或在 GitHub 上开 issue（仓库还没公开，链接以后再补）。", 11);

        var cancel = B("取消", () => DialogResult = false); cancel.IsCancel = true; buttons.Children.Add(cancel);
        buttons.Children.Add(B("保存设置", () =>
        {
            try { if ((startup.IsChecked == true) != StartupEntry.IsEnabled()) StartupEntry.Write(startup.IsChecked == true, Result.LauncherPath); DialogResult = true; }
            catch (Exception ex) { MessageBox.Show(this, "开机启动设置未能保存。\n" + ex.Message); }
        }, true)); ready = true; RefreshFit();
    }
    private static void OpenFolder(string folder) { Directory.CreateDirectory(folder); OpenUrl(folder); }
    private static void OpenUrl(string target) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception) { } }
    private sealed class DialogOwner : System.Windows.Forms.IWin32Window { public IntPtr Handle { get; } public DialogOwner(IntPtr handle) { Handle = handle; } }
}
