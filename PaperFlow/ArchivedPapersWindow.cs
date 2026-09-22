using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace PaperFlow;

public sealed class DeletePaperDialog : Window
{
    public DeletePaperDialog(string title)
    {
        Title = Lang.T("删除论文"); Width = Math.Min(460 * Appearance.DialogScale, SystemParameters.WorkArea.Width - 40);
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBackground"); SetResourceReference(ForegroundProperty, "Ink");
        var body = new StackPanel { Margin = new Thickness(22) }; Content = body;
        var name = MainWindow.Text(title, 16); name.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); name.TextWrapping = TextWrapping.Wrap; body.Children.Add(name);
        var warning = MainWindow.Text(Lang.T("删除后不可恢复，确定执行删除吗？"), 14);
        warning.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        warning.TextWrapping = TextWrapping.Wrap; warning.Margin = new Thickness(0, 14, 0, 20); body.Children.Add(warning);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; body.Children.Add(actions);
        var cancel = MainWindow.ActionButton(Lang.T("取消"), () => DialogResult = false); cancel.IsCancel = true; cancel.IsDefault = true;
        actions.Children.Add(cancel); actions.Children.Add(MainWindow.ActionButton(Lang.T("删除论文"), () => DialogResult = true));
        Loaded += (_, _) => cancel.Focus();
    }
}

public sealed class ArchivedPapersWindow : Window
{
    private readonly Func<IReadOnlyList<Paper>> papers;
    private readonly Action<Paper, Window> edit;
    private readonly Func<Paper, bool> restore;
    private readonly Func<Paper, Window, bool> delete;
    private readonly TextBox search = new();
    private readonly StackPanel rows = new();
    private readonly TextBlock count = new();

    public ArchivedPapersWindow(Func<IReadOnlyList<Paper>> papers, Action<Paper, Window> edit, Func<Paper, bool> restore, Func<Paper, Window, bool> delete)
    {
        this.papers = papers; this.edit = edit; this.restore = restore; this.delete = delete;
        Title = Lang.T("已归档论文"); Width = Math.Min(700 * Appearance.DialogScale, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(620 * Appearance.DialogScale, SystemParameters.WorkArea.Height - 30);
        MinWidth = Math.Min(420 * Appearance.DialogScale, Width); MinHeight = Math.Min(320 * Appearance.DialogScale, Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = true;
        SetResourceReference(BackgroundProperty, "WindowBackground"); SetResourceReference(ForegroundProperty, "Ink");
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(Text(Lang.T("已归档论文"), 22));
        var hint = Text(Lang.T("这里显示全部归档论文，不受挂件的隐藏、筛选和分页影响。"), 12);
        hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new Thickness(0, 8, 0, 12); header.Children.Add(hint);
        header.Children.Add(Text(Lang.T("搜索归档论文标题"), 12));
        AutomationProperties.SetName(search, Lang.T("搜索归档论文标题")); search.Margin = new Thickness(0, 5, 0, 8); header.Children.Add(search);
        count.Margin = new Thickness(0, 0, 0, 10); count.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); header.Children.Add(count);
        var close = MainWindow.ActionButton(Lang.T("关闭"), Close); close.IsCancel = true; close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        search.TextChanged += (_, _) => Refresh(); Loaded += (_, _) => search.Focus(); Refresh();
    }

    public void Refresh()
    {
        var archived = papers().Where(p => p.Archived).ToList();
        var matches = archived.Where(p => p.Title.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        count.Text = Lang.F("已归档 {0} 篇 · 当前显示 {1} 篇", archived.Count, matches.Count); rows.Children.Clear();
        if (matches.Count == 0)
        {
            var empty = Text(Lang.T(archived.Count == 0 ? "还没有归档论文。" : "没有匹配的归档论文。"), 14);
            empty.Margin = new Thickness(0, 20, 0, 20); rows.Children.Add(empty); return;
        }
        foreach (var paper in matches)
        {
            var row = new StackPanel { Tag = paper.Id, Margin = new Thickness(0, 0, 0, 16) };
            var title = Text(paper.Title, 15); title.TextWrapping = TextWrapping.Wrap; row.Children.Add(title);
            row.Children.Add(Text(Lang.F("进度 {0}% · {1}优先级", paper.Progress, Lang.Value(paper.Priority)), 12));
            var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; row.Children.Add(actions);
            actions.Children.Add(MainWindow.ActionButton(Lang.T("编辑论文资料"), () => { edit(paper, this); Refresh(); }));
            actions.Children.Add(MainWindow.ActionButton(Lang.T("恢复到论文列表"), () => { if (restore(paper)) Refresh(); }));
            actions.Children.Add(MainWindow.ActionButton(Lang.T("删除论文"), () => { if (delete(paper, this)) Refresh(); }));
            rows.Children.Add(row);
        }
    }

    private static TextBlock Text(string value, double size)
    {
        var text = MainWindow.Text(value, size); text.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); return text;
    }
}
