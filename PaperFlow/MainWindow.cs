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
    private readonly Button options = new();
    private readonly StackPanel cards = new();
    private readonly TextBlock summary = new();
    private readonly TextBlock footer = new();
    private readonly StackPanel pager = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 7) };
    private readonly TextBlock pageLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), FontSize = 12 };
    private readonly TextBox search = new();
    private readonly ComboBox filter = new();
    private readonly ComboBox sort = new();
    private readonly Button pin = new();
    private readonly Button compact = new();
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

    public static SolidColorBrush Brush(string color) => Appearance.Map(color);
    public static TextBlock Text(string text, double size = 13, string color = "#24352F") => new() { Text = text, FontSize = size * Appearance.Scale, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
    public static Button ActionButton(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, FontSize = 12, Margin = new Thickness(3, 0, 0, 0) };
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
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; AllowsTransparency = true; Background = Brushes.Transparent;
        FontFamily = new FontFamily(library.Settings.FontName); FontSize = library.Settings.TextSize;
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
        frame.Child = root;

        var heading = new Grid { Margin = new Thickness(14, 8, 10, 5), Background = Brushes.Transparent, ToolTip = "拖动标题栏或空白处移动挂件" };
        heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(brandIcon);
        var brandTitle = Text("PaperFlow", 18); brandTitle.FontWeight = FontWeights.SemiBold; brandTitle.FontSize = 18; brandTitle.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); brand.Children.Add(brandTitle);
        heading.Children.Add(brand);
        var chrome = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var add = ActionButton("＋", AddPaper, true); add.ToolTip = "新增论文 · Ctrl+N"; add.Padding = new Thickness(10, 4, 10, 4); add.FontSize = 17; AutomationProperties.SetName(add, "新增论文"); chrome.Children.Add(add);
        options.Content = "论文选项"; options.FontSize = 12; options.Padding = new Thickness(8, 6, 8, 6); options.Margin = new Thickness(4, 0, 0, 0); options.Click += (_, _) => OpenOptions(); chrome.Children.Add(options);
        pin.FontSize = 12; pin.Padding = new Thickness(8, 6, 8, 6); pin.Click += (_, _) => TogglePin(); chrome.Children.Add(pin);
        chrome.Children.Add(ActionButton("设置", OpenSettings));
        chrome.Children.Add(ActionButton("—", () => WindowState = WindowState.Minimized));
        var close = ActionButton("×", Close); close.ToolTip = "收起到系统托盘，双击托盘图标恢复"; chrome.Children.Add(close);
        Grid.SetColumn(chrome, 1); heading.Children.Add(chrome);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);

        var top = new StackPanel { Margin = new Thickness(17, 0, 17, 6), Background = Brushes.Transparent };
        summary.FontSize = 11; summary.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); top.Children.Add(summary);
        AutomationProperties.SetName(search, "搜索论文、学科、期刊"); search.ToolTip = "搜索论文、学科、期刊、合作者或备注";
        search.TextChanged += (_, _) => { if (ready) Render(); };
        filter.ItemsSource = new[] { "全部论文", "进行中", "已收录", "已归档" }; filter.SelectedIndex = 0; filter.Margin = new Thickness(7, 0, 0, 0);
        filter.SelectionChanged += (_, _) => { if (ready) Render(); };
        sort.ItemsSource = new[] { "手动排序", "最近修改", "截止日期", "进度优先" }; sort.SelectedIndex = 0; sort.Margin = new Thickness(7, 0, 0, 0);
        sort.SelectionChanged += (_, _) => { if (ready) Render(); };
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);

        var foot = new Border { Padding = new Thickness(20, 8, 20, 10), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Brush("#E0E5DD") };
        footer.Text = sync.Status; footer.FontSize = 10; footer.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); footer.TextTrimming = TextTrimming.CharacterEllipsis; foot.Child = footer;
        DockPanel.SetDock(foot, Dock.Bottom); root.Children.Add(foot);
        pager.Children.Add(ActionButton("‹ 上一页", () => TurnPage(-1)));
        pager.Children.Add(pageLabel);
        pager.Children.Add(ActionButton("下一页 ›", () => TurnPage(1)));
        DockPanel.SetDock(pager, Dock.Bottom); root.Children.Add(pager);
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

        tray = new Forms.NotifyIcon { Icon = CreateTrayIcon(), Text = "PaperFlow · 双击打开", Visible = !demonstration };
        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("显示 PaperFlow", null, (_, _) => Dispatcher.Invoke(Reveal));
        trayMenu.Items.Add("始终置顶 / 取消置顶", null, (_, _) => Dispatcher.Invoke(TogglePin));
        trayMenu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));
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
                footer.ToolTip = sync.Folder == "" ? store.DirectoryPath : "资料文件夹：" + sync.Folder + "\n跨设备到达时间由 OneDrive 决定，文件夹更新不等于云端上传已完成。";
            }
            catch (Exception ex) { footer.Text = "同步需要留意 · " + ex.Message; }
            finally { polling = false; }
        };
        if (!demonstration) timer.Start(); ready = true; Render();
        if (demonstration) { footer.Text = "演示数据 · 所有论文均为虚构 · " + Product.Name + " " + Product.Version; footer.ToolTip = null; }
        SessionEndingHook();
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
        using var source = new System.Drawing.Icon(stream, 32, 32); return (System.Drawing.Icon)source.Clone();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    private void SessionEndingHook() => Application.Current.SessionEnding += (_, _) => SaveWindow();
    private void Reveal() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void ExitApplication() { if (!SaveWindow()) return; quitting = true; Close(); }
    internal void CloseDemonstration() { if (!demonstration) throw new InvalidOperationException("仅供演示导出。"); quitting = true; Close(); }

    // Write a complete candidate snapshot before adopting it, so failed writes do not appear saved.
    private bool Commit(Action<Library> edit, string notice = "已保存")
    {
        try
        {
            var candidate = Storage.CloneLibrary(library); edit(candidate);
            sync.Commit(library, candidate); candidate.Papers = sync.Snapshot().Papers; library = candidate;
            try { store.Save(candidate); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { footer.Text = "修改已记录，快照备份待重试 · " + ex.Message; Render(); return true; }
            footer.Text = store.BackupNotice == null ? $"{notice} · {DateTime.Now:HH:mm} · 本地自动备份" : "已保存 · " + store.BackupNotice;
            footer.ToolTip = store.BackupNotice; Render(); return true;
        }
        catch (Exception ex)
        {
            Render(); footer.Text = "保存失败 · 本次修改未生效";
            MessageBox.Show(this, "无法保存，本次修改没有写入。\n\n" + ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error); return false;
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
        if (Commit(l => l.Papers.Insert(0, paper), "已新增论文")) { filter.SelectedIndex = 0; search.Clear(); scroller.ScrollToTop(); }
    }

    private void Render()
    {
        // A nested OLE drag loop still runs the synchronization timer. Defer rebuilding
        // controls until drop/cancel, while continuing to receive and save remote data.
        if (draggingPaper) return;
        Appearance.Apply(library.Settings);
        brandIcon.Source = Appearance.CreateHeaderIcon();
        FontFamily = new FontFamily(Appearance.FontName); FontSize = library.Settings.TextSize;
        frame.Background = Appearance.Paint(Appearance.Current.Window, Appearance.Opacity * (Appearance.Current.Glass ? .25 : 1));
        frame.BorderBrush = Appearance.Paint(Appearance.Current.Border, Appearance.Opacity * .75);
        pin.Content = library.Settings.Topmost ? "已置顶" : "置顶"; pin.ToolTip = "F12 切换置顶";
        pin.Foreground = library.Settings.Topmost ? Brush("#21846B") : Brush("#78867F");
        compact.Content = library.Settings.Compact ? "展开" : "紧凑";
        var active = library.Papers.Where(p => !p.Archived).ToList();
        summary.Text = $"{active.Count} 篇论文   ·   {active.Count(p => !p.Stages[6].Done)} 篇推进中   ·   {active.Count(p => p.Stages[6].Done)} 篇已收录" + (filter.SelectedIndex != 0 || search.Text != "" ? "   ·   已筛选" : "");
        IEnumerable<Paper> visible = library.Papers.Where(p => filter.SelectedIndex == 3 ? p.Archived : !p.Archived);
        if (filter.SelectedIndex == 1) visible = visible.Where(p => !p.Stages[6].Done);
        if (filter.SelectedIndex == 2) visible = visible.Where(p => p.Stages[6].Done);
        var term = search.Text.Trim();
        if (term != "") visible = visible.Where(p => string.Join(" ", p.Title, p.Subject, p.Language, p.Collaborators, p.Journal, p.Notes, p.Status, p.NextAction).Contains(term, StringComparison.OrdinalIgnoreCase));
        visible = sort.SelectedIndex switch { 1 => visible.OrderByDescending(p => p.UpdatedAt), 2 => visible.OrderBy(p => p.DueDate ?? DateTime.MaxValue), 3 => visible.OrderByDescending(p => p.Progress), _ => visible };
        var candidates = visible.ToList();
        var pagePapers = ViewRules.Apply(candidates, library.Settings);
        summary.Text = $"{active.Count} 篇论文 · 当前显示 {pagePapers.Count} 篇" + (library.Settings.PageMode == ViewRules.PageModes[2] ? " · 阶段分组" : library.Settings.HideSelectedStages ? $" · 按阶段隐藏 {candidates.Count(p => ViewRules.SelectedStage(p, library.Settings))} 篇" : "");
        summary.ToolTip = "隐藏和翻页仅改变显示，不删除论文。点击论文选项调整。";
        pager.Visibility = ViewRules.PageCount(library.Settings) > 1 ? Visibility.Visible : Visibility.Collapsed;
        pageLabel.Text = $"{ViewRules.PageTitle(library.Settings)} · {library.Settings.PageIndex + 1}/{ViewRules.PageCount(library.Settings)}";
        pageLabel.Foreground = Brush("#24352F");
        pageLabel.ToolTip = library.Settings.PageMode == ViewRules.PageModes[2] ? "所选阶段：" + string.Join("、", library.Settings.HiddenStages.Select(i => Paper.StageLabels[i])) : null;
        cards.Children.Clear();
        foreach (var p in pagePapers) cards.Children.Add(BuildCard(p));
        if (cards.Children.Count == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(20, 45, 20, 40) };
            var title = Text(library.Papers.Count == 0 ? "从第一篇论文开始" : "这里暂时没有论文", 22); title.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(title);
            var hint = Text(library.Papers.Count == 0 ? "点击右上角 ＋，输入论文标题。\n七个阶段和进度条会自动准备好。" : "论文可能在其他页，或被当前筛选隐藏。\n点击论文选项调整显示范围。", 13, "#78867F");
            hint.TextAlignment = TextAlignment.Center; hint.Margin = new Thickness(0, 15, 0, 20); empty.Children.Add(hint);
            if (library.Papers.Count == 0) { var demo = ActionButton("查看三篇示例", AddExamples); demo.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(demo); }
            else { var adjust = ActionButton("调整论文选项", OpenOptions); adjust.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(adjust); }
            cards.Children.Add(empty);
        }
    }

    private Border BuildCard(Paper p)
    {
        bool small = library.Settings.Compact;
        var card = new Border { Tag = p.Id, Background = Appearance.Paint(Appearance.Current.Card, Appearance.Opacity), CornerRadius = new CornerRadius(10), BorderBrush = Appearance.Paint(Appearance.Current.Border, Appearance.Opacity * .8), BorderThickness = new Thickness(1), Padding = new Thickness(11, small ? 6 : 8, 11, small ? 3 : 5), Margin = new Thickness(4, 0, 4, 6) };
        var stack = new StackPanel(); card.Child = stack;
        var titleArea = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new Thickness(0, 0, 0, 1) };
        var dots = PriorityDots(p);
        titleArea.Children.Add(dots);
        var name = Text(p.Title, small ? 14 : 15); name.FontWeight = library.Settings.TitleBold ? FontWeights.SemiBold : FontWeights.Normal; name.TextTrimming = TextTrimming.CharacterEllipsis; name.VerticalAlignment = VerticalAlignment.Center; name.ToolTip = p.Title + "\n拖动调整优先顺序";
        titleArea.Children.Add(name); stack.Children.Add(titleArea);
        var metadata = string.Join(" / ", new[] { p.Subject, p.Language, p.Journal }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var progressRow = new Grid { Margin = new Thickness(0, 2, 0, 3), ToolTip = "下一步：" + (p.NextAction != "" ? p.NextAction : p.NextStage) };
        progressRow.ColumnDefinitions.Add(new ColumnDefinition()); progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85 * Appearance.Scale) });
        var track = new Grid { Height = library.Settings.BarHeight, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border { Background = Brush("#EBEFE9"), CornerRadius = new CornerRadius(5) });
        var inner = new Grid(); inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, p.Progress), GridUnitType.Star) }); inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 100 - p.Progress), GridUnitType.Star) });
        var fill = new Border { Background = Brush(p.Stages[6].Done ? "#2F8B6D" : p.Status == "待返修" ? "#C69544" : "#4A9E83"), CornerRadius = new CornerRadius(5) }; inner.Children.Add(fill); track.Children.Add(inner);
        AutomationProperties.SetName(track, $"{p.Title} 进度 {p.Progress}%"); progressRow.Children.Add(track);
        var pct = Text($"{p.Progress}%", small ? 20 : 23); pct.Foreground = Appearance.Paint(Appearance.ProgressInk); pct.FontWeight = FontWeights.SemiBold; pct.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(pct, 1); progressRow.Children.Add(pct); stack.Children.Add(progressRow);
        var checks = new StageFlowPanel();
        for (int i = 0; i < p.Stages.Count; i++)
        {
            int index = i; var stage = p.Stages[i];
            var cb = new CheckBox { Content = Paper.StageLabels[i] + (stage.Skipped ? "（免）" : ""), IsChecked = stage.Done, IsEnabled = !stage.Skipped, FontSize = (small ? 11 : 12) * Appearance.Scale };
            cb.SetResourceReference(StyleProperty, "StageCheck"); AutomationProperties.SetName(cb, p.Title + " · " + Paper.StageLabels[i]);
            cb.Padding = new Thickness(small ? 4 : 5, 3, small ? 4 : 5, 3); cb.Margin = new Thickness(0, 0, 4, 3);
            cb.ToolTip = stage.Skipped ? "返修已设为不适用，可在论文资料中恢复" : i == 4 ? "勾选表示进入在审；继续勾选返修或收录后，移出在审分组。" : "点击切换；进度按适用阶段等权计算";
            cb.Click += (_, _) => Commit(l => l.Papers.Single(x => x.Id == p.Id).ToggleStage(index, cb.IsChecked == true)); checks.Children.Add(cb);
        }
        var due = Text(p.DueDate != null && !p.Stages[6].Done ? p.DeadlineText : $"已开始 {p.ElapsedDays} 天", 11, p.DueDate?.Date < DateTime.Today && !p.Stages[6].Done ? "#BE624C" : "#8A948C");
        due.Margin = new Thickness(4, 0, 5, 3); due.ToolTip = $"开始日期：{p.StartDate:yyyy-MM-dd}\n已开始 {p.ElapsedDays} 天"; checks.Children.Add(due);
        if (!string.IsNullOrWhiteSpace(p.NextAction))
        {
            var next = Text("下一步 " + p.NextAction, 11, "#6C7C70"); next.MaxWidth = 150 * Appearance.Scale; next.TextTrimming = TextTrimming.CharacterEllipsis; next.ToolTip = p.NextAction; next.Margin = new Thickness(4, 0, 0, 3); checks.Children.Add(next); checks.OptionalTail = next;
        }
        // One settings entry per paper, anchored at the bottom right corner of the card.
        var bottom = new Grid();
        bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.Children.Add(checks);
        var settings = new Button { Content = "⚙", FontSize = 13 * Appearance.Scale, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(7, 0, 0, 3), VerticalAlignment = VerticalAlignment.Bottom, Foreground = Brush("#62766A") };
        settings.ToolTip = (metadata == "" ? "尚未填写学科、期刊等资料" : metadata + " · " + p.EffectiveStatus) + "\n编辑资料、修改记录、排序、复制、归档";
        settings.Click += (_, _) => PaperMenu(p, settings);
        AutomationProperties.SetName(settings, p.Title + " 的论文设置");
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
        var dots = new Border { Child = row, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Padding = new Thickness(0, 3, 8 * Appearance.Scale, 3), VerticalAlignment = VerticalAlignment.Center, ToolTip = $"{p.Priority}优先级 · 三个点分别代表高、中、低\n点击修改，按住拖动调整顺序" };
        AutomationProperties.SetName(dots, p.Title + " 的优先级：" + p.Priority);
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
            if (Commit(l => { if (PaperOrder.MoveVisible(l.Papers, visible, source, id, after)) l.Papers.Single(p => p.Id == source).Record("调整论文优先顺序"); }, "已保存优先顺序")) sort.SelectedIndex = 0;
        };
    }

    private void PaperMenu(Paper p, FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor };
        void Item(string title, Action action) { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("编辑论文资料", () => EditPaper(p));
        Item("查看修改记录", () => ShowHistory(p));
        menu.Items.Add(new Separator());
        Item("上移一位", () => MovePaper(p.Id, -1)); Item("下移一位", () => MovePaper(p.Id, 1));
        Item("复制为新论文（阶段清零）", () => Commit(l => { var copy = Storage.Clone(p); copy.Id = Guid.NewGuid().ToString("N"); copy.Title = p.Title.Length > 490 ? p.Title[..490] + "（副本）" : p.Title + "（副本）"; copy.Stages = Paper.StageNames.Select(n => new Stage { Name = n }).ToList(); copy.History.Clear(); copy.Archived = false; copy.StartDate = DateTime.Today; copy.DueDate = null; copy.Status = "准备中"; copy.NextAction = ""; copy.Outcome = ""; copy.Notes = ""; copy.Record("复制论文资料 · 七阶段清零"); l.Papers.Insert(0, copy); }));
        Item(p.Archived ? "恢复到论文列表" : "归档（保留资料）", () => Commit(l => { var paper = l.Papers.Single(x => x.Id == p.Id); paper.Archived = !paper.Archived; paper.Record(paper.Archived ? "归档论文" : "恢复论文"); }));
        menu.IsOpen = true;
    }
    private void PriorityMenu(Paper p, FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor };
        foreach (var value in Paper.Priorities)
        {
            var item = new MenuItem { Header = value + "优先级", IsCheckable = true, IsChecked = p.Priority == value };
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
        var box = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18), Text = string.Join("\n\n", p.History.Select(h => $"{h.At:yyyy-MM-dd HH:mm:ss}   {h.Description}")) };
        new Window { Title = "修改记录 · " + p.Title, Owner = this, Width = 560, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = box }.ShowDialog();
    }
    private void OpenSettings()
    {
        var original = Storage.CloneLibrary(library).Settings;
        var dialog = new SettingsWindow(library.Settings, store.DirectoryPath, Export, Import, preview => { Appearance.Apply(preview); library.Settings = preview; Render(); }) { Owner = this };
        if (dialog.ShowDialog() == true) Commit(l => l.Settings = dialog.Result);
        else Commit(l => l.Settings = original);
    }
    private void OpenOptions()
    {
        var window = new Window { Title = "论文选项", Width = 490, Height = Math.Min(760, SystemParameters.WorkArea.Height - 30), Owner = this, ResizeMode = ResizeMode.CanResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(22) }; window.Content = root;
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(controls, Dock.Bottom); root.Children.Add(controls);
        var body = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(Text("搜索与视图", 20));
        body.Children.Add(Text("搜索论文、期刊、学科或合作者", 11, "#78867F"));
        search.Margin = new Thickness(0, 9, 0, 12); body.Children.Add(search);
        body.Children.Add(Text("显示范围", 12)); filter.Margin = new Thickness(0, 5, 0, 12); body.Children.Add(filter);
        body.Children.Add(Text("排序方式", 12)); sort.Margin = new Thickness(0, 5, 0, 12); body.Children.Add(sort);
        var small = new CheckBox { Content = "紧凑视图（保留七阶段）", IsChecked = library.Settings.Compact }; small.Click += (_, _) => Commit(l => l.Settings.Compact = small.IsChecked == true); body.Children.Add(small);
        void Label(string text) { var label = Text(text, 13); label.Margin = new Thickness(0, 16, 0, 7); body.Children.Add(label); }
        void ChangeView(Action<Preferences> change) => Commit(l => { change(l.Settings); l.Settings.PageIndex = 0; });
        Label("只显示这些优先级");
        var priorities = new WrapPanel(); body.Children.Add(priorities);
        foreach (var value in Paper.Priorities)
        {
            var check = new CheckBox { Content = value + "优先级", IsChecked = library.Settings.VisiblePriorities.Contains(value), Margin = new Thickness(0, 0, 18, 5) };
            check.Click += (_, _) => ChangeView(p => { p.VisiblePriorities.Remove(value); if (check.IsChecked == true) p.VisiblePriorities.Add(value); }); priorities.Children.Add(check);
        }
        Label("暂时隐藏的阶段");
        var hide = new CheckBox { Content = "隐藏所选阶段的论文", IsChecked = library.Settings.HideSelectedStages, Margin = new Thickness(0, 0, 0, 8) };
        hide.Click += (_, _) => ChangeView(p => p.HideSelectedStages = hide.IsChecked == true); body.Children.Add(hide);
        var stages = new WrapPanel(); body.Children.Add(stages);
        for (int i = 0; i < Paper.StageLabels.Length; i++)
        {
            int index = i;
            var check = new CheckBox { Content = Paper.StageLabels[i], IsChecked = library.Settings.HiddenStages.Contains(i), Margin = new Thickness(0, 0, 14, 7) };
            check.Click += (_, _) => ChangeView(p => { p.HiddenStages.Remove(index); if (check.IsChecked == true) p.HiddenStages.Add(index); }); stages.Children.Add(check);
        }
        var explanation = Text("按最后一个已勾选阶段归类；未勾选时归入开题。\n例如：在审后进入返修，会重新显示。", 11, "#78867F"); explanation.TextWrapping = TextWrapping.Wrap; body.Children.Add(explanation);
        Label("翻页方式");
        var paging = new ComboBox { ItemsSource = ViewRules.PageModes, SelectedItem = library.Settings.PageMode }; body.Children.Add(paging);
        paging.SelectionChanged += (_, _) => ChangeView(p => p.PageMode = paging.SelectedItem as string ?? ViewRules.PageModes[0]);
        var pageHint = Text("优先级：高 → 中 → 低。\n阶段分组：第一页排除所选阶段，第二页只看所选阶段。\n阶段分页会将隐藏项放到第二页；优先级筛选仍生效。", 11, "#78867F"); pageHint.TextWrapping = TextWrapping.Wrap; pageHint.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(pageHint);
        controls.Children.Add(ActionButton("显示全部", () => { search.Clear(); filter.SelectedIndex = 0; ChangeView(p => { p.HideSelectedStages = false; p.PageMode = ViewRules.PageModes[0]; p.VisiblePriorities = Paper.Priorities.ToList(); }); window.Close(); }));
        controls.Children.Add(ActionButton("完成", window.Close, true));
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
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "导出完整备份", Filter = "论文进度备份 (*.json)|*.json", FileName = $"论文进度-{DateTime.Now:yyyyMMdd-HHmm}.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(store.FilePath), StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(store.BackupPath), StringComparison.OrdinalIgnoreCase)) throw new IOException("请另选位置，不要覆盖正在使用的资料文件。");
            File.WriteAllText(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(library, Storage.JsonOptions), System.Text.Encoding.UTF8); footer.Text = "完整备份已导出";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败"); }
    }
    private void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "导入备份（只添加新编号，不覆盖已有论文）", Filter = "论文进度备份 (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 30_000_000) throw new InvalidDataException("文件超过 30 MB。");
            var incoming = Storage.Parse(File.ReadAllText(dialog.FileName, System.Text.Encoding.UTF8));
            var merged = Storage.Merge(library, incoming); int added = merged.Papers.Count - library.Papers.Count;
            if (Commit(l => l.Papers = merged.Papers, $"导入了 {added} 篇新论文"))
                MessageBox.Show(this, $"新增 {added} 篇，已有编号的记录保持原样。\n导入前资料已自动备份。", "导入完成");
        }
        catch (Exception ex) { MessageBox.Show(this, "文件没有导入，原资料保持原样。\n" + ex.Message, "导入失败"); }
    }
    private void AddExamples() => Commit(l =>
    {
        var one = new Paper { Title = "示例 · 论文 A", Subject = "管理学", Language = "中文", Status = "写作中", NextAction = "完成投稿前检查", StartDate = DateTime.Today.AddDays(-24) };
        var two = new Paper { Title = "示例 · 论文 B", Subject = "经济学", Language = "英文", Status = "待返修", NextAction = "整理审稿意见与回复", DueDate = DateTime.Today.AddDays(7), StartDate = DateTime.Today.AddDays(-68) };
        var three = new Paper { Title = "示例 · 论文 C", Subject = "公共管理", Language = "中文", Status = "准备中", NextAction = "整理语料与数据", StartDate = DateTime.Today.AddDays(-5) };
        foreach (var (p, count) in new[] { (one, 4), (two, 5), (three, 1) }) { for (int i = 0; i < count; i++) p.Stages[i].Done = true; p.Record("添加示例论文 · 可编辑或归档"); l.Papers.Add(p); }
    }, "已加入三篇示例，可编辑或归档");
}
