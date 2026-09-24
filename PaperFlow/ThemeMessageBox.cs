using System;
using System.Windows;
using System.Windows.Controls;

namespace PaperFlow;

// App messages share the same resources as editing windows, including live previews.
// Keep MessageBoxResult semantics so cancellation cannot turn into confirmation.
public sealed class ThemeMessageBox : Window
{
    public MessageBoxResult Result { get; private set; }

    public ThemeMessageBox(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        Title = title;
        Width = Math.Min(500 * Appearance.DialogScale, SystemParameters.WorkArea.Width - 40);
        MaxHeight = Math.Max(180, SystemParameters.WorkArea.Height - 40);
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "WindowBackground");
        SetResourceReference(ForegroundProperty, "Ink");
        FontFamily = new System.Windows.Media.FontFamily(Appearance.FamilyFor("body"));
        FontSize = 13 * Appearance.DialogScale;
        Result = buttons == MessageBoxButton.OK ? MessageBoxResult.OK
            : buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var body = new DockPanel();
        if (image != MessageBoxImage.None)
        {
            var symbol = new TextBlock { Text = image == MessageBoxImage.Question ? "?" : image == MessageBoxImage.Information ? "ⓘ" : "!", FontSize = 26, Margin = new Thickness(0, 0, 16, 0) };
            symbol.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); body.Children.Add(symbol);
        }
        body.Children.Add(text);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(80, MaxHeight - 150) });
        Button? defaultButton = null;
        void Add(string label, MessageBoxResult answer, bool primary = false)
        {
            var button = MainWindow.ActionButton(Lang.T(label), () => { Result = answer; DialogResult = true; }, primary, Appearance.DialogScale);
            button.IsDefault = answer == Result;
            button.IsCancel = answer == Result;
            if (button.IsDefault) defaultButton = button;
            actions.Children.Add(button);
        }
        if (buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel) { Add("是", MessageBoxResult.Yes, true); Add("否", MessageBoxResult.No); }
        else Add("好", MessageBoxResult.OK, true);
        if (buttons is MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel) Add("取消", MessageBoxResult.Cancel);
        Loaded += (_, _) => defaultButton?.Focus();
    }

    public static MessageBoxResult Show(Window? owner, string message, string? title = null, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new ThemeMessageBox(message, title ?? Product.Name, buttons, image);
        if (owner?.IsVisible == true) dialog.Owner = owner;
        else { dialog.ShowInTaskbar = true; dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen; }
        dialog.ShowDialog();
        return dialog.Result;
    }
}
