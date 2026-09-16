using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace PaperFlow;
public sealed class NewPaperDialog : Window
{
    public string PaperTitle { get; private set; } = "";
    public NewPaperDialog()
    {
        Title = Lang.T("新增论文"); Width = 420 * Appearance.DialogScale; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = true;
        var body = new StackPanel { Margin = new Thickness(24) }; Content = body;
        body.Children.Add(MainWindow.Text(Lang.T("写下论文标题"), 20));
        body.Children.Add(MainWindow.Text(Lang.T("自动附带七个阶段，资料可以稍后补充。"), 11, "#78867F"));
        var input = new TextBox { MaxLength = 500, Margin = new Thickness(0, 16, 0, 18) }; AutomationProperties.SetName(input, Lang.T("论文标题")); body.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = MainWindow.ActionButton(Lang.T("取消"), () => DialogResult = false); cancel.IsCancel = true; buttons.Children.Add(cancel);
        var add = MainWindow.ActionButton(Lang.T("新增"), () => { if (string.IsNullOrWhiteSpace(input.Text)) { input.Focus(); return; } PaperTitle = input.Text.Trim(); DialogResult = true; }, true); add.IsDefault = true; buttons.Children.Add(add); body.Children.Add(buttons);
        Loaded += (_, _) => input.Focus();
    }
}
