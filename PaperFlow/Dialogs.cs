using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PaperFlow;

public sealed class PaperEditor : Window
{
    public Paper Result { get; }
    // knownTags：本机自建的标签 + 论文上已经贴过的标签。这里只负责"贴"，造标签在设置里。
    public PaperEditor(Paper paper, IReadOnlyList<string> knownTags)
    {
        Result = paper;
        Title = Lang.T("论文资料"); Width = Math.Min(660 * Appearance.DialogScale, SystemParameters.WorkArea.Width - 40); Height = Math.Min(780 * Appearance.DialogScale, SystemParameters.WorkArea.Height - 30); MinHeight = 430 * Appearance.DialogScale; MinWidth = Math.Min(530 * Appearance.DialogScale, SystemParameters.WorkArea.Width - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = true;
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll); var body = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; scroll.Content = body;
        var headline = MainWindow.Text(Lang.T("让下一步更清楚"), 22); headline.Margin = new Thickness(0, 0, 0, 6); body.Children.Add(headline);
        var caption = MainWindow.Text(Lang.T("阶段在小部件上直接勾选；这里保存论文的完整资料，也可以贴标签。"), 12, "#78867F"); caption.Margin = new Thickness(0, 0, 0, 20); body.Children.Add(caption);

        // 标签：最多三个，点一下贴上、再点一下摘掉。
        body.Children.Add(MainWindow.Text(Lang.T("标签（最多三个）"), 12, "#62766A"));
        if (knownTags.Count == 0) body.Children.Add(MainWindow.Text(Lang.T("还没有标签：在 设置 → 视图与分页 → 隐藏用标签 里自己建一个。"), 11, "#78867F"));
        var tagRow = new System.Windows.Controls.WrapPanel { Margin = new Thickness(0, 5, 0, 12) }; body.Children.Add(tagRow);
        var tagPicks = new List<CheckBox>();
        foreach (var value in knownTags)
        {
            var pick = new CheckBox { Content = value, IsChecked = paper.Tags.Contains(value), Margin = new Thickness(0, 0, 16, 6) };
            pick.Click += (_, _) =>
            {
                if (tagPicks.Count(x => x.IsChecked == true) > Schemes.MaxTags)
                {
                    pick.IsChecked = false;
                    MessageBox.Show(this, Lang.F("一篇论文最多贴 {0} 个标签。", Schemes.MaxTags));
                }
            };
            tagPicks.Add(pick); tagRow.Children.Add(pick);
        }

        TextBox Field(string label, string value, bool multiline = false)
        {
            var heading = MainWindow.Text(label, 12, "#62766A"); heading.Margin = new Thickness(0, 0, 0, 5); body.Children.Add(heading);
            var input = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 12), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MaxLength = label == Lang.T("论文标题") ? 500 : 30000 };
            if (multiline) { input.Height = 110; input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; input.VerticalContentAlignment = VerticalAlignment.Top; }
            AutomationProperties.SetName(input, label); body.Children.Add(input); return input;
        }
        var title = Field(Lang.T("论文标题"), paper.Title);
        body.Children.Add(MainWindow.Text(Lang.T("优先级"), 12, "#62766A"));
        // 下拉框显示跟着语言走，存进资料的仍是中文规范值。
        var priority = new ComboBox { ItemsSource = Lang.Choices(Paper.Priorities), DisplayMemberPath = "Label", SelectedIndex = Math.Max(0, Array.IndexOf(Paper.Priorities, paper.Priority)), Margin = new Thickness(0, 5, 0, 12) }; body.Children.Add(priority);
        var subject = Field(Lang.T("学科"), paper.Subject);
        var language = Field(Lang.T("语言"), paper.Language);
        var collaborators = Field(Lang.T("合作者"), paper.Collaborators);
        var journal = Field(Lang.T("目标期刊"), paper.Journal);
        body.Children.Add(MainWindow.Text(Lang.T("当前投稿状态"), 12, "#62766A"));
        var status = new ComboBox { ItemsSource = Lang.Choices(Paper.Statuses), DisplayMemberPath = "Label", SelectedIndex = Math.Max(0, Array.IndexOf(Paper.Statuses, paper.Status)), Margin = new Thickness(0, 5, 0, 12) }; body.Children.Add(status);
        var next = Field(Lang.T("下一步行动"), paper.NextAction);
        var dates = new Grid { Margin = new Thickness(0, 0, 0, 12) }; dates.ColumnDefinitions.Add(new ColumnDefinition()); dates.ColumnDefinitions.Add(new ColumnDefinition());
        var startPanel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; startPanel.Children.Add(MainWindow.Text(Lang.T("开始日期"), 12, "#62766A"));
        var start = new DatePicker { SelectedDate = paper.StartDate, Margin = new Thickness(0, 5, 0, 0) }; startPanel.Children.Add(start); dates.Children.Add(startPanel);
        var duePanel = new StackPanel(); duePanel.Children.Add(MainWindow.Text(Lang.T("下一步截止日期（可留空）"), 12, "#62766A"));
        var due = new DatePicker { SelectedDate = paper.DueDate, Margin = new Thickness(0, 5, 0, 0) }; duePanel.Children.Add(due); Grid.SetColumn(duePanel, 1); dates.Children.Add(duePanel); body.Children.Add(dates);
        bool invalidDate = false;
        start.DateValidationError += (_, _) => invalidDate = true;
        due.DateValidationError += (_, _) => invalidDate = true;
        start.SelectedDateChanged += (_, _) => invalidDate = false;
        due.SelectedDateChanged += (_, _) => invalidDate = false;
        // 2.0.0 起任意阶段都能标"不适用"：它从进度分母里去掉，不再只限返修。
        body.Children.Add(MainWindow.Text(Lang.T("这些阶段不适用（从进度分母里去掉）"), 12, "#62766A"));
        var skipRow = new System.Windows.Controls.WrapPanel { Margin = new Thickness(0, 5, 0, 14) }; body.Children.Add(skipRow);
        var skipPicks = new List<CheckBox>();
        foreach (var stage in paper.Stages)
        {
            var pick = new CheckBox { Content = Lang.T(Schemes.Display(stage.Name)), IsChecked = stage.Skipped, Margin = new Thickness(0, 0, 16, 6) };
            skipPicks.Add(pick); skipRow.Children.Add(pick);
        }
        var outcome = Field(Lang.T("结局"), paper.Outcome);
        var notes = Field(Lang.T("备注 / 投稿与返修历史"), paper.Notes, true);
        body.Children.Add(MainWindow.Text(Lang.P(paper.ElapsedDays, "已开始 {0} 天   ·   上次编辑 {1:yyyy-MM-dd HH:mm}", "Started {0} day ago   ·   last edited {1:yyyy-MM-dd HH:mm}", "Started {0} days ago   ·   last edited {1:yyyy-MM-dd HH:mm}", paper.ElapsedDays, paper.UpdatedAt), 11, "#78867F"));
        body.Children.Add(new TextBlock { Text = Lang.T("进度表示适用阶段的完成比例。勾选“收录”后，小部件显示已录用；若你用“收录”表示数据库收录，可在结局中另记录用时间。"), TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = MainWindow.Brush("#78867F"), Margin = new Thickness(0, 10, 0, 0) });
        var cancel = MainWindow.ActionButton(Lang.T("取消"), () => DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel);
        var save = MainWindow.ActionButton(Lang.T("保存资料"), () =>
        {
            // DatePicker commits edited text on focus loss before this click handler.
            if (string.IsNullOrWhiteSpace(title.Text)) { MessageBox.Show(this, Lang.T("请填写论文标题。")); title.Focus(); return; }
            if (invalidDate || start.SelectedDate == null || (!string.IsNullOrWhiteSpace(due.Text) && due.SelectedDate == null)) { MessageBox.Show(this, Lang.T("请填写有效日期，截止日期也可以留空。")); return; }
            if (start.SelectedDate.Value.Year < 1900 || start.SelectedDate.Value.Year > 2200 || due.SelectedDate?.Year < 1900 || due.SelectedDate?.Year > 2200) { MessageBox.Show(this, Lang.T("日期需在 1900—2200 年之间。")); return; }
            paper.Title = title.Text.Trim(); paper.Subject = subject.Text.Trim(); paper.Language = language.Text.Trim(); paper.Collaborators = collaborators.Text.Trim();
            paper.Priority = (priority.SelectedItem as Choice)?.Value ?? "中";
            paper.Journal = journal.Text.Trim(); paper.Status = (status.SelectedItem as Choice)?.Value ?? "准备中"; paper.NextAction = next.Text.Trim();
            paper.StartDate = start.SelectedDate.Value.Date; paper.DueDate = due.SelectedDate?.Date; paper.Outcome = outcome.Text.Trim(); paper.Notes = notes.Text;
            for (int i = 0; i < skipPicks.Count && i < paper.Stages.Count; i++)
            {
                var stage = paper.Stages[i];
                bool skipped = skipPicks[i].IsChecked == true;
                if (stage.Skipped == skipped) continue;
                stage.Skipped = skipped;
                if (skipped) stage.Done = false;
                paper.Record((skipped ? "阶段设为不适用 · " : "阶段恢复为适用 · ") + Schemes.Display(stage.Name));
            }
            paper.Tags = tagPicks.Where(x => x.IsChecked == true).Select(x => (string)x.Content).Distinct().ToList();
            DialogResult = true;
        }, true); actions.Children.Add(save);
    }
}

