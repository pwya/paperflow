using System;
using System.Collections.Generic;
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
    private static readonly string[] Categories = { "外观", "字体与文字", "视图与分页", "同步与启动", "关于与反馈" };

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
        // 设置窗口跟着主题走（深色主题就是深色的），不再固定白底。
        // 窗口自己也要跟着主题实时变，否则换了主题之后控件变成浅色文字、窗口还是深色底，就成了看不清。
        SetResourceReference(BackgroundProperty, "WindowBackground");
        SetResourceReference(ForegroundProperty, "Ink");
        FontSize = 13 * scale * Appearance.RoleScale("body");
        var root = new DockPanel { Margin = new Thickness(18 * scale) }; Content = root;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14 * scale, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);

        // Left navigation plus one panel per category, so a long form stops being one endless list.
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(138 * scale) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(layout);
        var nav = new ListBox { ItemsSource = Categories, SelectedIndex = 0, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 14 * scale, 0), FontFamily = new FontFamily(Appearance.FamilyFor("body")), FontSize = 13 * scale * Appearance.RoleScale("body") };
        var pages = new Panel[Categories.Length];
        var host = new Grid();
        for (int i = 0; i < pages.Length; i++) { pages[i] = new StackPanel { Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed }; host.Children.Add(pages[i]); }
        var scroll = new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        // 让每页宽度跟着视口走，否则横向滚动会让文字永远不换行、长句子被切在窗口外。
        foreach (var page in pages) page.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("ViewportWidth") { Source = scroll });
        Grid.SetColumn(nav, 0); Grid.SetColumn(scroll, 1); layout.Children.Add(nav); layout.Children.Add(scroll);
        nav.SelectionChanged += (_, _) => { int picked = Math.Max(0, nav.SelectedIndex); for (int i = 0; i < pages.Length; i++) pages[i].Visibility = i == picked ? Visibility.Visible : Visibility.Collapsed; };

        TextBlock Label(Panel page, string text, double size = 13)
        {
            string role = size <= 12 ? "caption" : "body";
            var label = new TextBlock { Text = text, FontFamily = new FontFamily(Appearance.FamilyFor(role)), FontSize = size * scale * Appearance.RoleScale(role), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9 * Appearance.Scale, 0, 6 * Appearance.Scale) };
            page.Children.Add(label); return label;
        }
        void Preview() { if (ready) preview(JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(Result))!); }

        // ---------- 外观 ----------
        var look = pages[0];
        Label(look, "让外观像你自己的桌面", 20);
        Label(look, "所有改动都会立刻作用在挂件上；点取消会恢复打开设置前的样子。实心预览是你正在用的那套。", 11);
        Label(look, "布局");
        var layoutPicker = new ComboBox { ItemsSource = Themes.Layouts, SelectedItem = Result.ListLayout }; look.Children.Add(layoutPicker);
        layoutPicker.SelectionChanged += (_, _) => { Result.ListLayout = layoutPicker.SelectedItem as string ?? Themes.CardLayout; Preview(); };
        Label(look, "主题（按风格分组，共 " + Themes.All.Length + " 套）");
        var themeList = new ListBox { MaxHeight = 280 * scale, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        ListBoxItem? selected = null;
        foreach (var group in Themes.Grouped())
        {
            var heading = new ListBoxItem { Content = group.Key, IsEnabled = false, Focusable = false, FontFamily = new FontFamily(Appearance.FamilyFor("caption")), FontSize = 11.5 * scale * Appearance.RoleScale("caption"), Padding = new Thickness(2, 10 * Appearance.Scale, 0, 4 * Appearance.Scale), Background = Brushes.Transparent };
            heading.SetResourceReference(ForegroundProperty, "Muted");
            themeList.Items.Add(heading);
            foreach (var theme in group)
            {
                var row = ThemeRow(theme);
                if (theme.Name == Themes.Migrate(Result.Theme)) selected = row;
                themeList.Items.Add(row);
            }
        }
        look.Children.Add(themeList);
        themeList.SelectionChanged += (_, _) =>
        {
            if (themeList.SelectedItem is ListBoxItem item && item.Tag is Theme picked)
            {
                Result.Theme = picked.Name; Result.AccentColor = ""; Result.BackgroundColor = "";
                if (picked.Glass) Result.BackgroundOpacity = 0.35;
                Preview();
            }
        };
        if (selected != null) themeList.SelectedItem = selected;
        var follow = new CheckBox { Content = "跟随 Windows 的浅色/深色设置", IsChecked = Result.FollowSystemTheme, Margin = new Thickness(0, 8 * Appearance.Scale, 0, 4 * Appearance.Scale) }; look.Children.Add(follow);
        follow.Click += (_, _) => { Result.FollowSystemTheme = follow.IsChecked == true; Preview(); };
        Label(look, "进度条厚度");
        var barLabels = new[] { "跟随主题", "细 · 6", "中 · 14", "粗 · 20", "特粗 · 28" };
        var barValues = new[] { 0, 6, 14, 20, 28 };
        var bars = new ComboBox { ItemsSource = barLabels, SelectedIndex = Math.Max(0, Array.IndexOf(barValues, Result.BarHeight)) }; look.Children.Add(bars);
        bars.SelectionChanged += (_, _) => { Result.BarHeight = barValues[Math.Max(0, bars.SelectedIndex)]; Preview(); };
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
        Label(look, "背景图片");
        var imageRow = new StackPanel { Orientation = Orientation.Horizontal }; look.Children.Add(imageRow);
        var imageName = new TextBlock { FontSize = 11 * scale, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6 * Appearance.Scale, 0, 4 * Appearance.Scale) };
        imageName.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); look.Children.Add(imageName);
        void RefreshImageName()
        {
            if (Result.BackgroundImage == "") { imageName.Text = "当前没有背景图片，用的是主题底色。"; return; }
            imageName.Text = File.Exists(Result.BackgroundImage)
                ? "正在使用：" + Path.GetFileName(Result.BackgroundImage) + "\n文字要看清，把上面的不透明度和下面的遮罩一起调到合适为止。"
                : "找不到文件：" + Result.BackgroundImage + "\n换一张，或者点清除。";
        }
        void PickImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择背景图片", Filter = "图片 (*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|所有文件 (*.*)|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            Result.BackgroundImage = dialog.FileName; Preview(); RefreshImageName();
        }
        imageRow.Children.Add(B("选择图片…", PickImage));
        imageRow.Children.Add(B("清除", () => { Result.BackgroundImage = ""; Preview(); RefreshImageName(); }));
        imageRow.Children.Add(B("打开所在文件夹", () => { if (Result.BackgroundImage != "" && File.Exists(Result.BackgroundImage)) OpenFolder(Path.GetDirectoryName(Result.BackgroundImage)!); }));
        var scrimLabel = Label(look, "图片遮罩");
        var scrim = new Slider { Minimum = 0, Maximum = 95, TickFrequency = 5, IsSnapToTickEnabled = true, Value = Result.ImageScrim * 100, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) }; look.Children.Add(scrim);
        void ScrimChanged() { Result.ImageScrim = scrim.Value / 100; scrimLabel.Text = $"图片遮罩 · {scrim.Value:0}%（越大文字越清楚，越小越看得见图片）"; Preview(); }
        scrim.ValueChanged += (_, _) => ScrimChanged(); ScrimChanged();
        RefreshImageName();
        // ---------- 字体与文字 ----------
        var text = pages[1];
        Label(text, "文字分三档", 20);
        Label(text, "标题、正文、次要各管一层。字体和颜色留空就跟随基础设置或主题。", 11);
        Label(text, "基础字体");
        var systemFonts = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n).ToList();
        var fonts = new ComboBox { ItemsSource = systemFonts, SelectedItem = Result.FontName, MaxDropDownHeight = 260 }; text.Children.Add(fonts);
        fonts.SelectionChanged += (_, _) => { Result.FontName = fonts.SelectedItem as string ?? "Microsoft YaHei UI"; Preview(); };
        var sizeLabel = Label(text, "基础字号");
        var size = new Slider { Minimum = 9, Maximum = 36, TickFrequency = 1, IsSnapToTickEnabled = true, Value = Result.TextSize, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) }; text.Children.Add(size);
        var zoomLabel = Label(text, "界面缩放（文字和间距一起缩放）");
        var zoom = new Slider { Minimum = 80, Maximum = 200, TickFrequency = 5, IsSnapToTickEnabled = true, Value = Result.UiScale * 100, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) }; text.Children.Add(zoom);
        var fit = new TextBlock { FontSize = 11 * scale, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8 * Appearance.Scale) };
        fit.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); text.Children.Add(fit);
        var grow = new CheckBox { Content = "调整字号或界面缩放时，自动放大窗口（最多占屏幕工作区的一半）", IsChecked = Result.AutoGrowWindow, Margin = new Thickness(0, 4 * Appearance.Scale, 0, 8 * Appearance.Scale) }; text.Children.Add(grow);
        grow.Click += (_, _) => { Result.AutoGrowWindow = grow.IsChecked == true; Preview(); };

        // 每一档：字体、字号比例、颜色。颜色用对比度提醒兜底。
        void TierBlock(string name, string hint, Func<string> getFont, Action<string> setFont, Func<double> getScale, Action<double> setScale, Func<string> getColor, Action<string> setColor)
        {
            Label(text, name + " · " + hint, 15);
            var picker = new ComboBox { ItemsSource = new List<string> { "跟随基础字体" }.Concat(systemFonts).ToList(), SelectedIndex = 0, MaxDropDownHeight = 260 };
            var current = getFont();
            if (current != "") picker.SelectedItem = current;
            text.Children.Add(picker);
            picker.SelectionChanged += (_, _) => { setFont(picker.SelectedIndex <= 0 ? "" : picker.SelectedItem as string ?? ""); Preview(); };
            var scaleLabel = Label(text, "字号比例");
            var ratio = new Slider { Minimum = 60, Maximum = 200, TickFrequency = 5, IsSnapToTickEnabled = true, Value = getScale() * 100, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) };
            text.Children.Add(ratio);
            void RatioChanged() { setScale(ratio.Value / 100); scaleLabel.Text = $"字号比例 · {ratio.Value:0}%"; Preview(); }
            ratio.ValueChanged += (_, _) => RatioChanged(); RatioChanged();
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4 * Appearance.Scale, 0, 0) };
            var warn = new TextBlock { FontSize = 11 * scale, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4 * Appearance.Scale, 0, 6 * Appearance.Scale) };
            warn.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            void RefreshColor()
            {
                string chosen = getColor();
                if (chosen == "") { warn.Text = "颜色跟随主题。"; return; }
                string surface = Appearance.Current.Family == "经典" && Appearance.Current.Glass ? Appearance.Current.Card : Appearance.Current.Card;
                double ratioValue = Themes.ContrastRatio(chosen, surface);
                warn.Text = ratioValue < 2.5
                    ? $"对比度只有 {ratioValue:0.0}:1，压在这个主题的卡片底色上会看不清，建议换深一点或浅一点的颜色。"
                    : $"对比度 {ratioValue:0.0}:1，和卡片底色区分得开。";
            }
            colorRow.Children.Add(B("文字颜色…", () =>
            {
                var start = getColor() == "" ? Appearance.Current.Ink : getColor();
                using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(start) };
                if (dialog.ShowDialog(new DialogOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle)) != System.Windows.Forms.DialogResult.OK) return;
                setColor($"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}"); Preview(); RefreshColor();
            }));
            colorRow.Children.Add(B("恢复主题默认", () => { setColor(""); Preview(); RefreshColor(); }));
            text.Children.Add(colorRow); text.Children.Add(warn);
            RefreshColor();
        }
        TierBlock("标题", "论文标题、产品名", () => Result.TitleFont, v => Result.TitleFont = v, () => Result.TitleScale, v => Result.TitleScale = v, () => Result.TitleColor, v => Result.TitleColor = v);
        TierBlock("正文", "阶段标签、天数、下一步、按钮、表单", () => Result.BodyFont, v => Result.BodyFont = v, () => Result.BodyScale, v => Result.BodyScale = v, () => Result.BodyColor, v => Result.BodyColor = v);
        TierBlock("次要", "汇总行、页脚、提示文字", () => Result.CaptionFont, v => Result.CaptionFont = v, () => Result.CaptionScale, v => Result.CaptionScale = v, () => Result.CaptionColor, v => Result.CaptionColor = v);
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
        // ---------- 视图与分页 ----------
        var view = pages[2];
        Label(view, "列表怎么显示", 20);
        var bold = new CheckBox { Content = "论文标题加粗", IsChecked = Result.TitleBold, Margin = new Thickness(0, 6 * Appearance.Scale, 0, 6 * Appearance.Scale) }; view.Children.Add(bold);
        bold.Click += (_, _) => { Result.TitleBold = bold.IsChecked == true; Preview(); };
        var notices = new CheckBox { Content = "操作后在底部显示提示条", IsChecked = Result.ShowNotices, Margin = new Thickness(0, 6 * Appearance.Scale, 0, 6 * Appearance.Scale) }; view.Children.Add(notices);
        notices.Click += (_, _) => { Result.ShowNotices = notices.IsChecked == true; Preview(); };
        Label(view, "提示条只在这些时候出现：勾选阶段后论文被隐藏或被挪到别的页、归档、复制。正常的勾选不会弹。", 11);
        Label(view, "音效", 15);
        var soundMode = new ComboBox { ItemsSource = ViewRules.SoundModes, SelectedItem = Result.SoundMode }; view.Children.Add(soundMode);
        soundMode.SelectionChanged += (_, _) => { Result.SoundMode = soundMode.SelectedItem as string ?? ViewRules.SoundModes[0]; Preview(); };
        Label(view, "音色");
        var soundStyle = new ComboBox { ItemsSource = ViewRules.SoundStyles, SelectedItem = Result.SoundStyle }; view.Children.Add(soundStyle);
        soundStyle.SelectionChanged += (_, _) => { Result.SoundStyle = soundStyle.SelectedItem as string ?? ViewRules.SoundStyles[0]; Preview(); };
        var soundLabel = Label(view, "音量");
        var soundVolume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Value = Result.SoundVolume * 100, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) }; view.Children.Add(soundVolume);
        void VolumeChanged() { Result.SoundVolume = soundVolume.Value / 100; soundLabel.Text = $"音量 · {soundVolume.Value:0}%"; Preview(); }
        soundVolume.ValueChanged += (_, _) => VolumeChanged(); VolumeChanged();
        var soundRow = new StackPanel { Orientation = Orientation.Horizontal }; view.Children.Add(soundRow);
        soundRow.Children.Add(B("试听完成音", () => Chime.Play(Result, "complete")));
        soundRow.Children.Add(B("试听取消音", () => Chime.Play(Result, "undo")));
        soundRow.Children.Add(B("试听收录音", () => Chime.Play(Result, "reward")));
        Label(view, "音效默认关闭，只在本机生效、不随同步跑到别的电脑；勾满七个阶段时换成一小段奖励音。", 11);
        Label(view, "搜索、筛选、排序、紧凑视图、隐藏哪些阶段、翻页方式和显示范围都在挂件右上角的“论文选项”里，那里改的是此刻看到什么。这里只放长期偏好。", 11);
        Label(view, "字号、界面缩放和三档字体在“字体与文字”里。", 11);
        Label(view, "铺满屏幕时界面会不会挤，取决于字号和界面缩放的组合。字号很大时阶段标签会换行、卡片自然变高，一屏能看到的论文会变少，这是正常的。", 11);

        // ---------- 同步与启动 ----------
        var sync = pages[3];
        Label(sync, "论文同步与备份", 20);
        Label(sync, Result.SyncFolder == "" ? "当前只在本机保存。把安装文件夹放进任意会自动同步的网盘文件夹，再从那里的固定启动入口打开，就会自动连接旁边的 data 目录。" : "正在使用同步文件夹\n" + Result.SyncFolder, 11);
        if (Result.SyncFolder != "") sync.Children.Add(B("打开同步资料文件夹", () => OpenFolder(Result.SyncFolder)));
        Label(sync, "其他电脑收到文件后，小部件自动刷新；到达时间由你用的网盘客户端决定。字体、主题、字号、界面缩放和窗口位置只保存在本机。", 11);
        var backup = new StackPanel { Orientation = Orientation.Horizontal }; backup.Children.Add(B("导出备份", export)); backup.Children.Add(B("导入备份", import)); backup.Children.Add(B("本地资料", () => OpenFolder(directory))); sync.Children.Add(backup);
        Label(sync, "启动", 16);
        var startup = new CheckBox { Content = "登录 Windows 时自动打开", IsChecked = StartupEntry.IsEnabled(), Margin = new Thickness(0, 6 * Appearance.Scale, 0, 6 * Appearance.Scale) }; sync.Children.Add(startup);
        Label(sync, "× 收起到托盘，双击托盘图标恢复。更新时请先从托盘菜单退出，再打开固定启动入口。", 11);
        Label(sync, "更新通道和更新提示还没做（排在 1.6.0），现在更新靠网盘同步整个程序文件夹。", 11);

        // ---------- 关于与反馈 ----------
        var about = pages[4];
        Label(about, Product.Name, 20);
        Label(about, "版本 " + Product.Version + (Product.BuildCommit == "" ? " · 本地构建" : " · 提交 " + Product.BuildCommit), 12);
        Label(about, "MIT 许可 · Copyright (c) 2026 Panwang Yuang\n本程序不联网、不上传任何资料，论文数据只存在你自己的电脑和你选的同步文件夹里。", 11);
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
    // 主题列表的一行：三个色点做预览（底色 / 卡片 / 强调色），右边是名字。
    private static ListBoxItem ThemeRow(Theme theme)
    {
        var swatch = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        // 色点必须是每套主题自己的固定颜色，不能跟着当前主题变。
        foreach (var color in new[] { theme.Window, theme.Card, theme.Accent })
            swatch.Children.Add(new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Margin = new Thickness(0, 0, 3, 0), Fill = Appearance.Paint(color) });
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(swatch);
        line.Children.Add(new TextBlock { Text = theme.Name, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily(Appearance.FamilyFor("body")), FontSize = 12.5 * Appearance.DialogScale * Appearance.RoleScale("body") });
        if (theme.Dark)
        {
            var dark = new TextBlock { Text = "深色", FontFamily = new FontFamily(Appearance.FamilyFor("caption")), FontSize = 10 * Appearance.DialogScale * Appearance.RoleScale("caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            dark.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            line.Children.Add(dark);
        }
        return new ListBoxItem { Content = line, Tag = theme, Padding = new Thickness(6, 6, 6, 6), Background = Brushes.Transparent };
    }
    private static void OpenUrl(string target) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception) { } }
    private sealed class DialogOwner : System.Windows.Forms.IWin32Window { public IntPtr Handle { get; } public DialogOwner(IntPtr handle) { Handle = handle; } }
}
