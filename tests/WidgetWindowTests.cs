using PaperFlow;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Explicit, interactive Windows checks. All windows and rendered images use synthetic
// papers in a new temporary data root. Never pass a personal library to this runner.
internal static class WidgetWindowTests
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--paper-details-only")) { Console.Error.WriteLine("Supply an output directory for synthetic screenshots, optionally followed by --paper-details-only."); return 2; }
        var app = new CheckApp { Output = Path.GetFullPath(args[0]), PaperDetailsOnly = args.Length == 2 };
        var source = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PaperFlow", "App.xaml"));
        System.Xml.Linq.XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = new System.Xml.Linq.XElement(wpf + "ResourceDictionary",
            new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
            source.Root!.Element(wpf + "Application.Resources")!.Elements());
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
        return app.Run();
    }

    private sealed class CheckApp : Application
    {
        public string Output = "";
        public bool PaperDetailsOnly;
        private int checks;
        private readonly List<string> observations = new();
        private void Check(bool ok, string label)
        {
            observations.Add((ok ? "PASS " : "FAIL ") + label);
            if (!ok) throw new InvalidOperationException(label);
            checks++;
        }
        protected override async void OnStartup(StartupEventArgs e)
        {
            // Intentionally do not call PaperFlow.App.OnStartup: it opens normal user data.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Directory.CreateDirectory(Output);
            MainWindow? widget = null;
            try
            {
                Product.Demo = true;
                Lang.Apply("zh");
                var root = Path.Combine(Path.GetTempPath(), "PaperFlow-window-tests-" + Guid.NewGuid().ToString("N"));
                var library = new Library();
                library.Settings.WindowMode = "window"; library.Settings.UpdateMode = "never";
                library.Settings.Width = 380; library.Settings.Height = 220;
                library.Settings.Left = 50; library.Settings.Top = 80;
                library.Settings.Language = "zh";
                library.Papers = Enumerable.Range(1, 6).Select(i => new Paper { Title = "Synthetic paper " + i, Priority = i < 4 ? "高" : "中" }).ToList();
                var storage = new Storage(root); storage.Save(library);
                var sync = new SyncEngine(root, Path.Combine(root, "sync"), library);
                Appearance.Apply(library.Settings);
                widget = new MainWindow(storage, library, sync);
                widget.Show(); widget.Activate();
                await Task.Delay(700);
                var cards = Field<StackPanel>(widget, "cards");
                if (PaperDetailsOnly)
                {
                    Descendants<CheckBox>(cards).First().IsChecked = true;
                    CheckPaperDetails(widget, storage, cards);
                    Finish(widget); return;
                }
                var scroll = Field<ScrollViewer>(widget, "scroller");
                widget.Height = widget.MinHeight;
                await Task.Delay(350);
                Check(cards.Children.Count == 6, "small window keeps the whole unpaged list");
                Check(scroll.ScrollableHeight > 0, "one-card window scrolls to the remaining papers");
                var first = (FrameworkElement)cards.Children[0];
                Check(scroll.ViewportHeight + 1 >= first.ActualHeight, "minimum height contains a complete paper card");
                Check(scroll.ViewportHeight < first.ActualHeight * 2, "minimum height does not require two paper cards");
                Snapshot(widget, "01-small-light");
                scroll.ScrollToBottom(); await Task.Delay(150);
                Check(scroll.VerticalOffset > 0, "scrolling reaches the rest without changing pages");
                Snapshot(widget, "02-small-scrolled");
                scroll.ScrollToTop();

                // Preview the same preferences path used by the Settings dialog.
                foreach (var scene in new[] { ("夜航 · 霜蓝", Themes.CardLayout, "03-small-dark"), ("极简 · 白", Themes.ListLayout, "04-small-list") })
                {
                    var prefs = Storage.CloneLibrary(library).Settings;
                    prefs.Theme = scene.Item1; prefs.ListLayout = scene.Item2;
                    Invoke(widget, "PreviewAppearance", prefs);
                    await Task.Delay(250); widget.Height = widget.MinHeight; await Task.Delay(150);
                    Check(scroll.ViewportHeight + 1 >= ((FrameworkElement)cards.Children[0]).ActualHeight, "complete card after switching " + scene.Item3);
                    Snapshot(widget, scene.Item3);
                }
                var settings = new SettingsWindow(Field<Library>(widget, "library").Settings, root, () => { }, () => { }, p => Invoke(widget, "PreviewAppearance", p), () => 1) { Owner = widget };
                settings.Show(); await Task.Delay(200);
                Descendants<ListBox>(settings).First(b => b.Items.Count == 6).SelectedIndex = 2;
                await Task.Delay(150); Snapshot(settings, "07-settings-light");
                var modePicker = Descendants<ComboBox>(settings).First(b => b.Items.OfType<Choice>().Any(c => c.Value == "desktop"));
                modePicker.SelectedIndex = 2; await Task.Delay(100);
                Check(widget.Topmost, "window mode selection previews actual topmost state");
                modePicker.SelectedIndex = 1;
                settings.Result.Theme = "夜航 · 霜蓝";
                Invoke(widget, "PreviewAppearance", settings.Result); await Task.Delay(200);
                Snapshot(settings, "08-settings-dark");
                Check(((SolidColorBrush)settings.Background).Color == (Color)ColorConverter.ConvertFromString(Appearance.Current.Window), "open Settings follows the changed dark theme");
                var selectedText = Descendants<TextBlock>(modePicker).First(t => t.Text == "普通窗口");
                Check(((SolidColorBrush)selectedText.Foreground).Color == (Color)ColorConverter.ConvertFromString(Appearance.Current.Ink), "selected dropdown text stays legible after a dark theme switch");
                settings.Close();
                var restored = Storage.CloneLibrary(library).Settings; restored.ListLayout = Themes.ListLayout;
                Invoke(widget, "PreviewAppearance", restored); await Task.Delay(200);
                var current = Field<Library>(widget, "library");
                current.Settings.PageMode = ViewRules.PageModes[1]; current.Settings.PageIndex = 0;
                Invoke(widget, "Render"); await Task.Delay(250); widget.Height = widget.MinHeight; await Task.Delay(150);
                Check(cards.Children.Count == 3 && ViewRules.PageCount(current.Settings) == 3, "priority paging is unchanged in the small window");
                Snapshot(widget, "05-priority");
                current.Settings.WindowMode = "desktop"; Invoke(widget, "Render");
                await Task.Delay(300);
                var handle = new WindowInteropHelper(widget).Handle;
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
                try
                {
                    // Exercise the actual Windows Show Desktop command, not a simulated
                    // minimize message; restore other windows even if an assertion fails.
                    shell.ToggleDesktop(); await Task.Delay(1000);
                    var desktop = GetForegroundWindow();
                    var className = new System.Text.StringBuilder(256); GetClassName(desktop, className, 256);
                    observations.Add("Show Desktop foreground class: " + className);
                    Check(className.ToString() is "Progman" or "WorkerW", "Windows actually entered Show Desktop");
                    Check(IsWindowVisible(handle) && !IsIconic(handle), "widget remains visible after Show Desktop");
                    var placement = Field<object>(widget, "desktopPlacement");
                    observations.Add("Placement mode=" + Field<string>(placement, "mode") + "; failed=" + Field<bool>(placement, "failed") + "; desktopStyle=" + GetWindowLong(desktop, -20).ToString("X") + "; widgetStyle=" + GetWindowLong(handle, -20).ToString("X"));
                    Check(!Behind(handle, desktop), "widget is above the raised desktop");
                    Check(!widget.Topmost, "desktop placement does not become always-on-top");
                    Snapshot(widget, "06-show-desktop");
                    shell.ToggleDesktop(); await Task.Delay(400);
                    shell.ToggleDesktop(); await Task.Delay(700);
                    var secondClass = new System.Text.StringBuilder(256); GetClassName(GetForegroundWindow(), secondClass, 256);
                    observations.Add("Second Show Desktop foreground class: " + secondClass);
                    Check(secondClass.ToString() is "Progman" or "WorkerW", "Windows re-entered Show Desktop");
                    Check(!IsIconic(handle) && !Behind(handle, GetForegroundWindow()), "a second Show Desktop preserves the widget");
                }
                finally { shell.ToggleDesktop(); shell.UndoMinimizeALL(); Marshal.FinalReleaseComObject(shell); }
                await Task.Delay(500);
                var ordinary = new Window { Title = "Synthetic foreground app", Width = 300, Height = 220, Left = widget.Left, Top = widget.Top, Content = "Synthetic foreground window" };
                ordinary.Show(); ordinary.Activate(); await Task.Delay(350);
                Check((GetWindowLong(handle, -20) & 8) == 0, "opening an ordinary window removes temporary desktop topmost state");
                Check(Behind(handle, new WindowInteropHelper(ordinary).Handle), "ordinary windows cover the desktop widget");
                ordinary.Close();
                Descendants<CheckBox>(cards).First().IsChecked = true; await Task.Delay(150);
                Check(storage.Load().Papers[0].Stages[0].Done, "direct stage interaction saves from the small desktop widget");
                CheckPaperDetails(widget, storage, cards);
                current = Field<Library>(widget, "library");
                current.Settings.WindowMode = "topmost"; Invoke(widget, "Render");
                Check(widget.Topmost, "explicit pinning still works");
                current.Settings.WindowMode = "desktop"; Invoke(widget, "Render");
                Check(!widget.Topmost, "unpin returns to desktop placement");
                widget.Hide(); await Task.Delay(350);
                Check(!IsWindowVisible(handle), "explicit hide is never undone by desktop placement");
                Invoke(widget, "SaveWindow");
                var saved = storage.Load();
                Check(saved.Settings.Width == widget.Width && saved.Settings.Height < 400, "small window geometry survives save and reload");
                Finish(widget);
            }
            catch (Exception ex)
            {
                observations.Add(ex.ToString());
                File.WriteAllLines(Path.Combine(Output, "checks.txt"), observations);
                Console.Error.WriteLine(ex);
                if (widget != null) { FieldInfo? quitting = typeof(MainWindow).GetField("quitting", BindingFlags.NonPublic | BindingFlags.Instance); quitting?.SetValue(widget, true); widget.Close(); }
                Shutdown(1);
            }
        }
        private void Finish(MainWindow widget)
        {
            Console.WriteLine("PASS: " + checks + " window checks.");
            File.WriteAllLines(Path.Combine(Output, "checks.txt"), observations);
            Invoke(widget, "ExitApplication"); Shutdown(0);
        }

        private void CheckPaperDetails(MainWindow widget, Storage storage, StackPanel cards)
        {
            var current = Field<Library>(widget, "library");
            var paperId = current.Papers[0].Id;
            current.Settings.CustomSchemes.Add(new StageScheme("Synthetic scheme", current.Papers[0].Stages.Select(s => s.Name).Append("Synthetic step").ToList()));
            foreach (string action in new[] { "save", "cancel", "english", "uncheck" })
            {
                Exception? failure = null;
                var original = Field<Library>(widget, "library").Papers.Single(p => p.Id == paperId);
                var before = Storage.Clone(original);
                if (action == "english") Lang.Apply("en");
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    PaperEditor? editor = null;
                    try
                    {
                        editor = Windows.OfType<PaperEditor>().Single();
                        await Task.Delay(150);
                        var done = Field<System.Windows.Controls.WrapPanel>(editor, "doneRow").Children.OfType<CheckBox>().ToArray();
                        var skipped = Field<System.Windows.Controls.WrapPanel>(editor, "skipRow").Children.OfType<CheckBox>().ToArray();
                        Check(done[0].IsChecked == original.Stages[0].Done, "paper details read the widget stage state: " + action);
                        if (action == "save")
                        {
                            done[1].IsChecked = true;
                            Check(!original.Stages[1].Done && !storage.Load().Papers.Single(p => p.Id == paperId).Stages[1].Done, "editing stages does not save before confirmation");
                            skipped[1].IsChecked = true;
                            Check(!editor.Result.Stages[1].Done && done[1].IsChecked == false && !done[1].IsEnabled, "not-applicable clears and disables completion");
                            skipped[1].IsChecked = false;
                            Check(done[1].IsEnabled && done[1].IsChecked == false, "restoring applicability allows an explicit completion choice");
                            done[1].IsChecked = true; skipped[2].IsChecked = true;
                            var picker = Descendants<ComboBox>(editor).First(b => b.Items.OfType<Choice>().Any(c => c.Value == "Synthetic scheme"));
                            picker.SelectedItem = picker.Items.OfType<Choice>().Single(c => c.Value == "Synthetic scheme");
                            Check(editor.Result.Stages.Count == 8 && editor.Result.Stages[1].Done && editor.Result.Stages[2].Skipped, "changing schemes keeps unsaved completion and applicability by stage name");
                            await Task.Delay(150); Snapshot(editor, "09-paper-details-light");
                            var dark = Storage.CloneLibrary(current).Settings; dark.Theme = "夜航 · 霜蓝";
                            Appearance.Apply(dark); await Task.Delay(100); Snapshot(editor, "10-paper-details-dark");
                            Check(((SolidColorBrush)editor.Background).Color == (Color)ColorConverter.ConvertFromString(Appearance.Current.Window), "paper details background follows the dark theme");
                            var headline = Descendants<TextBlock>(editor).Single(t => t.Text == Lang.T("让下一步更清楚"));
                            Check(((SolidColorBrush)headline.Foreground).Color == (Color)ColorConverter.ConvertFromString(Appearance.Current.Ink), "paper details heading follows the dark theme");
                            var activeCheck = Field<System.Windows.Controls.WrapPanel>(editor, "doneRow").Children.OfType<CheckBox>().First();
                            Check(((SolidColorBrush)activeCheck.Foreground).Color == (Color)ColorConverter.ConvertFromString(Appearance.Current.Ink), "paper detail stage text follows the dark theme");
                            Descendants<Button>(editor).Single(b => Equals(b.Content, Lang.T("保存资料"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        else if (action == "uncheck")
                        {
                            done[0].IsChecked = false;
                            Descendants<Button>(editor).Single(b => Equals(b.Content, Lang.T("保存资料"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        else
                        {
                            done[0].IsChecked = false; skipped[1].IsChecked = true;
                            if (action == "english")
                            {
                                Check(Equals(done[0].Content, "Proposal"), "paper detail stage labels follow the English interface");
                                Snapshot(editor, "11-paper-details-english");
                            }
                            Descendants<Button>(editor).Single(b => Equals(b.Content, Lang.T("取消"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                    }
                    catch (Exception ex) { failure = ex; if (editor != null) editor.DialogResult = false; }
                }));
                Invoke(widget, "EditPaper", original);
                Lang.Apply("zh"); Appearance.Apply(Field<Library>(widget, "library").Settings);
                if (failure != null) throw failure;
                var saved = storage.Load().Papers.Single(p => p.Id == paperId);
                if (action == "save")
                {
                    Check(saved.Stages[1].Done && saved.Stages[2].Skipped && saved.Stages.Count == 8, "paper detail stage choices persist through the real save path");
                    Check(Descendants<CheckBox>(cards).Skip(1).First().IsChecked == true, "saving paper details refreshes the widget stage checks");
                    Check(saved.History.Any(h => h.Description == "完成 · " + Schemes.Display(saved.Stages[1].Name)), "details use the same completion history as widget edits");
                }
                else if (action == "uncheck") Check(!saved.Stages[0].Done && Descendants<CheckBox>(cards).First().IsChecked == false, "unchecking in details saves and clears the widget check");
                else Check(saved.Stages[0].Done == before.Stages[0].Done && saved.Stages[1].Skipped == before.Stages[1].Skipped && saved.History.Count == before.History.Count, "cancel discards stage edits and history: " + action);
            }
        }

        private void Snapshot(Window window, string name)
        {
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(Output, name + ".png")); png.Save(output);
        }
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.Invoke(target, args);
    private static bool Behind(IntPtr own, IntPtr other) { int left = 2048; for (var w = GetWindow(own, 3); w != IntPtr.Zero && left-- > 0; w = GetWindow(w, 3)) if (w == other) return true; return false; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
}
