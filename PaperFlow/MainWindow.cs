using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace PaperFlow;

public sealed class MainWindow : Window
{
    private readonly Storage store;
    private readonly SyncEngine sync;
    private readonly bool demonstration;
    private Library library;
    private bool polling;
    private readonly Border frame = new();
    private readonly Border scrim = new();
    private readonly Button options = new();
    private readonly StackPanel cards = new();
    private readonly TextBlock summary = new();
    private readonly TextBlock footer = new();
    private readonly StackPanel pager = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 7) };
    private readonly TextBlock pageLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), FontSize = 12 };
    private readonly Border toast = new();
    private readonly TextBlock toastText = new();
    private readonly Button toastAction = new();
    private readonly Button toastClose = new();
    private readonly Border updateBar = new();
    private readonly TextBlock updateText = new();
    private readonly Button updateAction = new();
    private readonly Button updateClose = new();
    private Action? updateActionHandler;
    private UpdateManifest? updateOffered;
    private bool updating;
    private readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private Action? toastActionHandler;
    private readonly TextBox search = new();
    private readonly ComboBox filter = new();
    private readonly ComboBox sort = new();
    private readonly Button pin = new();
    private readonly Button compact = new();
    private readonly Button hiddenToggle = new();
    private readonly Image brandIcon = new() { Width = 26, Height = 26, Margin = new Thickness(0, 0, 9, 0) };
    private bool draggingPaper;
    private string? draggedPaperId;
    private const string PaperDragFormat = "PaperFlow.PaperId";
    private DateTime nextDragScroll;
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool quitting;
    private DateTime displayDate = DateTime.Today;
    private bool ready;
    private readonly ScrollViewer scroller;
    private Action? layoutChrome;
    private readonly List<Button> quietChrome = new();

    public static SolidColorBrush Brush(string color) => Appearance.Map(color);
    // role 决定用哪一档文字设置：title / body / caption。
    public static TextBlock Text(string text, double size = 13, string color = "#24352F", double scale = -1, string role = "body")
    {
        string custom = Appearance.RoleColor(role);
        return new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(Appearance.FamilyFor(role)),
            FontSize = size * Appearance.RoleScale(role) * (scale < 0 ? Appearance.TextScale : scale),
            Foreground = custom == "" ? Brush(color) : Appearance.Paint(custom),
            VerticalAlignment = VerticalAlignment.Center
        };
    }
    public static Button ActionButton(string text, Action action, bool primary = false, double scale = -1)
    {
        var button = new Button { Content = text, FontFamily = new FontFamily(Appearance.FamilyFor("body")), FontSize = 12 * Appearance.RoleScale("body") * (scale < 0 ? Appearance.TextScale : scale), Margin = new Thickness(3, 0, 0, 0) };
        if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        button.Click += (_, _) => action();
        return button;
    }
    private static Grid Watermark(TextBox input, string hint)
    {
        var wrapper = new Grid(); wrapper.Children.Add(input);
        var label = Text(hint, 12, "#99A49B"); label.Margin = new Thickness(10, 0, 6, 0); label.IsHitTestVisible = false;
        wrapper.Children.Add(label);
        input.TextChanged += (_, _) => label.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        return wrapper;
    }

    public MainWindow(Storage storage, Library initial, SyncEngine synchronization, bool demonstration = false)
    {
        this.demonstration = demonstration;
        store = storage; library = initial; sync = synchronization;
        Title = "PaperFlow";
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));
        // A desktop widget lives on the wallpaper, not in the taskbar or Alt+Tab list.
        // It keeps running and stays reachable from the tray icon.
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; AllowsTransparency = true; Background = Brushes.Transparent;
        FontFamily = new FontFamily(library.Settings.FontName); FontSize = Appearance.EffectiveTextSize;
        MinWidth = 480; MinHeight = 400;
        var area = SystemParameters.WorkArea;
        Width = Math.Min(library.Settings.Width, area.Width); Height = Math.Min(library.Settings.Height, area.Height);
        Left = library.Settings.Left < 0 ? Math.Max(area.Left, area.Right - Width - 24) : Math.Clamp(library.Settings.Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = library.Settings.Top < 0 ? area.Top + Math.Max(0, (area.Height - Height) / 2) : Math.Clamp(library.Settings.Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        Topmost = library.Settings.Topmost;
        frame.CornerRadius = new CornerRadius(12); frame.BorderThickness = new Thickness(1);
        var surface = new Grid(); surface.Children.Add(frame); Content = surface;
        AddResizeHandles(surface);
        var root = new DockPanel { LastChildFill = true, Background = Brushes.Transparent };
        root.MouseLeftButtonDown += DragWidget;
        // 图片在最底层，上面盖一层主题色遮罩保证文字可读，再上面才是内容。
        scrim.CornerRadius = new CornerRadius(12); scrim.Visibility = Visibility.Collapsed; scrim.IsHitTestVisible = false;
        var layered = new Grid(); layered.Children.Add(scrim); layered.Children.Add(root);
        frame.Child = layered;

        var heading = new Grid { Margin = new Thickness(14, 8, 10, 5), Background = Brushes.Transparent, ToolTip = Lang.T("拖动标题栏或空白处移动小部件") };
        heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); heading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(brandIcon);
        var brandTitle = Text("PaperFlow", 18, "#24352F", -1, "title"); brandTitle.FontWeight = FontWeights.SemiBold; brand.Children.Add(brandTitle);
        heading.Children.Add(brand);
        var chrome = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var add = ActionButton("＋", AddPaper, true); add.ToolTip = Lang.T("新增论文 · Ctrl+N"); add.Padding = new Thickness(10 * Appearance.Scale, 4 * Appearance.Scale, 10 * Appearance.Scale, 4 * Appearance.Scale); add.FontSize = 17 * Appearance.TextScale; AutomationProperties.SetName(add, Lang.T("新增论文")); chrome.Children.Add(add);
        options.Content = Lang.T("论文选项"); options.Padding = new Thickness(8 * Appearance.Scale, 6 * Appearance.Scale, 8 * Appearance.Scale, 6 * Appearance.Scale); options.Margin = new Thickness(4, 0, 0, 0); options.Click += (_, _) => OpenOptions(); chrome.Children.Add(options); quietChrome.Add(options);
        pin.Padding = new Thickness(8 * Appearance.Scale, 6 * Appearance.Scale, 8 * Appearance.Scale, 6 * Appearance.Scale); pin.Click += (_, _) => TogglePin(); chrome.Children.Add(pin); quietChrome.Add(pin);
        var settingsButton = ActionButton(Lang.T("设置"), OpenSettings); chrome.Children.Add(settingsButton); quietChrome.Add(settingsButton);
        hiddenToggle.Padding = new Thickness(8 * Appearance.Scale, 6 * Appearance.Scale, 8 * Appearance.Scale, 6 * Appearance.Scale);
        hiddenToggle.Click += (_, _) => Commit(l => l.Settings.ShowHiddenNow = !l.Settings.ShowHiddenNow);
        chrome.Children.Add(hiddenToggle); quietChrome.Add(hiddenToggle);
        // No taskbar button means minimising would hide the widget with nowhere to return
        // from, so the only place-away control is the collapse-to-tray button.
        var close = ActionButton("×", Close); close.ToolTip = Lang.T("收起到系统托盘，双击托盘图标恢复"); chrome.Children.Add(close); quietChrome.Add(close);
        Grid.SetColumn(chrome, 1); heading.Children.Add(chrome);
        // Large text or a narrow window pushes the buttons onto a second row instead of
        // clipping the title. Nothing is hidden, the header just reflows.
        layoutChrome = () =>
        {
            if (heading.ActualWidth <= 0) return;
            bool stacked = brand.DesiredSize.Width + chrome.DesiredSize.Width + 12 > heading.ActualWidth;
            Grid.SetRow(chrome, stacked ? 1 : 0); Grid.SetColumn(chrome, stacked ? 0 : 1);
            chrome.HorizontalAlignment = stacked ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            chrome.Margin = stacked ? new Thickness(0, 5, 0, 0) : new Thickness(0);
        };
        heading.SizeChanged += (_, _) => layoutChrome();
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);

        var top = new StackPanel { Margin = new Thickness(17, 0, 17, 6), Background = Brushes.Transparent };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); top.Children.Add(summary);
        AutomationProperties.SetName(search, Lang.T("搜索论文、学科、期刊")); search.ToolTip = Lang.T("搜索论文、学科、期刊、合作者或备注");
        search.TextChanged += (_, _) => { if (ready) Render(); };
        filter.ItemsSource = new[] { Lang.T("全部论文"), Lang.T("进行中"), Lang.T("已收录"), Lang.T("已归档") }; filter.SelectedIndex = 0; filter.Margin = new Thickness(7, 0, 0, 0);
        filter.SelectionChanged += (_, _) => { if (ready) Render(); };
        // 显示按语言走，值还是原来的规范值，所以下面的索引逻辑一个字都不用改。
        sort.ItemsSource = Lang.Choices(ViewRules.SortModes); sort.DisplayMemberPath = "Label"; sort.SelectedIndex = Math.Max(0, Array.IndexOf(ViewRules.SortModes, library.Settings.SortMode)); sort.Margin = new Thickness(7, 0, 0, 0);
        // 排序方式会保存下来，否则重启就悄悄回到手动排序，用户会以为顺序没同步。
        sort.SelectionChanged += (_, _) => { if (ready && ViewRules.SortModes[Math.Max(0, sort.SelectedIndex)] != library.Settings.SortMode) Commit(l => l.Settings.SortMode = ViewRules.SortModes[Math.Max(0, sort.SelectedIndex)]); };
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);

        var foot = new Border { Padding = new Thickness(20, 8, 20, 10), BorderThickness = new Thickness(0, 1, 0, 0) };
        foot.SetResourceReference(Border.BorderBrushProperty, "Line");
        footer.Text = sync.Status; footer.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); footer.TextTrimming = TextTrimming.CharacterEllipsis; foot.Child = footer;
        DockPanel.SetDock(foot, Dock.Bottom); root.Children.Add(foot);
        pager.Children.Add(ActionButton(Lang.T("‹ 上一页"), () => TurnPage(-1)));
        pager.Children.Add(pageLabel);
        pager.Children.Add(ActionButton(Lang.T("下一页 ›"), () => TurnPage(1)));
        DockPanel.SetDock(pager, Dock.Bottom); root.Children.Add(pager);
        BuildUpdateBar(); DockPanel.SetDock(updateBar, Dock.Bottom); root.Children.Add(updateBar);
        BuildToast(); DockPanel.SetDock(toast, Dock.Bottom); root.Children.Add(toast);
        scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(13, 0, 9, 0), Content = cards };
        scroller.AllowDrop = true;
        scroller.PreviewDragOver += (_, e) =>
        {
            if (!draggingPaper || DateTime.UtcNow < nextDragScroll) return;
            var point = e.GetPosition(scroller);
            if (point.Y < 32) scroller.ScrollToVerticalOffset(scroller.VerticalOffset - 22);
            else if (point.Y > scroller.ActualHeight - 32) scroller.ScrollToVerticalOffset(scroller.VerticalOffset + 22);
            nextDragScroll = DateTime.UtcNow.AddMilliseconds(80);
        };
        root.Children.Add(scroller);

        tray = new Forms.NotifyIcon { Icon = CreateTrayIcon(), Text = Lang.T("PaperFlow · 双击打开"), Visible = !demonstration };
        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add(Lang.T("显示 PaperFlow"), null, (_, _) => Dispatcher.Invoke(Reveal));
        trayMenu.Items.Add(Lang.T("始终置顶 / 取消置顶"), null, (_, _) => Dispatcher.Invoke(TogglePin));
        trayMenu.Items.Add(Lang.T("放好桌面和开始菜单快捷方式"), null, (_, _) => Dispatcher.Invoke(CreateShortcuts));
        trayMenu.Items.Add(Lang.T("退出"), null, (_, _) => Dispatcher.Invoke(ExitApplication));
        tray.ContextMenuStrip = trayMenu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Reveal);
        Closing += (_, e) => { if (!quitting) { e.Cancel = true; SaveWindow(); Hide(); } };
        Closed += (_, _) => { timer.Stop(); tray.Dispose(); };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { AddPaper(); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { OpenOptions(); e.Handled = true; }
            if (e.Key == Key.F12) { TogglePin(); e.Handled = true; }
        };
        timer.Tick += async (_, _) =>
        {
            var signal = Path.Combine(store.DirectoryPath, "show.signal");
            if (File.Exists(signal)) { try { File.Delete(signal); Reveal(); } catch (IOException) { } }
            if (displayDate != DateTime.Today) { displayDate = DateTime.Today; Render(); }
            if (polling) return;
            polling = true;
            try
            {
                bool changed = await Task.Run(sync.Poll);
                if (changed) { library.Papers = sync.Snapshot().Papers; store.Save(library); Render(); }
                footer.Text = sync.Status;
                footer.ToolTip = sync.Folder == "" ? store.DirectoryPath : Lang.T("资料文件夹：") + sync.Folder + Lang.T("\n跨设备到达时间由你的网盘客户端决定，文件夹更新不等于云端上传已完成。");
            }
            catch (Exception ex) { footer.Text = Lang.T("同步需要留意 · ") + ex.Message; }
            finally { polling = false; }
        };
        if (!demonstration) timer.Start(); ready = true; Render();
        if (demonstration) { footer.Text = Lang.T("演示数据 · 所有论文均为虚构 · ") + Product.Name + " " + Product.Version; footer.ToolTip = null; }
        SessionEndingHook();
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
        using var source = new System.Drawing.Icon(stream, 32, 32); return (System.Drawing.Icon)source.Clone();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    private void SessionEndingHook() => Application.Current.SessionEnding += (_, _) => SaveWindow();
    private void Reveal() { Show(); WindowState = WindowState.Normal; Activate(); HideFromAltTab(); }

    // 任务栏按钮靠 ShowInTaskbar=false 去掉，Alt+Tab 则需要 WS_EX_TOOLWINDOW。
    // 两个都要，缺一个就会在其中一个地方露出来。
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    private const int GwlExStyle = -20, WsExToolWindow = 0x00000080, WsExAppWindow = 0x00040000;
    private void HideFromAltTab()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        SetWindowLong(handle, GwlExStyle, (GetWindowLong(handle, GwlExStyle) | WsExToolWindow) & ~WsExAppWindow);
    }
    private void ExitApplication() { if (!SaveWindow()) return; quitting = true; Close(); }
    internal void CloseDemonstration() { if (!demonstration) throw new InvalidOperationException(Lang.T("仅供演示导出。")); quitting = true; Close(); }
    protected override void OnSourceInitialized(EventArgs e) { base.OnSourceInitialized(e); HideFromAltTab(); }

    // Write a complete candidate snapshot before adopting it, so failed writes do not appear saved.
    private bool Commit(Action<Library> edit, string? notice = null)
    {
        notice ??= Lang.T("已保存");
        try
        {
            var candidate = Storage.CloneLibrary(library); edit(candidate);
            sync.Commit(library, candidate); candidate.Papers = sync.Snapshot().Papers; library = candidate;
            try { store.Save(candidate); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { footer.Text = Lang.T("修改已记录，快照备份待重试 · ") + ex.Message; Render(); return true; }
            footer.Text = store.BackupNotice == null ? Lang.F("{0} · {1:HH:mm} · 本地自动备份", notice, DateTime.Now) : Lang.T("已保存 · ") + store.BackupNotice;
            footer.ToolTip = store.BackupNotice; Render(); return true;
        }
        catch (Exception ex)
        {
            Render(); footer.Text = Lang.T("保存失败 · 本次修改未生效");
            MessageBox.Show(this, Lang.T("无法保存，本次修改没有写入。\n\n") + ex.Message, Lang.T("保存失败"), MessageBoxButton.OK, MessageBoxImage.Error); return false;
        }
    }
    private bool SaveWindow() => Commit(l =>
    {
        if (WindowState == WindowState.Normal) { l.Settings.Width = Width; l.Settings.Height = Height; l.Settings.Left = Left; l.Settings.Top = Top; }
    });
    private void TogglePin() { if (Commit(l => l.Settings.Topmost = !l.Settings.Topmost)) Topmost = library.Settings.Topmost; }
    private void ToggleCompact() => Commit(l => l.Settings.Compact = !l.Settings.Compact);
    private void TurnPage(int delta)
    {
        Commit(l => l.Settings.PageIndex = (l.Settings.PageIndex + delta + ViewRules.PageCount(l.Settings)) % ViewRules.PageCount(l.Settings));
        scroller.ScrollToTop();
    }

    private void AddPaper()
    {
        var dialog = new NewPaperDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var paper = new Paper { Title = dialog.PaperTitle }; paper.Record("创建论文 · 自动生成七阶段");
        if (Commit(l => l.Papers.Insert(0, paper), Lang.T("已新增论文"))) { filter.SelectedIndex = 0; search.Clear(); scroller.ScrollToTop(); }
    }

    private void Render()
    {
        // A nested OLE drag loop still runs the synchronization timer. Defer rebuilding
        // controls until drop/cancel, while continuing to receive and save remote data.
        if (draggingPaper) return;
        Appearance.Apply(library.Settings);
        brandIcon.Source = Appearance.CreateHeaderIcon();
        FontFamily = new FontFamily(Appearance.FamilyFor("body")); FontSize = 13 * Appearance.RoleScale("body") * Appearance.TextScale;
        summary.FontFamily = new FontFamily(Appearance.FamilyFor("caption")); summary.FontSize = 11 * Appearance.RoleScale("caption") * Appearance.TextScale;
        footer.FontFamily = new FontFamily(Appearance.FamilyFor("caption")); footer.FontSize = 10 * Appearance.RoleScale("caption") * Appearance.TextScale;
        pageLabel.FontFamily = new FontFamily(Appearance.FamilyFor("caption")); pageLabel.FontSize = 12 * Appearance.RoleScale("caption") * Appearance.TextScale;
        // 顶部按钮跟着风格走：纸感/极简/标签只用文字，柔光用描边，夜航和经典用浅色块。
        string chromeStyle = Appearance.HeaderStyle switch { "plain" => "QuietButton", "outline" => "OutlineButton", _ => "SoftButton" };
        foreach (var button in quietChrome) button.SetResourceReference(StyleProperty, chromeStyle);
        foreach (var button in quietChrome) button.Foreground = Appearance.HeaderStyle == "plain" ? Brush("#78867F") : Brush("#24352F");
        var picture = Appearance.BackgroundImage();
        if (picture == null)
        {
            scrim.Visibility = Visibility.Collapsed;
            frame.Background = Appearance.Paint(Appearance.Current.Window, Appearance.Opacity * (Appearance.Current.Glass ? .25 : 1));
        }
        else
        {
            // 图片铺满整个小部件，遮罩浓度由用户控制，文字可读性靠它保证。
            frame.Background = new ImageBrush(picture) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center, Opacity = Appearance.Opacity };
            scrim.Visibility = Visibility.Visible;
            scrim.Background = Appearance.Paint(Appearance.Current.Window, Appearance.Scrim * Appearance.Opacity);
        }
        frame.BorderBrush = Appearance.Paint(Appearance.Current.Border, Appearance.Opacity * .75);
        toast.Background = Appearance.Paint(Appearance.Current.Card, Math.Max(0.94, Appearance.Opacity));
        toast.BorderBrush = Appearance.Paint(Appearance.Current.Border, .9);
        toastText.Foreground = Appearance.Paint(Appearance.Current.Ink);
        toastAction.Foreground = Appearance.Paint(Appearance.Current.Accent);
        toastClose.Foreground = Appearance.Paint(Appearance.Current.Muted);
        updateBar.Background = Appearance.Paint(Appearance.Current.Card, Math.Max(0.94, Appearance.Opacity));
        updateBar.BorderBrush = Appearance.Paint(Appearance.Current.Border, .9);
        updateText.Foreground = Appearance.Paint(Appearance.Current.Ink);
        updateAction.Foreground = Appearance.Paint(Appearance.Current.Accent);
        updateClose.Foreground = Appearance.Paint(Appearance.Current.Muted);
        pin.Content = Lang.T(library.Settings.Topmost ? "已置顶" : "置顶"); pin.ToolTip = Lang.T("F12 切换置顶");
        pin.Foreground = library.Settings.Topmost ? Brush("#21846B") : Brush("#78867F");
        compact.Content = Lang.T(library.Settings.Compact ? "展开" : "紧凑");
        var active = library.Papers.Where(p => !p.Archived).ToList();
        summary.Text = Lang.P(active.Count, "{0} 篇论文   ·   {1} 篇推进中   ·   {2} 篇已收录", "{0} paper   ·   {1} in progress   ·   {2} accepted", "{0} papers   ·   {1} in progress   ·   {2} accepted", active.Count, active.Count(p => !p.Stages[6].Done), active.Count(p => p.Stages[6].Done))
            + (filter.SelectedIndex != 0 || search.Text != "" ? Lang.T("   ·   已筛选") : "");
        var candidates = Candidates();
        // 右上角的临时开关：有被隐藏的论文（或正展开着）时才出现，按阶段分组翻页时不需要它。
        int hiddenCount = candidates.Count(p => ViewRules.SelectedStage(p, library.Settings));
        hiddenToggle.Visibility = library.Settings.PageMode != ViewRules.PageModes[2] && library.Settings.HideSelectedStages && (hiddenCount > 0 || library.Settings.ShowHiddenNow) ? Visibility.Visible : Visibility.Collapsed;
        hiddenToggle.Content = Lang.F(library.Settings.ShowHiddenNow ? "收起隐藏 {0} 篇" : "显示隐藏 {0} 篇", hiddenCount);
        hiddenToggle.ToolTip = library.Settings.ShowHiddenNow
            ? Lang.T("把这些按设置隐藏的论文收回去")
            : Lang.T("临时看一眼按当前设置被隐藏的论文，它们会显示成灰底，方便区分");
        AutomationProperties.SetName(hiddenToggle, Lang.T("显示或隐藏按阶段隐藏的论文"));
        var pagePapers = ViewRules.Apply(candidates, library.Settings);
        summary.Text = Lang.P(active.Count, "{0} 篇论文 · 当前显示 {1} 篇", "{0} paper · showing {1}", "{0} papers · showing {1}", active.Count, pagePapers.Count)
            + (library.Settings.PageMode == ViewRules.PageModes[2] ? Lang.T(" · 阶段分组") : library.Settings.HideSelectedStages ? Lang.F(" · 按阶段隐藏 {0} 篇", candidates.Count(p => ViewRules.SelectedStage(p, library.Settings))) : "")
            + (library.Settings.SortMode == ViewRules.SortModes[0] ? "" : Lang.T(" · 排序：") + Lang.Value(library.Settings.SortMode));
        summary.ToolTip = Lang.T("隐藏和翻页仅改变显示，不删除论文。点击论文选项调整。");
        pager.Visibility = ViewRules.PageCount(library.Settings) > 1 ? Visibility.Visible : Visibility.Collapsed;
        pageLabel.Text = $"{ViewRules.PageTitle(library.Settings)} · {library.Settings.PageIndex + 1}/{ViewRules.PageCount(library.Settings)}";
        pageLabel.Foreground = Brush("#24352F");
        pageLabel.ToolTip = library.Settings.PageMode == ViewRules.PageModes[2] ? Lang.T("所选阶段：") + string.Join(Lang.ListSeparator, library.Settings.HiddenStages.Select(Lang.Stage)) : null;
        cards.Children.Clear();
        foreach (var p in pagePapers) cards.Children.Add(BuildCard(p));
        if (cards.Children.Count == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(20, 45, 20, 40) };
            var title = Text(Lang.T(library.Papers.Count == 0 ? "从第一篇论文开始" : "这里暂时没有论文"), 22, "#24352F", -1, "title"); title.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(title);
            var hint = Text(Lang.T(library.Papers.Count == 0 ? "点击右上角 ＋，输入论文标题。\n七个阶段和进度条会自动准备好。" : "论文可能在其他页，或被当前筛选隐藏。\n点击论文选项调整显示范围。"), 13, "#78867F", -1, "caption");
            hint.TextAlignment = TextAlignment.Center; hint.Margin = new Thickness(0, 15, 0, 20); empty.Children.Add(hint);
            if (library.Papers.Count == 0) { var demo = ActionButton(Lang.T("查看三篇示例"), AddExamples); demo.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(demo); }
            else { var adjust = ActionButton(Lang.T("调整论文选项"), OpenOptions); adjust.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(adjust); }
            cards.Children.Add(empty);
        }
        Dispatcher.BeginInvoke(new Action(() => layoutChrome?.Invoke()), DispatcherPriority.Loaded);
    }

    // The settings sliders show how many whole cards fit in the live viewport.
    internal int EstimateVisiblePapers()
    {
        var borders = cards.Children.OfType<Border>().Where(b => b.Tag is string).ToList();
        if (borders.Count == 0) return 0;
        double card = borders.Average(b => b.ActualHeight + b.Margin.Top + b.Margin.Bottom);
        return ViewRules.EstimateVisiblePapers(scroller.ActualHeight, card, borders.Count);
    }

    // The settings dialog previews appearance live. Growing the widget with the text keeps
    // the visible paper count from collapsing, but never past half of the work area.
    private void PreviewAppearance(Preferences preview)
    {
        double before = Appearance.TextScale;
        Appearance.Apply(preview); library.Settings = preview;
        if (preview.AutoGrowWindow && before > 0.1)
        {
            double ratio = Appearance.TextScale / before;
            if (Math.Abs(ratio - 1) > 0.01)
            {
                var area = SystemParameters.WorkArea;
                Width = Math.Clamp(Width * ratio, MinWidth, Math.Max(MinWidth, area.Width / 2));
                Height = Math.Clamp(Height * ratio, MinHeight, Math.Max(MinHeight, area.Height / 2));
                Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
                Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
            }
        }
        Render();
    }

    // 底部提示条：勾选阶段后论文被隐藏或被挪到别的页时交代一句，八秒自己走。
    private void BuildToast()
    {
        toast.Visibility = Visibility.Collapsed;
        toast.CornerRadius = new CornerRadius(10); toast.BorderThickness = new Thickness(1);
        toast.Margin = new Thickness(15, 0, 15, 9); toast.Padding = new Thickness(14, 9, 10, 9);
        toast.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = 0.16, Color = Colors.Black };
        toastText.TextWrapping = TextWrapping.Wrap; toastText.VerticalAlignment = VerticalAlignment.Center;
        toastAction.Background = Brushes.Transparent; toastAction.Padding = new Thickness(8, 2, 8, 2); toastAction.Visibility = Visibility.Collapsed;
        toastAction.Click += (_, _) => { var action = toastActionHandler; HideToast(); action?.Invoke(); };
        toastClose.Content = "×"; toastClose.Background = Brushes.Transparent; toastClose.Padding = new Thickness(8, 2, 8, 2);
        toastClose.ToolTip = Lang.T("收起这条提示"); toastClose.Click += (_, _) => HideToast();
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.Children.Add(toastText);
        Grid.SetColumn(toastAction, 1); body.Children.Add(toastAction);
        Grid.SetColumn(toastClose, 2); body.Children.Add(toastClose);
        toast.Child = body;
        toastTimer.Tick += (_, _) => HideToast();
    }
    private void ShowNotice(string text, string actionLabel = "", Action? action = null)
    {
        if (!library.Settings.ShowNotices) return;
        toastText.Text = text;
        toastText.FontFamily = new FontFamily(Appearance.FamilyFor("body"));
        toastText.FontSize = 12.5 * Appearance.RoleScale("body") * Appearance.TextScale;
        toastAction.Content = actionLabel;
        toastAction.Visibility = actionLabel == "" ? Visibility.Collapsed : Visibility.Visible;
        toastAction.FontFamily = new FontFamily(Appearance.FamilyFor("body"));
        toastAction.FontSize = 12 * Appearance.RoleScale("body") * Appearance.TextScale;
        toastClose.FontSize = 13 * Appearance.RoleScale("body") * Appearance.TextScale;
        toastActionHandler = action;
        toast.Visibility = Visibility.Visible;
        toastTimer.Stop(); toastTimer.Start();
    }
    private void HideToast() { toastTimer.Stop(); toast.Visibility = Visibility.Collapsed; toastActionHandler = null; }

    // ---------- 更新：只下载、不上传，且只有用户点了按钮才会下载 ----------
    // 一条不自动消失的提示条：发现新版 → 下载并安装 → 重启并更新。
    private void BuildUpdateBar()
    {
        updateBar.Visibility = Visibility.Collapsed;
        updateBar.CornerRadius = new CornerRadius(10); updateBar.BorderThickness = new Thickness(1);
        updateBar.Margin = new Thickness(15, 0, 15, 9); updateBar.Padding = new Thickness(14, 9, 10, 9);
        updateText.TextWrapping = TextWrapping.Wrap; updateText.VerticalAlignment = VerticalAlignment.Center;
        updateAction.Background = Brushes.Transparent; updateAction.Padding = new Thickness(8, 2, 8, 2);
        updateAction.Click += (_, _) => { var action = updateActionHandler; action?.Invoke(); };
        updateClose.Content = "×"; updateClose.Background = Brushes.Transparent; updateClose.Padding = new Thickness(8, 2, 8, 2);
        updateClose.ToolTip = Lang.T("先不更新，下次启动再说"); updateClose.Click += (_, _) => { updateBar.Visibility = Visibility.Collapsed; };
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.Children.Add(updateText);
        Grid.SetColumn(updateAction, 1); body.Children.Add(updateAction);
        Grid.SetColumn(updateClose, 2); body.Children.Add(updateClose);
        updateBar.Child = body;
    }
    private void OfferUpdate(UpdateManifest manifest)
    {
        if (demonstration || manifest == null) return;
        updateOffered = manifest;
        UpdateBarText(Lang.F("有新版本 {0}", manifest.Version), Lang.T("下载并安装"), StartUpdate);
    }
    private void UpdateBarText(string text, string actionLabel, Action action)
    {
        updateText.Text = text;
        updateText.FontFamily = new FontFamily(Appearance.FamilyFor("body"));
        updateText.FontSize = 12.5 * Appearance.RoleScale("body") * Appearance.TextScale;
        updateAction.Content = actionLabel;
        updateAction.FontFamily = new FontFamily(Appearance.FamilyFor("body"));
        updateAction.FontSize = 12 * Appearance.RoleScale("body") * Appearance.TextScale;
        updateAction.IsEnabled = true;
        updateActionHandler = action;
        updateBar.Visibility = Visibility.Visible;
    }
    private async void StartUpdate()
    {
        var manifest = updateOffered;
        if (manifest == null || updating) return;
        updating = true;
        UpdateBarText(Lang.T("更新在后台下载，不影响你继续用。"), Lang.T("正在下载…"), () => { });
        updateAction.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(value => updateText.Text = Lang.F("正在下载更新 {0}%", Math.Round(value)));
            using var client = Updates.Client();
            var file = await Updates.DownloadAsync(client, manifest, Updates.CacheFolder(), progress, System.Threading.CancellationToken.None);
            var installed = Updates.Install(manifest, file, Shortcuts.ProgramFolder(library.Settings.LauncherPath));
            UpdateBarText(Lang.F("新版本已就绪 {0}，重启后生效。", manifest.Version), Lang.T("重启并更新"), () =>
            {
                if (!SaveWindow()) return;
                quitting = true; Restart(installed);
            });
        }
        catch (Exception ex)
        {
            UpdateBarText(Lang.T("下载失败") + " · " + ex.Message, Lang.T("重试"), StartUpdate);
        }
        finally { updating = false; }
    }
    // 自动检查失败要安静（离线是常态），手动检查才说出来。
    internal async Task CheckForUpdatesAsync(bool manual)
    {
        if (demonstration) return;
        if (!manual && !Updates.ShouldCheck(library.Settings.UpdateMode, library.Settings.LastUpdateCheckUtc, DateTime.UtcNow)) return;
        try
        {
            using var client = Updates.Client();
            var manifest = await Updates.FetchAsync(client, System.Threading.CancellationToken.None);
            Commit(l => l.Settings.LastUpdateCheckUtc = DateTime.UtcNow);
            if (manifest != null) OfferUpdate(manifest);
        }
        catch (Exception ex)
        {
            // 失败也记下时间，免得断网时每次启动都去试一遍。
            try { Commit(l => l.Settings.LastUpdateCheckUtc = DateTime.UtcNow); } catch (Exception) { }
            if (manual) ShowNotice(Lang.F("检查更新失败：{0}", ex.Message));
        }
    }
    // 关掉自己再开一个：新进程先等旧进程退出，避免抢单实例锁把挂件弄丢。
    // executable 为空就重开当前这个程序文件；装完更新时传新版本自己的 exe。
    private void Restart(string? executable = null)
    {
        try
        {
            var args = new List<string>();
            var original = Environment.GetCommandLineArgs().Skip(1).ToList();
            for (int i = 0; i < original.Count; i++) { if (original[i] == "--wait-for") { i++; continue; } args.Add(original[i]); }
            args.Add("--wait-for"); args.Add(Environment.ProcessId.ToString());
            string target = executable ?? Environment.ProcessPath ?? "";
            if (target == "") throw new InvalidOperationException(Lang.T("找不到要重新打开的程序文件。"));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(target)!,
                Arguments = string.Join(" ", args.Select(a => a.Contains(' ', StringComparison.Ordinal) ? "\"" + a + "\"" : a))
            });
            quitting = true; Close();
        }
        catch (Exception ex) { ShowNotice(Lang.F("没能自动重启，请自己从托盘退出再打开：{0}", ex.Message)); }
    }

    // 当前筛选、搜索、排序之后还剩哪些论文；提示条判断"这篇还在不在眼前"要用同一份名单。
    private List<Paper> Candidates()
    {
        IEnumerable<Paper> visible = library.Papers.Where(p => filter.SelectedIndex == 3 ? p.Archived : !p.Archived);
        if (filter.SelectedIndex == 1) visible = visible.Where(p => !p.Stages[6].Done);
        if (filter.SelectedIndex == 2) visible = visible.Where(p => p.Stages[6].Done);
        var term = search.Text.Trim();
        if (term != "") visible = visible.Where(p => string.Join(" ", p.Title, p.Subject, p.Language, p.Collaborators, p.Journal, p.Notes, p.Status, p.NextAction).Contains(term, StringComparison.OrdinalIgnoreCase));
        return (sort.SelectedIndex switch { 1 => visible.OrderByDescending(p => p.UpdatedAt), 2 => visible.OrderBy(p => p.DueDate ?? DateTime.MaxValue), 3 => visible.OrderByDescending(p => p.Progress), _ => visible }).ToList();
    }

    private Border BuildCard(Paper p)
    {
        bool list = Appearance.Layout == Themes.ListLayout;
        bool small = library.Settings.Compact || list;
        // 临时展开出来的“按设置隐藏”的论文：整体压暗、底色换成主题的柔和色，并挂一个标记。
        bool dimmed = library.Settings.ShowHiddenNow && ViewRules.SelectedStage(p, library.Settings);
        var card = new Border { Tag = p.Id };
        if (list)
        {
            // 列表布局：没有卡片，只有一条分隔线，一屏能看更多篇。
            card.Background = dimmed ? Appearance.Paint(Appearance.Current.Soft, Appearance.Opacity * .45) : Brushes.Transparent;
            card.CornerRadius = new CornerRadius(0);
            card.BorderThickness = new Thickness(0, 0, 0, 1);
            card.BorderBrush = Appearance.Paint(Appearance.Current.Border, Appearance.Opacity * .7);
            card.Padding = new Thickness(3 * Appearance.Scale, 6 * Appearance.Scale, 3 * Appearance.Scale, 8 * Appearance.Scale);
            card.Margin = new Thickness(6, 0, 6, 0);
        }
        else
        {
            card.Background = dimmed ? Appearance.Paint(Appearance.Current.Soft, Appearance.Opacity * .95) : Appearance.Paint(Appearance.Current.Card, Appearance.Opacity);
            card.CornerRadius = new CornerRadius(Appearance.CardRadius);
            card.BorderBrush = Appearance.Paint(Appearance.Current.Border, Appearance.Opacity * .8);
            card.BorderThickness = new Thickness(Appearance.Shadow ? 0 : 1);
            card.Padding = new Thickness(13 * Appearance.Scale, small ? 7 * Appearance.Scale : 10 * Appearance.Scale, 13 * Appearance.Scale, small ? 5 * Appearance.Scale : 8 * Appearance.Scale);
            card.Margin = new Thickness(5, 0, 5, 7);
            if (Appearance.Shadow)
            {
                var shadow = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12 * Appearance.Scale, ShadowDepth = 2 * Appearance.Scale, Direction = 270, Opacity = 0.10, Color = Colors.Black };
                card.Effect = shadow;
            }
        }
        if (dimmed) card.Opacity = 0.8;
        var stack = new StackPanel(); card.Child = stack;
        var titleArea = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new Thickness(0, 0, 0, 1) };
        var dots = PriorityDots(p);
        titleArea.Children.Add(dots);
        var name = Text(p.Title, small ? 14 : 15, "#24352F", -1, "title"); name.FontWeight = library.Settings.TitleBold ? FontWeights.SemiBold : FontWeights.Normal; name.TextTrimming = TextTrimming.CharacterEllipsis; name.VerticalAlignment = VerticalAlignment.Center; name.ToolTip = p.Title + Lang.T("\n拖动调整优先顺序");
        titleArea.Children.Add(name);
        // 百分比放在标题行右侧，不跟进度条挤在一起，也不再抢戏。
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition()); titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.Children.Add(titleArea);
        if (dimmed)
        {
            var tag = Text(Lang.T("已隐藏 · ") + Lang.Stage(p.CurrentStageIndex), 10.5, "#78867F", -1, "caption");
            var chip = new Border { Child = tag, Background = Appearance.Paint(Appearance.Current.Card, .85), CornerRadius = new CornerRadius(Math.Min(Appearance.ChipRadius, 8 * Appearance.Scale)), Padding = new Thickness(7 * Appearance.Scale, 2 * Appearance.Scale, 7 * Appearance.Scale, 2 * Appearance.Scale), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), ToolTip = Lang.T("按其当前阶段，这类论文在你的设置里是隐藏的；点右上角可以收回去") };
            Grid.SetColumn(chip, 1); titleRow.Children.Add(chip);
        }
        var pct = Text($"{p.Progress}%", Appearance.PercentSize); pct.FontWeight = FontWeights.SemiBold; pct.VerticalAlignment = VerticalAlignment.Center; pct.Margin = new Thickness(10, 0, 0, 0);
        if (Appearance.RoleColor("body") == "") pct.Foreground = Appearance.PercentAccent ? Appearance.Paint(Appearance.Current.Accent) : Brush("#78867F");
        AutomationProperties.SetName(pct, Lang.F("{0} 进度 {1}%", p.Title, p.Progress));
        Grid.SetColumn(pct, 2); titleRow.Children.Add(pct); stack.Children.Add(titleRow);
        var metadata = string.Join(" / ", new[] { p.Subject, p.Language, p.Journal }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var progressRow = new Grid { Margin = new Thickness(0, 4 * Appearance.Scale, 0, 5 * Appearance.Scale), ToolTip = Lang.T("下一步：") + (p.NextAction != "" ? p.NextAction : p.NextStage) };
        var track = new Grid { Height = Appearance.BarHeight * Appearance.Scale, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border { Background = Brush("#EBEFE9"), CornerRadius = new CornerRadius(5) });
        var inner = new Grid(); inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, p.Progress), GridUnitType.Star) }); inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 100 - p.Progress), GridUnitType.Star) });
        var fill = new Border { Background = Brush(p.Stages[6].Done ? "#2F8B6D" : p.Status == "待返修" ? "#C69544" : "#4A9E83"), CornerRadius = new CornerRadius(5) }; inner.Children.Add(fill); track.Children.Add(inner);
        AutomationProperties.SetName(track, Lang.F("{0} 进度 {1}%", p.Title, p.Progress)); progressRow.Children.Add(track);
        stack.Children.Add(progressRow);
        var checks = new StageFlowPanel();
        for (int i = 0; i < p.Stages.Count; i++)
        {
            int index = i; var stage = p.Stages[i];
            var cb = new CheckBox { Content = Lang.Stage(i) + (stage.Skipped ? Lang.T("（免）") : ""), IsChecked = stage.Done, IsEnabled = !stage.Skipped, FontFamily = new FontFamily(Appearance.FamilyFor("body")), FontSize = (small ? 11 : 12) * Appearance.RoleScale("body") * Appearance.TextScale };
            cb.SetResourceReference(StyleProperty, Appearance.ChipStyle switch { "pill" => "StagePill", "tag" => "StageTag", "chip" => "StageCheck", _ => "StageText" });
            AutomationProperties.SetName(cb, p.Title + " · " + Lang.Stage(i));
            if (Appearance.ChipStyle == "tag") cb.Background = Appearance.Paint(Appearance.Tags.Length == 0 ? Appearance.Current.Accent : Appearance.Tags[i % Appearance.Tags.Length]);
            cb.Padding = new Thickness(small ? 4 : 5, 3, small ? 4 : 5, 3); cb.Margin = new Thickness(0, 0, Appearance.ChipStyle == "text" ? 12 * Appearance.Scale : 4, 3);
            cb.ToolTip = Lang.T(stage.Skipped ? "返修已设为不适用，可在论文资料中恢复" : i == 4 ? "勾选表示进入在审；继续勾选返修或收录后，移出在审分组。" : "点击切换；进度按适用阶段等权计算");
            // 监听状态变化而不是 Click：键盘、鼠标和自动化切换都走同一条路径。
            void Toggle(bool done)
            {
                if (!Commit(l => l.Papers.Single(x => x.Id == p.Id).ToggleStage(index, done))) return;
                var updated = library.Papers.FirstOrDefault(x => x.Id == p.Id);
                if (updated == null) return;
                Chime.PlayForToggle(library.Settings, done, updated.Stages[6].Done);
                var notice = ViewRules.AfterStageToggle(updated, library.Settings, Candidates());
                if (notice == null) return;
                if (notice.Kind == "paged") ShowNotice(notice.Text, notice.Action, () => { Commit(l => l.Settings.PageIndex = notice.Page); scroller.ScrollToTop(); });
                else ShowNotice(notice.Text, notice.Action, () => Commit(l => l.Settings.HiddenStages.Remove(updated.CurrentStageIndex)));
            }
            cb.Checked += (_, _) => Toggle(true);
            cb.Unchecked += (_, _) => Toggle(false);
            checks.Children.Add(cb);
        }
        var due = Text(p.DueDate != null && !p.Stages[6].Done ? p.DeadlineText : Lang.P(p.ElapsedDays, "已开始 {0} 天", "Started {0} day ago", "Started {0} days ago", p.ElapsedDays), 11, p.DueDate?.Date < DateTime.Today && !p.Stages[6].Done ? "#BE624C" : "#8A948C");
        due.Margin = new Thickness(4, 0, 5, 3); due.ToolTip = Lang.P(p.ElapsedDays, "开始日期：{0:yyyy-MM-dd}\n已开始 {1} 天", "Started {0:yyyy-MM-dd}\n{1} day in", "Started {0:yyyy-MM-dd}\n{1} days in", p.StartDate, p.ElapsedDays); checks.Children.Add(due);
        if (!string.IsNullOrWhiteSpace(p.NextAction))
        {
            var next = Text(Lang.T("下一步 ") + p.NextAction, 11, "#6C7C70"); next.MaxWidth = 150 * Appearance.TextScale; next.TextTrimming = TextTrimming.CharacterEllipsis; next.ToolTip = p.NextAction; next.Margin = new Thickness(4, 0, 0, 3); checks.Children.Add(next); checks.OptionalTail = next;
        }
        // One settings entry per paper, anchored at the bottom right corner of the card.
        var bottom = new Grid();
        bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.Children.Add(checks);
        var settings = new Button { Content = "⚙", FontFamily = new FontFamily(Appearance.FamilyFor("body")), FontSize = 13 * Appearance.RoleScale("body") * Appearance.TextScale, Padding = new Thickness(7 * Appearance.Scale, 2 * Appearance.Scale, 7 * Appearance.Scale, 2 * Appearance.Scale), Margin = new Thickness(7, 0, 0, 3), VerticalAlignment = VerticalAlignment.Bottom, Foreground = Brush("#62766A") };
        settings.ToolTip = (metadata == "" ? Lang.T("尚未填写学科、期刊等资料") : metadata + " · " + Lang.Value(p.EffectiveStatus)) + Lang.T("\n编辑资料、修改记录、排序、复制、归档");
        settings.Click += (_, _) => PaperMenu(p, settings);
        AutomationProperties.SetName(settings, Lang.F("{0} 的论文设置", p.Title));
        Grid.SetColumn(settings, 1); bottom.Children.Add(settings); stack.Children.Add(bottom);
        AttachPaperDrag(card, p.Id, dots, () => PriorityMenu(p, dots));
        return card;
    }

    // Priority is shown as three dots at the left edge of the title row; the filled
    // count and the colour both carry the level. The dots stay a drag handle, so a
    // press that never moves opens the priority menu instead of reordering.
    private static FrameworkElement PriorityDots(Paper p)
    {
        int level = ViewRules.PriorityLevel(p.Priority);
        var color = Appearance.Paint(p.Priority switch { "高" => "#C2543F", "中" => "#BE8C31", _ => "#3E8A6C" });
        double size = 6.5 * Appearance.Scale;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 3; i++) row.Children.Add(new System.Windows.Shapes.Ellipse { Width = size, Height = size, Margin = new Thickness(i == 0 ? 0 : 3.4 * Appearance.Scale, 0, 0, 0), Fill = i < level ? color : Brush("#EBEFE9") });
        var dots = new Border { Child = row, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Padding = new Thickness(0, 3, 8 * Appearance.Scale, 3), VerticalAlignment = VerticalAlignment.Center, ToolTip = Lang.F("{0}优先级 · 三个点分别代表高、中、低\n点击修改，按住拖动调整顺序", Lang.Value(p.Priority)) };
        AutomationProperties.SetName(dots, p.Title + Lang.T(" 的优先级：") + Lang.Value(p.Priority));
        return dots;
    }

    private static DependencyObject? ParentOf(DependencyObject node) => node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        for (var current = node; current != null; current = ParentOf(current)) if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }
    private static bool InteractiveSource(DependencyObject? source)
    {
        for (var node = source; node != null; node = ParentOf(node))
            if (node is ButtonBase or TextBoxBase or ComboBox or Thumb or ScrollBar) return true;
        return false;
    }
    private void DragWidget(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 1 || InteractiveSource(e.OriginalSource as DependencyObject)) return;
        for (var node = e.OriginalSource as DependencyObject; node != null; node = ParentOf(node))
            if (node is Border { Tag: string }) return; // Card gestures belong to paper ordering.
        if (Mouse.LeftButton != MouseButtonState.Pressed) return;
        e.Handled = true; DragMove(); SaveWindow();
    }
    private void AttachPaperDrag(Border card, string id, FrameworkElement? leftmost = null, Action? leftmostClick = null)
    {
        Point? start = null;
        bool leftmostPressed = false;
        card.PreviewMouseLeftButtonDown += (_, e) =>
        {
            leftmostPressed = leftmost != null && e.OriginalSource is DependencyObject source && IsWithin(source, leftmost);
            start = InteractiveSource(e.OriginalSource as DependencyObject) ? null : e.GetPosition(card);
            if (start != null) { card.CaptureMouse(); e.Handled = true; }
        };
        card.PreviewMouseLeftButtonUp += (_, e) =>
        {
            // The releasing element is the capture target, so the press location is remembered above.
            bool pending = start != null, clickedLeftmost = leftmostPressed;
            start = null; leftmostPressed = false;
            if (card.IsMouseCaptured) card.ReleaseMouseCapture();
            if (pending && clickedLeftmost && leftmostClick != null) { leftmostClick(); e.Handled = true; }
        };
        card.LostMouseCapture += (_, _) => { if (!draggingPaper) { start = null; leftmostPressed = false; } };
        card.PreviewMouseMove += (_, e) =>
        {
            if (start == null || e.LeftButton != MouseButtonState.Pressed || draggingPaper) return;
            var point = e.GetPosition(card);
            if (Math.Abs(point.X - start.Value.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - start.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null; leftmostPressed = false; draggingPaper = true; draggedPaperId = id;
            card.ReleaseMouseCapture();
            try { DragDrop.DoDragDrop(card, new DataObject(PaperDragFormat, id), DragDropEffects.Move); }
            finally { draggingPaper = false; draggedPaperId = null; Render(); }
            e.Handled = true;
        };
        void ClearMarker() { card.BorderBrush = Appearance.Paint(Appearance.Current.Border, Appearance.Opacity * .8); card.BorderThickness = new Thickness(1); }
        card.AllowDrop = true;
        card.DragOver += (_, e) =>
        {
            bool valid = draggingPaper && draggedPaperId != id && e.Data.GetData(PaperDragFormat) is string value && value == draggedPaperId;
            e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true;
            if (!valid) { ClearMarker(); return; }
            card.BorderBrush = Appearance.Paint(Appearance.Current.Accent);
            card.BorderThickness = e.GetPosition(card).Y < card.ActualHeight / 2 ? new Thickness(1, 3, 1, 1) : new Thickness(1, 1, 1, 3);
        };
        card.DragLeave += (_, _) => ClearMarker();
        card.Drop += (_, e) =>
        {
            ClearMarker(); e.Handled = true;
            if (!draggingPaper || e.Data.GetData(PaperDragFormat) is not string source || source != draggedPaperId || source == id) return;
            var visible = cards.Children.OfType<Border>().Select(c => c.Tag as string).Where(x => x != null).Cast<string>().ToList();
            bool after = e.GetPosition(card).Y >= card.ActualHeight / 2;
            if (Commit(l => { if (PaperOrder.MoveVisible(l.Papers, visible, source, id, after)) l.Papers.Single(p => p.Id == source).Record("调整论文优先顺序"); }, Lang.T("已保存优先顺序"))) sort.SelectedIndex = 0;
        };
    }

    private void PaperMenu(Paper p, FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor };
        void Item(string title, Action action) { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item(Lang.T("编辑论文资料"), () => EditPaper(p));
        Item(Lang.T("查看修改记录"), () => ShowHistory(p));
        menu.Items.Add(new Separator());
        Item(Lang.T("上移一位"), () => MovePaper(p.Id, -1)); Item(Lang.T("下移一位"), () => MovePaper(p.Id, 1));
        Item(Lang.T("复制为新论文（阶段清零）"), () =>
        {
            if (Commit(l => { var copy = Storage.Clone(p); copy.Id = Guid.NewGuid().ToString("N"); var suffix = Lang.T("（副本）"); copy.Title = p.Title.Length > 490 - suffix.Length ? p.Title[..(490 - suffix.Length)] + suffix : p.Title + suffix; copy.Stages = Paper.StageNames.Select(n => new Stage { Name = n }).ToList(); copy.History.Clear(); copy.Archived = false; copy.StartDate = DateTime.Today; copy.DueDate = null; copy.Status = "准备中"; copy.NextAction = ""; copy.Outcome = ""; copy.Notes = ""; copy.Record("复制论文资料 · 七阶段清零"); l.Papers.Insert(0, copy); }))
                ShowNotice(Lang.T("已复制为新论文 · 它已经放在列表最上面"));
        });
        Item(Lang.T(p.Archived ? "恢复到论文列表" : "归档（保留资料）"), () =>
        {
            if (!Commit(l => { var paper = l.Papers.Single(x => x.Id == p.Id); paper.Archived = !paper.Archived; paper.Record(paper.Archived ? "归档论文" : "恢复论文"); })) return;
            ShowNotice(Lang.T(p.Archived ? "已恢复到论文列表" : "已归档 · 在论文选项的显示范围里选“已归档”可以再找到它"));
        });
        menu.IsOpen = true;
    }
    private void PriorityMenu(Paper p, FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor };
        foreach (var value in Paper.Priorities)
        {
            var item = new MenuItem { Header = Lang.F("{0}优先级", Lang.Value(value)), IsCheckable = true, IsChecked = p.Priority == value };
            item.Click += (_, _) => Commit(l => { var paper = l.Papers.Single(x => x.Id == p.Id); if (paper.Priority != value) { paper.Priority = value; paper.Record("优先级设为" + value); } });
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    private void MovePaper(string id, int delta)
    {
        sort.SelectedIndex = 0;
        Commit(l => { int index = l.Papers.FindIndex(p => p.Id == id); int next = index + delta; if (next >= 0 && next < l.Papers.Count) (l.Papers[index], l.Papers[next]) = (l.Papers[next], l.Papers[index]); });
    }
    private void EditPaper(Paper original)
    {
        var dialog = new PaperEditor(Storage.Clone(original)) { Owner = this };
        if (dialog.ShowDialog() == true) Commit(l => { dialog.Result.Record("更新论文资料"); var before = new Library { Papers = new() { original } }; var after = new Library { Papers = new() { dialog.Result } }; SyncProtocol.ApplyEdits(l, SyncProtocol.Diff(before, after)); });
    }
    private void ShowHistory(Paper p)
    {
        var box = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18), Text = string.Join("\n\n", p.History.Select(h => $"{h.At:yyyy-MM-dd HH:mm:ss}   {Lang.History(h.Description)}")) };
        new Window { Title = Lang.T("修改记录 · ") + p.Title, Owner = this, Width = 560 * Appearance.DialogScale, Height = 500 * Appearance.DialogScale, ShowInTaskbar = true, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = box }.ShowDialog();
    }
    private void OpenSettings()
    {
        var original = Storage.CloneLibrary(library).Settings;
        var dialog = new SettingsWindow(library.Settings, store.DirectoryPath, Export, Import, PreviewAppearance, EstimateVisiblePapers) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            bool languageChanged = !string.Equals(Lang.Effective(original.Language), Lang.Effective(dialog.Result.Language), StringComparison.Ordinal);
            if (!Commit(l => l.Settings = dialog.Result)) return;
            // 语言换了就重启一次：挂件上的按钮、托盘菜单是开窗口时建好的，重启最干净。
            if (languageChanged) { Restart(); return; }
            if (Updates.Offered is UpdateManifest found) OfferUpdate(found);
        }
        else Commit(l => l.Settings = original);
    }
    // 挂件自己不占任务栏，最容易的“弄丢”方式就是找不到入口。托盘菜单里一键把两个入口放好。
    private void CreateShortcuts()
    {
        try
        {
            Shortcuts.Apply(true, true, library.Settings.LauncherPath);
            tray.ShowBalloonTip(6000, Product.Name, Lang.T("桌面和开始菜单各放好一个入口。要固定在任务栏，右键那个快捷方式选“固定到任务栏”。"), Forms.ToolTipIcon.Info);
        }
        catch (Exception ex) { MessageBox.Show(this, Lang.T("快捷方式未能创建。\n") + ex.Message, Product.Name, MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void OpenOptions()
    {
        var window = new Window { Title = Lang.T("论文选项"), Width = Math.Min(490 * Appearance.DialogScale, SystemParameters.WorkArea.Width - 40), Height = Math.Min(760 * Appearance.DialogScale, SystemParameters.WorkArea.Height - 30), Owner = this, ResizeMode = ResizeMode.CanResize, ShowInTaskbar = true, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(22) }; window.Content = root;
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(controls, Dock.Bottom); root.Children.Add(controls);
        var body = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(Text(Lang.T("搜索与视图"), 20));
        body.Children.Add(Text(Lang.T("搜索论文、期刊、学科或合作者"), 11, "#78867F"));
        search.Margin = new Thickness(0, 9, 0, 12); body.Children.Add(search);
        body.Children.Add(Text(Lang.T("显示范围"), 12)); filter.Margin = new Thickness(0, 5, 0, 12); body.Children.Add(filter);
        body.Children.Add(Text(Lang.T("排序方式"), 12)); sort.Margin = new Thickness(0, 5, 0, 12); body.Children.Add(sort);
        var small = new CheckBox { Content = Lang.T("紧凑视图（保留七阶段）"), IsChecked = library.Settings.Compact }; small.Click += (_, _) => Commit(l => l.Settings.Compact = small.IsChecked == true); body.Children.Add(small);
        void Label(string text) { var label = Text(text, 13); label.Margin = new Thickness(0, 16, 0, 7); body.Children.Add(label); }
        void ChangeView(Action<Preferences> change) => Commit(l => { change(l.Settings); l.Settings.PageIndex = 0; });
        Label(Lang.T("只显示这些优先级"));
        var priorities = new WrapPanel(); body.Children.Add(priorities);
        foreach (var value in Paper.Priorities)
        {
            var check = new CheckBox { Content = Lang.F("{0}优先级", Lang.Value(value)), IsChecked = library.Settings.VisiblePriorities.Contains(value), Margin = new Thickness(0, 0, 18, 5) };
            check.Click += (_, _) => ChangeView(p => { p.VisiblePriorities.Remove(value); if (check.IsChecked == true) p.VisiblePriorities.Add(value); }); priorities.Children.Add(check);
        }
        Label(Lang.T("暂时隐藏的阶段"));
        var hide = new CheckBox { Content = Lang.T("隐藏所选阶段的论文"), IsChecked = library.Settings.HideSelectedStages, Margin = new Thickness(0, 0, 0, 8) };
        hide.Click += (_, _) => ChangeView(p => p.HideSelectedStages = hide.IsChecked == true); body.Children.Add(hide);
        var stages = new WrapPanel(); body.Children.Add(stages);
        for (int i = 0; i < Paper.StageLabels.Length; i++)
        {
            int index = i;
            var check = new CheckBox { Content = Lang.Stage(i), IsChecked = library.Settings.HiddenStages.Contains(i), Margin = new Thickness(0, 0, 14, 7) };
            check.Click += (_, _) => ChangeView(p => { p.HiddenStages.Remove(index); if (check.IsChecked == true) p.HiddenStages.Add(index); }); stages.Children.Add(check);
        }
        var explanation = Text(Lang.T("按最后一个已勾选阶段归类；未勾选时归入开题。\n例如：在审后进入返修，会重新显示。"), 11, "#78867F"); explanation.TextWrapping = TextWrapping.Wrap; body.Children.Add(explanation);
        var peek = Text(Lang.T("挂件右上角有个“显示隐藏 N 篇”的临时开关：点一下就能看一眼这些论文，展开时它们显示成灰底并带“已隐藏”标记，这里的设置不受影响。"), 11, "#78867F");
        peek.TextWrapping = TextWrapping.Wrap; peek.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(peek);
        Label(Lang.T("翻页方式"));
        var paging = new ComboBox { ItemsSource = Lang.Choices(ViewRules.PageModes), DisplayMemberPath = "Label", SelectedIndex = Math.Max(0, Array.IndexOf(ViewRules.PageModes, library.Settings.PageMode)) }; body.Children.Add(paging);
        paging.SelectionChanged += (_, _) => ChangeView(p => p.PageMode = ViewRules.PageModes[Math.Max(0, paging.SelectedIndex)]);
        var pageHint = Text(Lang.T("优先级：高 → 中 → 低。\n阶段分组：第一页排除所选阶段，第二页只看所选阶段。\n阶段分页会将隐藏项放到第二页；优先级筛选仍生效。"), 11, "#78867F"); pageHint.TextWrapping = TextWrapping.Wrap; pageHint.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(pageHint);
        controls.Children.Add(ActionButton(Lang.T("显示全部"), () => { search.Clear(); filter.SelectedIndex = 0; sort.SelectedIndex = 0; ChangeView(p => { p.HideSelectedStages = false; p.PageMode = ViewRules.PageModes[0]; p.VisiblePriorities = Paper.Priorities.ToList(); }); window.Close(); }));
        controls.Children.Add(ActionButton(Lang.T("完成"), window.Close, true));
        window.Closed += (_, _) => { body.Children.Remove(search); body.Children.Remove(filter); body.Children.Remove(sort); };
        window.Loaded += (_, _) => search.Focus(); window.ShowDialog();
    }
    private void AddResizeHandles(Grid surface)
    {
        foreach (var pair in new[] { (HorizontalAlignment.Left, VerticalAlignment.Stretch), (HorizontalAlignment.Right, VerticalAlignment.Stretch), (HorizontalAlignment.Stretch, VerticalAlignment.Top), (HorizontalAlignment.Stretch, VerticalAlignment.Bottom), (HorizontalAlignment.Right, VerticalAlignment.Bottom) })
        {
            bool horizontal = pair.Item1 != HorizontalAlignment.Stretch, vertical = pair.Item2 != VerticalAlignment.Stretch;
            var thumb = new Thumb { HorizontalAlignment = pair.Item1, VerticalAlignment = pair.Item2, Width = horizontal ? 6 : double.NaN, Height = vertical ? 6 : double.NaN, Cursor = horizontal && vertical ? Cursors.SizeNWSE : horizontal ? Cursors.SizeWE : Cursors.SizeNS, Background = Brushes.Transparent, Opacity = 0 };
            thumb.DragDelta += (_, e) =>
            {
                if (horizontal) { double value = Math.Clamp(Width + e.HorizontalChange * (pair.Item1 == HorizontalAlignment.Left ? -1 : 1), MinWidth, 1800); if (pair.Item1 == HorizontalAlignment.Left) Left -= value - Width; Width = value; }
                if (vertical) { double value = Math.Clamp(Height + e.VerticalChange * (pair.Item2 == VerticalAlignment.Top ? -1 : 1), MinHeight, 1600); if (pair.Item2 == VerticalAlignment.Top) Top -= value - Height; Height = value; }
            };
            thumb.DragCompleted += (_, _) => SaveWindow(); surface.Children.Add(thumb);
        }
    }
    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = Lang.T("导出完整备份"), Filter = Lang.T("论文进度备份 (*.json)|*.json"), FileName = Lang.F("论文进度-{0:yyyyMMdd-HHmm}.json", DateTime.Now) };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(store.FilePath), StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(store.BackupPath), StringComparison.OrdinalIgnoreCase)) throw new IOException(Lang.T("请另选位置，不要覆盖正在使用的资料文件。"));
            File.WriteAllText(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(library, Storage.JsonOptions), System.Text.Encoding.UTF8); footer.Text = Lang.T("完整备份已导出");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Lang.T("导出失败")); }
    }
    private void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Lang.T("导入备份（只添加新编号，不覆盖已有论文）"), Filter = Lang.T("论文进度备份 (*.json)|*.json") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 30_000_000) throw new InvalidDataException(Lang.T("文件超过 30 MB。"));
            var incoming = Storage.Parse(File.ReadAllText(dialog.FileName, System.Text.Encoding.UTF8));
            var merged = Storage.Merge(library, incoming); int added = merged.Papers.Count - library.Papers.Count;
            if (Commit(l => l.Papers = merged.Papers, Lang.F("导入了 {0} 篇新论文", added)))
                MessageBox.Show(this, Lang.F("新增 {0} 篇，已有编号的记录保持原样。\n导入前资料已自动备份。", added), Lang.T("导入完成"));
        }
        catch (Exception ex) { MessageBox.Show(this, Lang.T("文件没有导入，原资料保持原样。\n") + ex.Message, Lang.T("导入失败")); }
    }
    private void AddExamples() => Commit(l =>
    {
        var one = new Paper { Title = Lang.T("示例 · 论文 A"), Subject = Lang.T("管理学"), Language = Lang.T("中文"), Status = "写作中", NextAction = Lang.T("完成投稿前检查"), StartDate = DateTime.Today.AddDays(-24) };
        var two = new Paper { Title = Lang.T("示例 · 论文 B"), Subject = Lang.T("经济学"), Language = Lang.T("英文"), Status = "待返修", NextAction = Lang.T("整理审稿意见与回复"), DueDate = DateTime.Today.AddDays(7), StartDate = DateTime.Today.AddDays(-68) };
        var three = new Paper { Title = Lang.T("示例 · 论文 C"), Subject = Lang.T("公共管理"), Language = Lang.T("中文"), Status = "准备中", NextAction = Lang.T("整理语料与数据"), StartDate = DateTime.Today.AddDays(-5) };
        foreach (var (p, count) in new[] { (one, 4), (two, 5), (three, 1) }) { for (int i = 0; i < count; i++) p.Stages[i].Done = true; p.Record("添加示例论文 · 可编辑或归档"); l.Papers.Add(p); }
    }, Lang.T("已加入三篇示例，可编辑或归档"));
}
