using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PaperProgress;

public sealed class PaperEditor : Window
{
    public Paper Result { get; }
    public PaperEditor(Paper paper)
    {
        Result = paper;
        Title = "论文资料"; Width = 660; Height = Math.Min(780, SystemParameters.WorkArea.Height); MinHeight = 430; MinWidth = 530;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll); var body = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; scroll.Content = body;
        var headline = MainWindow.Text("让下一步更清楚", 22); headline.Margin = new Thickness(0, 0, 0, 6); body.Children.Add(headline);
        var caption = MainWindow.Text("七阶段在挂件上直接勾选；这里保存论文的完整资料。", 12, "#78867F"); caption.Margin = new Thickness(0, 0, 0, 20); body.Children.Add(caption);

        TextBox Field(string label, string value, bool multiline = false)
        {
            var heading = MainWindow.Text(label, 12, "#62766A"); heading.Margin = new Thickness(0, 0, 0, 5); body.Children.Add(heading);
            var input = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 12), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MaxLength = label == "论文标题" ? 500 : 30000 };
            if (multiline) { input.Height = 110; input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; input.VerticalContentAlignment = VerticalAlignment.Top; }
            AutomationProperties.SetName(input, label); body.Children.Add(input); return input;
        }
        var title = Field("论文标题", paper.Title);
        var subject = Field("学科", paper.Subject);
        var language = Field("语言", paper.Language);
        var collaborators = Field("合作者", paper.Collaborators);
        var journal = Field("目标期刊", paper.Journal);
        body.Children.Add(MainWindow.Text("当前投稿状态", 12, "#62766A"));
        var status = new ComboBox { ItemsSource = Paper.Statuses, SelectedItem = paper.Status, Margin = new Thickness(0, 5, 0, 12) }; body.Children.Add(status);
        var next = Field("下一步行动", paper.NextAction);
        var dates = new Grid { Margin = new Thickness(0, 0, 0, 12) }; dates.ColumnDefinitions.Add(new ColumnDefinition()); dates.ColumnDefinitions.Add(new ColumnDefinition());
        var startPanel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; startPanel.Children.Add(MainWindow.Text("开始日期", 12, "#62766A"));
        var start = new DatePicker { SelectedDate = paper.StartDate, Margin = new Thickness(0, 5, 0, 0) }; startPanel.Children.Add(start); dates.Children.Add(startPanel);
        var duePanel = new StackPanel(); duePanel.Children.Add(MainWindow.Text("下一步截止日期（可留空）", 12, "#62766A"));
        var due = new DatePicker { SelectedDate = paper.DueDate, Margin = new Thickness(0, 5, 0, 0) }; duePanel.Children.Add(due); Grid.SetColumn(duePanel, 1); dates.Children.Add(duePanel); body.Children.Add(dates);
        bool invalidDate = false;
        start.DateValidationError += (_, _) => invalidDate = true;
        due.DateValidationError += (_, _) => invalidDate = true;
        start.SelectedDateChanged += (_, _) => invalidDate = false;
        due.SelectedDateChanged += (_, _) => invalidDate = false;
        var skip = new CheckBox { Content = "返修不适用（无需返修即录用，按其余六阶段计算）", IsChecked = paper.Stages[5].Skipped, Margin = new Thickness(0, 0, 0, 14) }; body.Children.Add(skip);
        var outcome = Field("结局", paper.Outcome);
        var notes = Field("备注 / 投稿与返修历史", paper.Notes, true);
        body.Children.Add(MainWindow.Text($"已开始 {paper.ElapsedDays} 天   ·   上次编辑 {paper.UpdatedAt:yyyy-MM-dd HH:mm}", 11, "#78867F"));
        body.Children.Add(new TextBlock { Text = "进度表示适用阶段的完成比例。勾选“收录”后，挂件显示已录用；若你用“收录”表示数据库收录，可在结局中另记录用时间。", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = MainWindow.Brush("#78867F"), Margin = new Thickness(0, 10, 0, 0) });
        var cancel = MainWindow.ActionButton("取消", () => DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel);
        var save = MainWindow.ActionButton("保存资料", () =>
        {
            // DatePicker commits edited text on focus loss before this click handler.
            if (string.IsNullOrWhiteSpace(title.Text)) { MessageBox.Show(this, "请填写论文标题。"); title.Focus(); return; }
            if (invalidDate || start.SelectedDate == null || (!string.IsNullOrWhiteSpace(due.Text) && due.SelectedDate == null)) { MessageBox.Show(this, "请填写有效日期，截止日期也可以留空。"); return; }
            if (start.SelectedDate.Value.Year < 1900 || start.SelectedDate.Value.Year > 2200 || due.SelectedDate?.Year < 1900 || due.SelectedDate?.Year > 2200) { MessageBox.Show(this, "日期需在 1900—2200 年之间。"); return; }
            paper.Title = title.Text.Trim(); paper.Subject = subject.Text.Trim(); paper.Language = language.Text.Trim(); paper.Collaborators = collaborators.Text.Trim();
            paper.Journal = journal.Text.Trim(); paper.Status = status.SelectedItem as string ?? "准备中"; paper.NextAction = next.Text.Trim();
            paper.StartDate = start.SelectedDate.Value.Date; paper.DueDate = due.SelectedDate?.Date; paper.Outcome = outcome.Text.Trim(); paper.Notes = notes.Text;
            bool skipped = skip.IsChecked == true;
            if (paper.Stages[5].Skipped != skipped) paper.Record(skipped ? "返修设为不适用 · 从进度分母排除" : "返修恢复为适用阶段");
            paper.Stages[5].Skipped = skipped; if (skipped) paper.Stages[5].Done = false;
            DialogResult = true;
        }, true); actions.Children.Add(save);
    }
}

