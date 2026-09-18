using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PaperFlow;

// 简短的文字输入框（给"另存为方案"起名字用）。WPF 没有自带的输入框。
public sealed class TextPrompt : Window
{
    public string Value { get; private set; } = "";
    public TextPrompt(string headline, string hint, string initial)
    {
        Title = headline; Width = Math.Min(420 * Appearance.DialogScale, SystemParameters.WorkArea.Width - 40); SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        var body = new StackPanel { Margin = new Thickness(20) };
        var label = MainWindow.Text(hint, 12, "#62766A"); body.Children.Add(label);
        var input = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 16), MaxLength = 60 };
        body.Children.Add(input);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(MainWindow.ActionButton(Lang.T("取消"), () => DialogResult = false));
        row.Children.Add(MainWindow.ActionButton(Lang.T("好"), () => { Value = input.Text.Trim(); DialogResult = true; }, true));
        body.Children.Add(row);
        Content = body;
        Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Value = input.Text.Trim(); DialogResult = true; } };
    }
}

// 阶段编辑面板：设置里的"阶段方案"和论文资料里的"这篇论文的阶段"共用这一套界面。
// 每张卡片是一个阶段，按住左边的把手拖动排序；加、改名、删都在这儿。
public sealed class StageEditorDialog : Window
{
    private readonly bool schemeMode;
    private readonly Func<string, bool> nameTaken;
    private readonly StackPanel cardList = new();
    private readonly List<string> stages = new();
    private readonly TextBox schemeName = new();
    public string ResultSchemeName { get; private set; } = "";
    public List<string> ResultStages { get; private set; } = new();
    // 在论文里编辑时点了"另存为方案"，就带回一套新方案。
    public StageScheme? SavedScheme { get; private set; }

    public StageEditorDialog(string headline, string caption, string schemeNameValue, IReadOnlyList<string> stageNames, bool schemeMode, Func<string, bool>? nameTaken = null)
    {
        this.schemeMode = schemeMode;
        this.nameTaken = nameTaken ?? (_ => false);
        stages.AddRange(stageNames);
        ResultSchemeName = schemeNameValue;
        Title = headline;
        Width = Math.Min(560 * Appearance.DialogScale, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(720 * Appearance.DialogScale, SystemParameters.WorkArea.Height - 30);
        MinHeight = 420 * Appearance.DialogScale;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = true;
        SetResourceReference(BackgroundProperty, "WindowBackground"); SetResourceReference(ForegroundProperty, "Ink");
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll);
        var body = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; scroll.Content = body;
        var title = MainWindow.Text(headline, 22); body.Children.Add(title);
        var hint = MainWindow.Text(caption, 11, "#78867F"); hint.Margin = new Thickness(0, 0, 0, 16); body.Children.Add(hint);
        schemeName = new TextBox { Text = schemeNameValue, Margin = new Thickness(0, 5, 0, 16), MaxLength = 60, Visibility = schemeMode ? Visibility.Visible : Visibility.Collapsed };
        if (schemeMode)
        {
            var nameLabel = MainWindow.Text(Lang.T("方案名字"), 12, "#62766A"); body.Children.Add(nameLabel); body.Children.Add(schemeName);
        }
        var stageLabel = MainWindow.Text(Lang.T("阶段（按住左边的点拖动排序）"), 12, "#62766A"); body.Children.Add(stageLabel);
        cardList.Margin = new Thickness(0, 8, 0, 8); body.Children.Add(cardList);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(MainWindow.ActionButton(Lang.T("添加阶段"), () => { if (stages.Count >= Schemes.MaxStages) { MessageBox.Show(this, Storage.Why(SchemeProblem.TooManyStages)); return; } stages.Add(Lang.T("新阶段")); Build(); }));
        if (!schemeMode)
            row.Children.Add(MainWindow.ActionButton(Lang.T("另存为方案…"), () =>
            {
                string name = AskName(Lang.T("另存为方案"), Lang.T("给这套阶段起个名字，以后别的论文可以直接选它。"), "");
                if (name.Length == 0) return;
                if (!Validate(name, true, out string why)) { MessageBox.Show(this, why); return; }
                SavedScheme = new StageScheme(name, stages.ToList());
                MessageBox.Show(this, Lang.T("已经存成一个方案了，保存这篇论文之后就能在别的论文里选它。"));
            }));
        body.Children.Add(row);
        actions.Children.Add(MainWindow.ActionButton(Lang.T("取消"), () => DialogResult = false));
        actions.Children.Add(MainWindow.ActionButton(Lang.T("保存"), () =>
        {
            string name = schemeMode ? schemeName.Text.Trim() : ResultSchemeName;
            if (!Validate(name, schemeMode, out string why)) { MessageBox.Show(this, why); return; }
            ResultSchemeName = name; ResultStages = stages.ToList(); DialogResult = true;
        }, true));
        Build();
    }

    private string AskName(string headline, string hint, string initial)
    {
        var prompt = new TextPrompt(headline, hint, initial) { Owner = this };
        return prompt.ShowDialog() == true ? prompt.Value : "";
    }

    private bool Validate(string name, bool checkSchemeName, out string why)
    {
        why = "";
        if (checkSchemeName)
        {
            if (!Schemes.IsValidSchemeName(name)) { why = Storage.Why(SchemeProblem.NameTooWide); return false; }
            if (Schemes.IsBuiltIn(name)) { why = Lang.T("自建方案不能和内置方案同名。"); return false; }
            if (nameTaken(name)) { why = Lang.T("已经有同名的方案了。"); return false; }
        }
        var problem = Schemes.Inspect(checkSchemeName ? name : ResultSchemeName, stages);
        if (problem != SchemeProblem.None) { why = Storage.Why(problem); return false; }
        return true;
    }

    private void Build()
    {
        cardList.Children.Clear();
        for (int i = 0; i < stages.Count; i++) cardList.Children.Add(Card(i));
    }

    private FrameworkElement Card(int index)
    {
        int slot = index;
        var handle = MainWindow.Text("⋮⋮", 13, "#78867F");
        handle.Cursor = Cursors.SizeAll; handle.VerticalAlignment = VerticalAlignment.Center; handle.Margin = new Thickness(0, 0, 10, 0);
        handle.ToolTip = Lang.T("按住这里拖动排序");
        AutomationProperties.SetName(handle, Lang.T("拖动排序"));
        var name = new TextBox { Text = stages[slot], VerticalContentAlignment = VerticalAlignment.Center, MinWidth = 220 * Appearance.DialogScale };
        name.TextChanged += (_, _) => { if (slot < stages.Count) stages[slot] = name.Text; };
        var remove = MainWindow.ActionButton("✕", () =>
        {
            if (stages.Count <= Schemes.MinStages) { MessageBox.Show(this, Storage.Why(SchemeProblem.TooFewStages)); return; }
            stages.RemoveAt(slot); Build();
        });
        remove.ToolTip = Lang.T("删掉这个阶段");
        var dock = new DockPanel();
        DockPanel.SetDock(handle, Dock.Left); dock.Children.Add(handle);
        DockPanel.SetDock(remove, Dock.Right); dock.Children.Add(remove);
        dock.Children.Add(name);
        var card = new Border
        {
            Child = dock, Margin = new Thickness(0, 0, 0, 6),
            Background = Appearance.Paint(Appearance.Current.Card, Appearance.Opacity),
            BorderBrush = Appearance.Paint(Appearance.Current.Border, .9), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7)
        };
        bool dragging = false, moved = false;
        Point start = default;
        int target = slot;
        handle.MouseLeftButtonDown += (_, e) => { dragging = true; moved = false; target = slot; start = e.GetPosition(cardList); handle.CaptureMouse(); e.Handled = true; };
        handle.MouseMove += (_, e) =>
        {
            if (!dragging || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(cardList);
            if (Math.Abs(point.X - start.X) < 3 && Math.Abs(point.Y - start.Y) < 3) return;
            moved = true; target = IndexAt(point.Y);
            for (int i = 0; i < cardList.Children.Count; i++)
                if (cardList.Children[i] is Border other)
                {
                    bool hit = i == target;
                    other.BorderBrush = Appearance.Paint(hit ? Appearance.Current.Accent : Appearance.Current.Border, .9);
                    other.BorderThickness = new Thickness(hit ? 2 : 1);
                }
        };
        handle.MouseLeftButtonUp += (_, _) =>
        {
            handle.ReleaseMouseCapture();
            if (!dragging) return;
            dragging = false;
            if (!moved) return;
            var reordered = Schemes.Move(stages, slot, target);
            stages.Clear(); stages.AddRange(reordered);
            Build();
        };
        return card;
    }

    private int IndexAt(double y)
    {
        for (int i = 0; i < cardList.Children.Count; i++)
        {
            if (cardList.Children[i] is not FrameworkElement child) continue;
            double top = child.TranslatePoint(new Point(0, 0), cardList).Y;
            if (y < top + child.ActualHeight / 2) return i;
        }
        return Math.Max(0, cardList.Children.Count - 1);
    }
}
