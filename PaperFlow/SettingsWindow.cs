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
    // 用户在这个窗口里改过的标签名字：保存时要把论文上贴着的旧名字一起改掉。
    public List<(string From, string To)> TagRenames { get; } = new();
    // 方案改名同理：论文身上记着方案名，改名要一起跟过去。
    public List<(string From, string To)> SchemeRenames { get; } = new();
    private bool ready;
    // 分类名跟着语言走；用属性而不是静态字段，免得第一次取值时的语言被永久记住。
    private static string[] Categories => new[] { Lang.T("外观"), Lang.T("字体与文字"), Lang.T("视图与分页"), Lang.T("阶段方案"), Lang.T("同步与启动"), Lang.T("关于与反馈") };

    public SettingsWindow(Preferences settings, string directory, Action export, Action import, Action<Preferences> preview, Func<int> estimate, Func<string, int>? schemeUsage = null)
    {
        schemeUsage ??= (_ => 0);
        Result = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(settings))!;
        double scale = Appearance.DialogScale;
        // Inside a normal window the text stops at 250%, so the form always fits.
        Button B(string text, Action action, bool primary = false) => MainWindow.ActionButton(text, action, primary, scale);
        Title = Lang.T("设置");
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
        Label(look, Lang.T("让外观像你自己的桌面"), 20);
        Label(look, Lang.T("所有改动都会立刻作用在挂件上；点取消会恢复打开设置前的样子。实心预览是你正在用的那套。"), 11);
        Label(look, Lang.T("界面语言"));
        var languages = Lang.LanguageChoices();
        var languagePicker = new ComboBox { ItemsSource = languages, DisplayMemberPath = "Label", SelectedIndex = Result.Language switch { Lang.Chinese => 1, Lang.English => 2, _ => 0 }, Margin = new Thickness(0, 5, 0, 6) };
        languagePicker.SelectionChanged += (_, _) => Result.Language = languages[Math.Clamp(languagePicker.SelectedIndex, 0, 2)].Value;
        look.Children.Add(languagePicker);
        Label(look, Lang.T("跟随系统就是看 Windows 的显示语言；保存后挂件会自动重启一次换成新语言，你的资料一个字都不动。"), 11);
        Label(look, Lang.T("布局"));
        var layoutPicker = new ComboBox { ItemsSource = Lang.Choices(Themes.Layouts), DisplayMemberPath = "Label", SelectedIndex = Math.Max(0, Array.IndexOf(Themes.Layouts, Result.ListLayout)) }; look.Children.Add(layoutPicker);
        layoutPicker.SelectionChanged += (_, _) => { Result.ListLayout = Themes.Layouts[Math.Max(0, layoutPicker.SelectedIndex)]; Preview(); };
        Label(look, Lang.F("主题（按风格分组，共 {0} 套）", Themes.All.Length));
        var themeList = new ListBox { MaxHeight = 280 * scale, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        ListBoxItem? selected = null;
        foreach (var group in Themes.Grouped())
        {
            var heading = new ListBoxItem { Content = Lang.T(group.Key), IsEnabled = false, Focusable = false, FontFamily = new FontFamily(Appearance.FamilyFor("caption")), FontSize = 11.5 * scale * Appearance.RoleScale("caption"), Padding = new Thickness(2, 10 * Appearance.Scale, 0, 4 * Appearance.Scale), Background = Brushes.Transparent };
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
        var follow = new CheckBox { Content = Lang.T("跟随 Windows 的浅色/深色设置"), IsChecked = Result.FollowSystemTheme, Margin = new Thickness(0, 8 * Appearance.Scale, 0, 4 * Appearance.Scale) }; look.Children.Add(follow);
        follow.Click += (_, _) => { Result.FollowSystemTheme = follow.IsChecked == true; Preview(); };
        Label(look, Lang.T("进度条厚度"));
        var barLabels = new[] { Lang.T("跟随主题"), Lang.T("细 · 6"), Lang.T("中 · 14"), Lang.T("粗 · 20"), Lang.T("特粗 · 28") };
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
        colors.Children.Add(B(Lang.T("主色…"), () => Pick(true))); colors.Children.Add(B(Lang.T("底色…"), () => Pick(false)));
        colors.Children.Add(B(Lang.T("恢复主题色"), () => { Result.AccentColor = ""; Result.BackgroundColor = ""; Preview(); }));
        var opacityLabel = Label(look, Lang.T("背景不透明度"));
        var opacity = new Slider { Minimum = 5, Maximum = 100, Value = Result.BackgroundOpacity * 100, TickFrequency = 5, IsSnapToTickEnabled = true, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) }; look.Children.Add(opacity);
        void OpacityChanged() { Result.BackgroundOpacity = opacity.Value / 100; opacityLabel.Text = Lang.F("背景不透明度 · {0:0}%（文字保持清晰）", opacity.Value); Preview(); }
        opacity.ValueChanged += (_, _) => OpacityChanged(); OpacityChanged();
        Label(look, Lang.T("背景图片"));
        var imageRow = new StackPanel { Orientation = Orientation.Horizontal }; look.Children.Add(imageRow);
        var imageName = new TextBlock { FontSize = 11 * scale, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6 * Appearance.Scale, 0, 4 * Appearance.Scale) };
        imageName.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); look.Children.Add(imageName);
        void RefreshImageName()
        {
            if (Result.BackgroundImage == "") { imageName.Text = Lang.T("当前没有背景图片，用的是主题底色。"); return; }
            imageName.Text = File.Exists(Result.BackgroundImage)
                ? Lang.T("正在使用：") + Path.GetFileName(Result.BackgroundImage) + Lang.T("\n文字要看清，把上面的不透明度和下面的遮罩一起调到合适为止。")
                : Lang.T("找不到文件：") + Result.BackgroundImage + Lang.T("\n换一张，或者点清除。");
        }
        void PickImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = Lang.T("选择背景图片"), Filter = Lang.T("图片 (*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|所有文件 (*.*)|*.*") };
            if (dialog.ShowDialog(this) != true) return;
            Result.BackgroundImage = dialog.FileName; Preview(); RefreshImageName();
        }
        imageRow.Children.Add(B(Lang.T("选择图片…"), PickImage));
        imageRow.Children.Add(B(Lang.T("清除"), () => { Result.BackgroundImage = ""; Preview(); RefreshImageName(); }));
        imageRow.Children.Add(B(Lang.T("打开所在文件夹"), () => { if (Result.BackgroundImage != "" && File.Exists(Result.BackgroundImage)) OpenFolder(Path.GetDirectoryName(Result.BackgroundImage)!); }));
        var scrimLabel = Label(look, Lang.T("图片遮罩"));
        var scrim = new Slider { Minimum = 0, Maximum = 95, TickFrequency = 5, IsSnapToTickEnabled = true, Value = Result.ImageScrim * 100, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) }; look.Children.Add(scrim);
        void ScrimChanged() { Result.ImageScrim = scrim.Value / 100; scrimLabel.Text = Lang.F("图片遮罩 · {0:0}%（越大文字越清楚，越小越看得见图片）", scrim.Value); Preview(); }
        scrim.ValueChanged += (_, _) => ScrimChanged(); ScrimChanged();
        RefreshImageName();
        // ---------- 字体与文字 ----------
        var text = pages[1];
        Label(text, Lang.T("文字分三档"), 20);
        Label(text, Lang.T("标题、正文、次要各管一层。字体和颜色留空就跟随基础设置或主题。"), 11);
        Label(text, Lang.T("基础字体"));
        var systemFonts = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n).ToList();
        var fonts = new ComboBox { ItemsSource = systemFonts, SelectedItem = Result.FontName, MaxDropDownHeight = 260 }; text.Children.Add(fonts);
        fonts.SelectionChanged += (_, _) => { Result.FontName = fonts.SelectedItem as string ?? "Microsoft YaHei UI"; Preview(); };
        var sizeLabel = Label(text, Lang.T("基础字号"));
        var size = new Slider { Minimum = 9, Maximum = 36, TickFrequency = 1, IsSnapToTickEnabled = true, Value = Result.TextSize, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) }; text.Children.Add(size);
        var zoomLabel = Label(text, Lang.T("界面缩放（文字和间距一起缩放）"));
        var zoom = new Slider { Minimum = 80, Maximum = 200, TickFrequency = 5, IsSnapToTickEnabled = true, Value = Result.UiScale * 100, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) }; text.Children.Add(zoom);
        var fit = new TextBlock { FontSize = 11 * scale, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8 * Appearance.Scale) };
        fit.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); text.Children.Add(fit);
        var grow = new CheckBox { Content = Lang.T("调整字号或界面缩放时，自动放大窗口（最多占屏幕工作区的一半）"), IsChecked = Result.AutoGrowWindow, Margin = new Thickness(0, 4 * Appearance.Scale, 0, 8 * Appearance.Scale) }; text.Children.Add(grow);
        grow.Click += (_, _) => { Result.AutoGrowWindow = grow.IsChecked == true; Preview(); };

        // 每一档：字体、字号比例、颜色。颜色用对比度提醒兜底。
        void TierBlock(string name, string hint, Func<string> getFont, Action<string> setFont, Func<double> getScale, Action<double> setScale, Func<string> getColor, Action<string> setColor)
        {
            Label(text, name + " · " + hint, 15);
            var picker = new ComboBox { ItemsSource = new List<string> { Lang.T("跟随基础字体") }.Concat(systemFonts).ToList(), SelectedIndex = 0, MaxDropDownHeight = 260 };
            var current = getFont();
            if (current != "") picker.SelectedItem = current;
            text.Children.Add(picker);
            picker.SelectionChanged += (_, _) => { setFont(picker.SelectedIndex <= 0 ? "" : picker.SelectedItem as string ?? ""); Preview(); };
            var scaleLabel = Label(text, Lang.T("字号比例"));
            var ratio = new Slider { Minimum = 60, Maximum = 200, TickFrequency = 5, IsSnapToTickEnabled = true, Value = getScale() * 100, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) };
            text.Children.Add(ratio);
            void RatioChanged() { setScale(ratio.Value / 100); scaleLabel.Text = Lang.F("字号比例 · {0:0}%", ratio.Value); Preview(); }
            ratio.ValueChanged += (_, _) => RatioChanged(); RatioChanged();
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4 * Appearance.Scale, 0, 0) };
            var warn = new TextBlock { FontSize = 11 * scale, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4 * Appearance.Scale, 0, 6 * Appearance.Scale) };
            warn.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            void RefreshColor()
            {
                string chosen = getColor();
                if (chosen == "") { warn.Text = Lang.T("颜色跟随主题。"); return; }
                string surface = Appearance.Current.Family == "经典" && Appearance.Current.Glass ? Appearance.Current.Card : Appearance.Current.Card;
                double ratioValue = Themes.ContrastRatio(chosen, surface);
                warn.Text = ratioValue < 2.5
                    ? Lang.F("对比度只有 {0:0.0}:1，压在这个主题的卡片底色上会看不清，建议换深一点或浅一点的颜色。", ratioValue)
                    : Lang.F("对比度 {0:0.0}:1，和卡片底色区分得开。", ratioValue);
            }
            colorRow.Children.Add(B(Lang.T("文字颜色…"), () =>
            {
                var start = getColor() == "" ? Appearance.Current.Ink : getColor();
                using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(start) };
                if (dialog.ShowDialog(new DialogOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle)) != System.Windows.Forms.DialogResult.OK) return;
                setColor($"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}"); Preview(); RefreshColor();
            }));
            colorRow.Children.Add(B(Lang.T("恢复主题默认"), () => { setColor(""); Preview(); RefreshColor(); }));
            text.Children.Add(colorRow); text.Children.Add(warn);
            RefreshColor();
        }
        TierBlock(Lang.T("标题"), Lang.T("论文标题、产品名"), () => Result.TitleFont, v => Result.TitleFont = v, () => Result.TitleScale, v => Result.TitleScale = v, () => Result.TitleColor, v => Result.TitleColor = v);
        TierBlock(Lang.T("正文"), Lang.T("阶段标签、天数、下一步、按钮、表单"), () => Result.BodyFont, v => Result.BodyFont = v, () => Result.BodyScale, v => Result.BodyScale = v, () => Result.BodyColor, v => Result.BodyColor = v);
        TierBlock(Lang.T("次要"), Lang.T("汇总行、页脚、提示文字"), () => Result.CaptionFont, v => Result.CaptionFont = v, () => Result.CaptionScale, v => Result.CaptionScale = v, () => Result.CaptionColor, v => Result.CaptionColor = v);
        void RefreshFit()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                int count = estimate();
                fit.Text = count > 0 ? Lang.P(count, "按当前窗口和字号，大约能完整显示 {0} 篇论文。", "About {0} card fits whole at this window and text size.", "About {0} cards fit whole at this window and text size.", count) : Lang.T("当前窗口放不下整张卡片，可以把窗口调大或把缩放调小。");
            }), DispatcherPriority.Loaded);
        }
        void SizeChanged() { Result.TextSize = Math.Round(size.Value); sizeLabel.Text = Lang.F("字号 · {0:0}（只改文字大小，不动间距）", size.Value); Preview(); RefreshFit(); }
        size.ValueChanged += (_, _) => SizeChanged(); SizeChanged();
        void ZoomChanged() { Result.UiScale = zoom.Value / 100; zoomLabel.Text = Lang.F("界面缩放 · {0:0}%（文字和间距一起缩放）", zoom.Value); Preview(); RefreshFit(); }
        zoom.ValueChanged += (_, _) => ZoomChanged(); ZoomChanged();
        // ---------- 视图与分页 ----------
        var view = pages[2];
        Label(view, Lang.T("列表怎么显示"), 20);
        var bold = new CheckBox { Content = Lang.T("论文标题加粗"), IsChecked = Result.TitleBold, Margin = new Thickness(0, 6 * Appearance.Scale, 0, 6 * Appearance.Scale) }; view.Children.Add(bold);
        bold.Click += (_, _) => { Result.TitleBold = bold.IsChecked == true; Preview(); };
        var notices = new CheckBox { Content = Lang.T("操作后在底部显示提示条"), IsChecked = Result.ShowNotices, Margin = new Thickness(0, 6 * Appearance.Scale, 0, 6 * Appearance.Scale) }; view.Children.Add(notices);
        notices.Click += (_, _) => { Result.ShowNotices = notices.IsChecked == true; Preview(); };
        Label(view, Lang.T("提示条只在这些时候出现：勾选阶段后论文被隐藏或被挪到别的页、归档、复制。正常的勾选不会弹。"), 11);
        Label(view, Lang.T("音效"), 15);
        var soundMode = new ComboBox { ItemsSource = Lang.Choices(ViewRules.SoundModes), DisplayMemberPath = "Label", SelectedIndex = Math.Max(0, Array.IndexOf(ViewRules.SoundModes, Result.SoundMode)) }; view.Children.Add(soundMode);
        soundMode.SelectionChanged += (_, _) => { Result.SoundMode = ViewRules.SoundModes[Math.Max(0, soundMode.SelectedIndex)]; Preview(); };
        Label(view, Lang.T("音色"));
        var soundStyle = new ComboBox { ItemsSource = Lang.Choices(ViewRules.SoundStyles), DisplayMemberPath = "Label", SelectedIndex = Math.Max(0, Array.IndexOf(ViewRules.SoundStyles, Result.SoundStyle)) }; view.Children.Add(soundStyle);
        soundStyle.SelectionChanged += (_, _) => { Result.SoundStyle = ViewRules.SoundStyles[Math.Max(0, soundStyle.SelectedIndex)]; Preview(); };
        var soundLabel = Label(view, Lang.T("音量"));
        var soundVolume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Value = Result.SoundVolume * 100, Margin = new Thickness(0, 5 * Appearance.Scale, 0, 7 * Appearance.Scale) }; view.Children.Add(soundVolume);
        void VolumeChanged() { Result.SoundVolume = soundVolume.Value / 100; soundLabel.Text = Lang.F("音量 · {0:0}%", soundVolume.Value); Preview(); }
        soundVolume.ValueChanged += (_, _) => VolumeChanged(); VolumeChanged();
        var soundRow = new StackPanel { Orientation = Orientation.Horizontal }; view.Children.Add(soundRow);
        soundRow.Children.Add(B(Lang.T("试听完成音"), () => Chime.Play(Result, "complete")));
        soundRow.Children.Add(B(Lang.T("试听取消音"), () => Chime.Play(Result, "undo")));
        soundRow.Children.Add(B(Lang.T("试听收录音"), () => Chime.Play(Result, "reward")));
        Label(view, Lang.T("音效默认关闭，只在本机生效、不随同步跑到别的电脑；勾满七个阶段时换成一小段奖励音。"), 11);
        // ---------- 隐藏用标签 ----------
        Label(view, Lang.T("隐藏用标签"), 16);
        Label(view, Lang.T("标签是你自己起的短记号，比如“等老师反馈”“等编辑部意见”。给论文贴上标签以后，可以把带某个标签的论文先收起来，需要的时候再展开看一眼。"), 11);
        var tagSwitch = new CheckBox { Content = Lang.T("打开隐藏用标签"), IsChecked = Result.TagHidingEnabled, Margin = new Thickness(0, 6 * Appearance.Scale, 0, 6 * Appearance.Scale) };
        tagSwitch.Click += (_, _) => Result.TagHidingEnabled = tagSwitch.IsChecked == true;
        view.Children.Add(tagSwitch);
        var tagRows = new StackPanel(); view.Children.Add(tagRows);
        var tagInput = new TextBox { Width = 170 * scale, MaxLength = 24, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8 * Appearance.Scale, 0) };
        var tagAdd = B(Lang.T("新建标签"), () => { });
        var tagAddRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6 * Appearance.Scale, 0, 4 * Appearance.Scale) };
        tagAddRow.Children.Add(tagInput); tagAddRow.Children.Add(tagAdd); view.Children.Add(tagAddRow);
        void RebuildTags()
        {
            tagRows.Children.Clear();
            foreach (var tag in Result.CustomTags.ToList())
            {
                string original = tag;
                var box = new TextBox { Text = tag, Width = 170 * scale, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8 * Appearance.Scale, 0) };
                void Commit()
                {
                    string wanted = (box.Text ?? "").Trim();
                    if (wanted == original) return;
                    if (!Schemes.IsValidTagName(wanted)) { MessageBox.Show(this, Lang.T("标签名不能为空，也不能超过六个汉字那么宽。")); RebuildTags(); return; }
                    if (Result.CustomTags.Contains(wanted)) { MessageBox.Show(this, Lang.T("已经有同名的标签了。")); RebuildTags(); return; }
                    int at = Result.CustomTags.IndexOf(original);
                    if (at >= 0) Result.CustomTags[at] = wanted;
                    for (int i = 0; i < Result.HiddenTags.Count; i++) if (Result.HiddenTags[i] == original) Result.HiddenTags[i] = wanted;
                    TagRenames.Add((original, wanted));
                    original = wanted; RebuildTags();
                }
                box.LostFocus += (_, _) => Commit();
                box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(); };
                var remove = B(Lang.T("删掉"), () => { Result.CustomTags.Remove(original); Result.HiddenTags.Remove(original); RebuildTags(); });
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2 * Appearance.Scale, 0, 2 * Appearance.Scale) };
                row.Children.Add(box); row.Children.Add(remove); tagRows.Children.Add(row);
            }
            if (Result.CustomTags.Count == 0) tagRows.Children.Add(new TextBlock { Text = Lang.T("还没有标签。"), Foreground = MainWindow.Brush("#78867F"), FontSize = 11 * scale });
        }
        tagAdd.Click += (_, _) =>
        {
            string wanted = (tagInput.Text ?? "").Trim();
            if (wanted.Length == 0) return;
            if (!Schemes.IsValidTagName(wanted)) { MessageBox.Show(this, Lang.T("标签名不能为空，也不能超过六个汉字那么宽。")); return; }
            if (Result.CustomTags.Contains(wanted)) { MessageBox.Show(this, Lang.T("已经有同名的标签了。")); return; }
            if (Result.CustomTags.Count >= Schemes.MaxCustomTags) { MessageBox.Show(this, Lang.F("自建标签最多 {0} 个。", Schemes.MaxCustomTags)); return; }
            Result.CustomTags.Add(wanted); tagInput.Clear(); RebuildTags();
        };
        RebuildTags();
        Label(view, Lang.T("搜索、筛选、排序、紧凑视图、隐藏用标签和显示范围都在挂件右上角的“论文选项”里，那里改的是此刻看到什么。这里只放长期偏好。"), 11);
        Label(view, Lang.T("字号、界面缩放和三档字体在“字体与文字”里。"), 11);
        Label(view, Lang.T("铺满屏幕时界面会不会挤，取决于字号和界面缩放的组合。字号很大时阶段标签会换行、卡片自然变高，一屏能看到的论文会变少，这是正常的。"), 11);

        // ---------- 同步与启动 ----------
        // ---------- 阶段方案 ----------
        var schemes = pages[3];
        Label(schemes, Lang.T("阶段方案"), 20);
        Label(schemes, Lang.T("一套方案就是一串有序的阶段。内置两套可以直接用；自己的方案可以随便加、改名、删，也可以按住卡片左边的点拖动排序。换方案时同名阶段会保留勾选，对不上的会变回未勾。"), 11);
        // schemeRows 先声明：下面几个按钮的 lambda 会调用 RebuildSchemes。
        var schemeRows = new StackPanel(); schemes.Children.Add(schemeRows);
        Label(schemes, Lang.T("内置方案"), 16);
        foreach (var built in Schemes.BuiltIn)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2 * Appearance.Scale, 0, 2 * Appearance.Scale) };
            row.Children.Add(new TextBlock { Text = Lang.T(built.Name) + " · " + string.Join(Lang.ListSeparator, built.StageNames.Select(n => Lang.T(Schemes.Display(n)))), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12 * Appearance.Scale, 0), TextWrapping = TextWrapping.Wrap });
            row.Children.Add(B(Lang.T("复制一份再改"), () =>
            {
                var prompt = new TextPrompt(Lang.T("复制一份再改"), Lang.T("新方案的名字"), Lang.T(built.Name) + Lang.T(" 副本")) { Owner = this };
                if (prompt.ShowDialog() != true) return;
                if (!AddScheme(prompt.Value, built.StageNames)) return;
                RebuildSchemes();
            }));
            schemes.Children.Add(row);
        }
        Label(schemes, Lang.T("我自己的方案"), 16);
        var newScheme = B(Lang.T("新建方案…"), () =>
        {
            var prompt = new TextPrompt(Lang.T("新建方案"), Lang.T("新方案的名字"), "") { Owner = this };
            if (prompt.ShowDialog() != true) return;
            // 从"标准七步"起手，改起来比对着空列表快。
            if (!AddScheme(prompt.Value, Schemes.Default.StageNames)) return;
            RebuildSchemes();
        });
        schemes.Children.Add(newScheme);
        Label(schemes, Lang.T("内置两套不能改名、也不能删；想改就先复制一份。正在被论文使用的方案删不掉，得先给那些论文换一套。"), 11);
        void RebuildSchemes()
        {
            schemeRows.Children.Clear();
            foreach (var scheme in Result.CustomSchemes.ToList())
            {
                var current = scheme;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2 * Appearance.Scale, 0, 2 * Appearance.Scale) };
                row.Children.Add(new TextBlock { Text = Lang.T(current.Name) + " · " + string.Join(Lang.ListSeparator, current.StageNames.Select(n => Lang.T(Schemes.Display(n)))), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12 * Appearance.Scale, 0), TextWrapping = TextWrapping.Wrap });
                row.Children.Add(B(Lang.T("编辑…"), () =>
                {
                    var editor = new StageEditorDialog(Lang.T("阶段方案"), Lang.T("改名、加阶段、删阶段、按住拖动排序，都在这里。"), current.Name, current.StageNames, true, name => Result.CustomSchemes.Any(s => s.Name == name && s.Name != current.Name)) { Owner = this };
                    if (editor.ShowDialog() != true) return;
                    int at = Result.CustomSchemes.IndexOf(current);
                    if (at < 0) return;
                    if (editor.ResultSchemeName != current.Name) SchemeRenames.Add((current.Name, editor.ResultSchemeName));
                    Result.CustomSchemes[at] = new StageScheme(editor.ResultSchemeName, editor.ResultStages);
                    RebuildSchemes();
                }));
                row.Children.Add(B(Lang.T("删掉"), () =>
                {
                    int used = schemeUsage(current.Name);
                    if (used > 0) { MessageBox.Show(this, Lang.F("还有 {0} 篇论文在用《{1}》，先给它们换一个方案，再删这个方案。", used, Lang.T(current.Name))); return; }
                    Result.CustomSchemes.Remove(current); RebuildSchemes();
                }));
                schemeRows.Children.Add(row);
            }
            if (Result.CustomSchemes.Count == 0) schemeRows.Children.Add(new TextBlock { Text = Lang.T("还没有自己的方案。"), Foreground = MainWindow.Brush("#78867F"), FontSize = 11 * scale });
        }
        bool AddScheme(string rawName, IReadOnlyList<string> stageNames)
        {
            string name = (rawName ?? "").Trim();
            if (!Schemes.IsValidSchemeName(name)) { MessageBox.Show(this, Storage.Why(SchemeProblem.NameTooWide)); return false; }
            if (Schemes.IsBuiltIn(name) || Result.CustomSchemes.Any(s => s.Name == name)) { MessageBox.Show(this, Lang.T("已经有同名的方案了。")); return false; }
            Result.CustomSchemes.Add(new StageScheme(name, stageNames.ToList()));
            return true;
        }
        RebuildSchemes();

        var sync = pages[4];
        Label(sync, Lang.T("论文同步与备份"), 20);
        // 不假定任何人一定在用同步文件夹：用着就显示地址，然后一句 tip 说清它能做什么。
        if (Result.SyncFolder != "") Label(sync, Lang.T("正在使用同步文件夹\n") + Result.SyncFolder, 11);
        Label(sync, Lang.T("如果你使用 OneDrive / 百度网盘 / Dropbox / 坚果云 等同步文件夹，在同一个文件夹下，其他电脑收到后会同步你的设置哦。"), 11);
        var backup = new StackPanel { Orientation = Orientation.Horizontal };
        if (Result.SyncFolder != "") backup.Children.Add(B(Lang.T("打开同步资料文件夹"), () => OpenFolder(Result.SyncFolder)));
        backup.Children.Add(B(Lang.T("导出备份"), export)); backup.Children.Add(B(Lang.T("导入备份"), import)); backup.Children.Add(B(Lang.T("本地资料"), () => OpenFolder(directory))); sync.Children.Add(backup);
        Label(sync, Lang.T("启动"), 16);
        var startup = new CheckBox { Content = Lang.T("登录 Windows 时自动打开"), IsChecked = StartupEntry.IsEnabled(), Margin = new Thickness(0, 6 * Appearance.Scale, 0, 6 * Appearance.Scale) }; sync.Children.Add(startup);
        Label(sync, Lang.T("× 收起到托盘，双击托盘图标恢复。更新时请先从托盘菜单退出，再打开固定启动入口。"), 11);
        Label(sync, Lang.T("更新"), 16);
        Label(sync, Lang.T("更新提示"));
        var updateChoices = new List<Choice> { new("always", Lang.T("每次都提示")), new("daily", Lang.T("每天一次")), new("never", Lang.T("不提示")) };
        var updateMode = new ComboBox { ItemsSource = updateChoices, DisplayMemberPath = "Label", SelectedIndex = Math.Max(0, updateChoices.FindIndex(c => c.Value == Result.UpdateMode)), Margin = new Thickness(0, 5, 0, 6) };
        updateMode.SelectionChanged += (_, _) => Result.UpdateMode = updateChoices[Math.Clamp(updateMode.SelectedIndex, 0, 2)].Value;
        sync.Children.Add(updateMode);
        // 上次是成功还是失败都写清楚：失败时把原因留在这一行，用户不用去猜。
        var updateStatus = Label(sync, Result.LastUpdateError != "" && Result.LastUpdateCheckUtc is DateTime failed
            ? Lang.F("上次检查没成功（{0:yyyy-MM-dd HH:mm}）：{1}", failed.ToLocalTime(), Result.LastUpdateError)
            : Result.LastUpdateCheckUtc is DateTime last ? Lang.F("上次检查：{0:yyyy-MM-dd HH:mm}", last.ToLocalTime()) : Lang.T("还没检查过"), 11);
        var checkNow = B(Lang.T("现在检查一次"), () => { });
        checkNow.Click += async (_, _) =>
        {
            checkNow.IsEnabled = false; updateStatus.Text = Lang.T("正在检查…");
            try
            {
                using var client = Updates.Client();
                var manifest = await Updates.FetchAsync(client, System.Threading.CancellationToken.None);
                Result.LastUpdateCheckUtc = DateTime.UtcNow; Result.LastUpdateError = "";
                Updates.Offered = manifest;
                Updates.LastFailure = null;
                updateStatus.Text = manifest == null ? Lang.F("已经是最新版（{0}）。", Product.Version) : Lang.F("发现新版本 {0}：切回挂件就能下载。", manifest.Version);
            }
            catch (Exception ex)
            {
                var failure = Updates.Describe(ex);
                Result.LastUpdateCheckUtc = DateTime.UtcNow; Result.LastUpdateError = failure.Message;
                Updates.Offered = null; Updates.LastFailure = failure;
                updateStatus.Text = failure.Network
                    ? Lang.F("这次没连上更新服务器（{0}）", failure.Message)
                    : Lang.F("这次检查没成功：{0}", failure.Message);
            }
            finally { checkNow.IsEnabled = true; }
        };
        sync.Children.Add(checkNow);
        Label(sync, Lang.T("检查更新只读一份静态清单（国内镜像 Gitee 优先，其次是 GitHub），只下载、不上传，论文数据不会被发送出去。选“不提示”就一次网络请求都不发，那时也可以随时按这个按钮手动检查。"), 11);
        Label(sync, Lang.T("快捷方式"), 16);
        Label(sync, Shortcuts.HasLauncher(Result.LauncherPath)
            ? Lang.T("挂件本身不进任务栏，用这两个入口打开最省事。它们指向固定启动入口，以后换了版本也不用重建。")
            : Lang.T("这台电脑上还没找到固定启动入口（说明现在是从程序文件直接打开的），快捷方式会先指向当前这个程序文件。以后用固定启动入口打开一次、再回来重新勾选一次，会更稳妥。"), 11);
        var shortcutDesktop = new CheckBox { Content = Lang.T("桌面上放一个入口"), IsChecked = Shortcuts.Exists(Shortcuts.DesktopPath()), Margin = new Thickness(0, 6 * Appearance.Scale, 0, 6 * Appearance.Scale) };
        var shortcutStart = new CheckBox { Content = Lang.T("开始菜单里放一个入口"), IsChecked = Shortcuts.Exists(Shortcuts.StartMenuPath()), Margin = new Thickness(0, 0, 0, 6 * Appearance.Scale) };
        sync.Children.Add(shortcutDesktop); sync.Children.Add(shortcutStart);
        if (Product.Portable)
        {
            // 试用版不该动正式版在用的开机启动和快捷方式：直接把这两处关掉并说明原因。
            startup.IsEnabled = false; shortcutDesktop.IsEnabled = false; shortcutStart.IsEnabled = false;
            Label(sync, Lang.T("试用版不改动开机启动和桌面、开始菜单快捷方式，免得覆盖你正式在用的那套入口。"), 11);
        }
        Label(sync, Lang.T("勾上表示放好，取消勾选表示移除，保存设置时生效；不勾也不影响程序运行。任务栏图标不能由程序自己钉：右键上面那个快捷方式，选“固定到任务栏”就留在任务栏上了。"), 11);
        sync.Children.Add(B(Lang.T("打开程序文件夹"), () => OpenFolder(Shortcuts.ProgramFolder(Result.LauncherPath))));

        // ---------- 关于与反馈 ----------
        var about = pages[5];
        Label(about, Product.Name, 20);
        Label(about, Lang.F("版本 {0}", Product.Version), 12);
        Label(about, Lang.T("MIT 许可 · Copyright (c) 2026 Panwang Yuang\n不需要注册账号，论文数据只存在你自己的电脑上。程序只在检查更新时联网，而且只下载、不上传。"), 11);
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6 * Appearance.Scale, 0, 0) };
        links.Children.Add(B(Lang.T("打开主页"), () => OpenUrl("https://panwangyuang.com")));
        links.Children.Add(B(Lang.T("开 GitHub issue"), () => OpenUrl("https://github.com/pwya/paperflow/issues/new")));
        links.Children.Add(B(Lang.T("写邮件反馈"), () => OpenUrl("mailto:pwya1998@126.com?subject=PaperFlow%20%E5%8F%8D%E9%A6%88")));
        links.Children.Add(B(Lang.T("复制邮箱"), () => { try { Clipboard.SetText("pwya1998@126.com"); } catch (Exception) { } }));
        about.Children.Add(links);
        Label(about, Lang.T("遇到问题发邮件到 pwya1998@126.com，或在 GitHub 上开 issue（截图和文字都可以），两条路我都会看。"), 11);

        var cancel = B(Lang.T("取消"), () => DialogResult = false); cancel.IsCancel = true; buttons.Children.Add(cancel);
        buttons.Children.Add(B(Lang.T("保存设置"), () =>
        {
            try
            {
                if ((startup.IsChecked == true) != StartupEntry.IsEnabled()) StartupEntry.Write(startup.IsChecked == true, Result.LauncherPath);
                bool desktop = shortcutDesktop.IsChecked == true, menu = shortcutStart.IsChecked == true;
                // 只在和现状不一样时动手，免得每次保存都重写一遍快捷方式。
                if (desktop != Shortcuts.Exists(Shortcuts.DesktopPath()) || menu != Shortcuts.Exists(Shortcuts.StartMenuPath())) Shortcuts.Apply(desktop, menu, Result.LauncherPath);
                DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, Lang.T("设置未能保存。\n") + ex.Message); }
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
        line.Children.Add(new TextBlock { Text = Lang.T(theme.Name), VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily(Appearance.FamilyFor("body")), FontSize = 12.5 * Appearance.DialogScale * Appearance.RoleScale("body") });
        if (theme.Dark)
        {
            var dark = new TextBlock { Text = Lang.T("深色"), FontFamily = new FontFamily(Appearance.FamilyFor("caption")), FontSize = 10 * Appearance.DialogScale * Appearance.RoleScale("caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            dark.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            line.Children.Add(dark);
        }
        return new ListBoxItem { Content = line, Tag = theme, Padding = new Thickness(6, 6, 6, 6), Background = Brushes.Transparent };
    }
    private static void OpenUrl(string target) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception) { } }
    private sealed class DialogOwner : System.Windows.Forms.IWin32Window { public IntPtr Handle { get; } public DialogOwner(IntPtr handle) { Handle = handle; } }
}
