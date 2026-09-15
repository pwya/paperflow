using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PaperFlow;
public sealed class SettingsWindow : Window
{
    public Preferences Result { get; }
    private bool ready;
    public SettingsWindow(Preferences settings, string directory, Action export, Action import, Action<Preferences> preview)
    {
        Result = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(settings))!;
        Title = "外观与设置"; Width = 490; Height = Math.Min(780, SystemParameters.WorkArea.Height - 30); MinHeight = 420;
        ResizeMode = ResizeMode.CanResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Keep the settings form legible while a translucent/dark widget is previewed.
        Background = Brushes.White; Foreground = new SolidColorBrush(Color.FromRgb(36, 53, 47)); FontSize = 13;
        Resources["Ink"] = Foreground; Resources["Muted"] = Brushes.SlateGray; Resources["Card"] = Brushes.White;
        Resources["Soft"] = new SolidColorBrush(Color.FromRgb(233, 238, 234)); Resources["Line"] = Brushes.LightGray;
        Resources["Accent"] = new SolidColorBrush(Color.FromRgb(33, 132, 107)); Resources["AccentText"] = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll);
        var body = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; scroll.Content = body;
        TextBlock Label(string text, double size = 13) { var label = new TextBlock { Text = text, FontSize = size, Foreground = Foreground, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 6) }; body.Children.Add(label); return label; }
        void Preview() { if (ready) preview(JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(Result))!); }
        Label("让挂件融入你的桌面", 21);
        Label("外观即时预览；取消会恢复原来的设置。", 11);
        Label("主题");
        var themes = new ComboBox { ItemsSource = Appearance.Presets.Select(p => p.Name).ToArray(), SelectedItem = Result.Theme }; body.Children.Add(themes);
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 2) }; body.Children.Add(colors);
        void Pick(bool accent)
        {
            var current = accent ? Appearance.Current.Accent : Appearance.Current.Window;
            using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(current) };
            if (dialog.ShowDialog(new DialogOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle)) != System.Windows.Forms.DialogResult.OK) return;
            string value = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            if (accent) Result.AccentColor = value; else Result.BackgroundColor = value; Preview();
        }
        colors.Children.Add(MainWindow.ActionButton("主色…", () => Pick(true))); colors.Children.Add(MainWindow.ActionButton("底色…", () => Pick(false)));
        colors.Children.Add(MainWindow.ActionButton("恢复主题色", () => { Result.AccentColor = ""; Result.BackgroundColor = ""; Preview(); }));
        var opacityLabel = Label("背景不透明度");
        var opacity = new Slider { Minimum = 5, Maximum = 100, Value = Result.BackgroundOpacity * 100, TickFrequency = 5, IsSnapToTickEnabled = true, Margin = new Thickness(0, 5, 0, 7) }; body.Children.Add(opacity);
        void OpacityChanged() { Result.BackgroundOpacity = opacity.Value / 100; opacityLabel.Text = $"背景不透明度 · {opacity.Value:0}%（文字保持清晰）"; Preview(); }
        opacity.ValueChanged += (_, _) => OpacityChanged(); OpacityChanged();
        themes.SelectionChanged += (_, _) => { Result.Theme = themes.SelectedItem as string ?? "竹青"; Result.AccentColor = ""; Result.BackgroundColor = ""; opacity.Value = Result.Theme == "透明" ? 35 : 100; Preview(); };
        Label("字体");
        var fonts = new ComboBox { ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).Append(Result.FontName).Distinct().OrderBy(n => n).ToList(), SelectedItem = Result.FontName, MaxDropDownHeight = 260 }; body.Children.Add(fonts);
        fonts.SelectionChanged += (_, _) => { Result.FontName = fonts.SelectedItem as string ?? "Microsoft YaHei UI"; Preview(); };
        var sizeLabel = Label("字号");
        var size = new Slider { Minimum = 10, Maximum = 22, TickFrequency = 1, IsSnapToTickEnabled = true, Value = Result.TextSize, Margin = new Thickness(0, 5, 0, 7) }; body.Children.Add(size);
        void SizeChanged() { Result.TextSize = size.Value; sizeLabel.Text = $"字号 · {size.Value:0}（论文标题与百分比按比例放大）"; Preview(); }
        size.ValueChanged += (_, _) => SizeChanged(); SizeChanged();
        var bold = new CheckBox { Content = "论文标题加粗", IsChecked = Result.TitleBold, Margin = new Thickness(0, 6, 0, 6) }; body.Children.Add(bold);
        bold.Click += (_, _) => { Result.TitleBold = bold.IsChecked == true; Preview(); };
        Label("进度条厚度");
        var bars = new ComboBox { ItemsSource = new[] { "醒目 · 14", "粗 · 20", "特粗 · 28" }, SelectedIndex = Array.IndexOf(new[] { 14, 20, 28 }, Result.BarHeight) }; body.Children.Add(bars);
        bars.SelectionChanged += (_, _) => { Result.BarHeight = new[] { 14, 20, 28 }[Math.Max(0, bars.SelectedIndex)]; Preview(); };
        var startup = new CheckBox { Content = "登录 Windows 时自动打开", IsChecked = StartupEntry.IsEnabled(), Margin = new Thickness(0, 18, 0, 6) }; body.Children.Add(startup);
        Label("论文同步与备份", 16);
        Label(Result.SyncFolder == "" ? "当前为本机保存。通过 OneDrive 中的固定启动入口打开，即可连接其 data 目录。" : "正在使用 OneDrive 文件夹\n" + Result.SyncFolder, 11);
        if (Result.SyncFolder != "") body.Children.Add(MainWindow.ActionButton("打开同步资料文件夹", () => OpenFolder(Result.SyncFolder)));
        Label("其他电脑收到文件后，挂件自动刷新。云端到达时间由 OneDrive 决定。字体、主题和窗口位置只保存在本机。", 11);
        var backup = new StackPanel { Orientation = Orientation.Horizontal }; backup.Children.Add(MainWindow.ActionButton("导出备份", export)); backup.Children.Add(MainWindow.ActionButton("导入备份", import)); backup.Children.Add(MainWindow.ActionButton("本地资料", () => OpenFolder(directory))); body.Children.Add(backup);
        Label(Product.Name + " 版本 " + Product.Version + " · 本地离线可用\n× 收起到托盘；双击托盘图标恢复。更新时请先从托盘菜单退出，再打开固定启动入口。", 11);
        var cancel = MainWindow.ActionButton("取消", () => DialogResult = false); cancel.IsCancel = true; buttons.Children.Add(cancel);
        buttons.Children.Add(MainWindow.ActionButton("保存设置", () =>
        {
            try { if ((startup.IsChecked == true) != StartupEntry.IsEnabled()) StartupEntry.Write(startup.IsChecked == true, Result.LauncherPath); DialogResult = true; }
            catch (Exception ex) { MessageBox.Show(this, "开机启动设置未能保存。\n" + ex.Message); }
        }, true)); ready = true;
    }
    private static void OpenFolder(string folder) { Directory.CreateDirectory(folder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); }
    private sealed class DialogOwner : System.Windows.Forms.IWin32Window { public IntPtr Handle { get; } public DialogOwner(IntPtr handle) { Handle = handle; } }
}
