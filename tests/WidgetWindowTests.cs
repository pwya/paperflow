using PaperFlow;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Explicit, interactive Windows checks. All windows and rendered images use synthetic
// papers in a new temporary data root. Never pass a personal library to this runner.
internal static class WidgetWindowTests
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] is not ("--paper-details-only" or "--minimal-only" or "--paper-management-only" or "--updates-only" or "--themes-order-only"))) { Console.Error.WriteLine("Supply an output directory for synthetic screenshots, optionally followed by --paper-details-only, --minimal-only, --paper-management-only, --updates-only or --themes-order-only."); return 2; }
        var app = new CheckApp { Output = Path.GetFullPath(args[0]), PaperDetailsOnly = args.Contains("--paper-details-only"), MinimalOnly = args.Contains("--minimal-only"), ManagementOnly = args.Contains("--paper-management-only"), UpdatesOnly = args.Contains("--updates-only"), ThemesOrderOnly = args.Contains("--themes-order-only") };
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
        public bool MinimalOnly;
        public bool ManagementOnly;
        public bool UpdatesOnly;
        public bool ThemesOrderOnly;
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
                Product.Portable = true;
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
                if (ThemesOrderOnly) { await CheckThemesAndOrder(widget, storage, cards); Finish(widget); return; }
                if (UpdatesOnly) { await CheckManualUpdates(widget, storage); Finish(widget); return; }
                if (ManagementOnly) { await CheckManagement(widget, storage, cards); Finish(widget); return; }
                if (MinimalOnly) { await CheckMinimal(widget, storage, cards); Finish(widget); return; }
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
                current.Settings.DisplayMode = "minimal";
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
                ordinary.Show(); bool activated = ordinary.Activate();
                for (int attempt = 0; attempt < 20 && (GetWindowLong(handle, -20) & 8) != 0; attempt++) await Task.Delay(100);
                observations.Add("Ordinary test window activated=" + activated + "; foreground is test window=" + (GetForegroundWindow() == new WindowInteropHelper(ordinary).Handle));
                Check(activated && GetForegroundWindow() == new WindowInteropHelper(ordinary).Handle, "interactive desktop must allow the ordinary test window to gain focus");
                Check((GetWindowLong(handle, -20) & 8) == 0, "opening an ordinary window removes temporary desktop topmost state");
                Check(Behind(handle, new WindowInteropHelper(ordinary).Handle), "ordinary windows cover the desktop widget");
                ordinary.Close();
                current.Settings.DisplayMode = "full"; Invoke(widget, "Render"); await Task.Delay(150);
                Descendants<CheckBox>(cards).First().IsChecked = true; await Task.Delay(150);
                Check(storage.Load().Papers[0].Stages[0].Done, "direct stage interaction saves from the small desktop widget");
                CheckPaperDetails(widget, storage, cards);
                await CheckMinimal(widget, storage, cards);
                await CheckManagement(widget, storage, cards);
                await CheckManualUpdates(widget, storage);
                await CheckThemesAndOrder(widget, storage, cards);
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
        private async Task CheckManualUpdates(MainWindow widget, Storage storage)
        {
            var baseline = Storage.CloneLibrary(Field<Library>(widget, "library"));
            var previousUrl = Updates.SingleManifestUrl;
            try
            {
                foreach (var mode in new[] { "full", "minimal" })
                foreach (var action in new[] { "save", "cancel", "close", "late", "failure" })
                {
                    var prefs = Field<Library>(widget, "library").Settings;
                    prefs.DisplayMode = mode; prefs.UpdateMode = "never";
                    Invoke(widget, "Render");
                    Field<Border>(widget, "updateBar").Visibility = Visibility.Collapsed;
                    using var listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    Updates.SingleManifestUrl = $"http://127.0.0.1:{port}/update.json";
                    var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var respond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var server = Task.Run(async () =>
                    {
                        using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
                        using var stream = client.GetStream();
                        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                        var request = await reader.ReadLineAsync();
                        if (request != "GET /update.json HTTP/1.1") throw new InvalidOperationException("Unexpected update request: " + request);
                        while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                        requested.SetResult();
                        await respond.Task.WaitAsync(TimeSpan.FromSeconds(10));
                        string json = action == "failure" ? "invalid manifest" : JsonSerializer.Serialize(new
                        {
                            version = "99.0.0", url = $"http://127.0.0.1:{port}/synthetic.zip",
                            sha256 = new string('a', 64), length = 123, exeSha256 = new string('b', 64), exeLength = 456
                        });
                        byte[] body = Encoding.UTF8.GetBytes(json);
                        byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(header); await stream.WriteAsync(body);
                    });
                    _ = Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        SettingsWindow? dialog = null;
                        try
                        {
                            dialog = Windows.OfType<SettingsWindow>().Single();
                            Descendants<ListBox>(dialog).First(b => b.Items.Count == 6).SelectedIndex = 4;
                            dialog.UpdateLayout();
                            dialog.Result.UpdateMode = "always";
                            var checkNow = Descendants<Button>(dialog).Single(b => Equals(b.Content, Lang.T("现在检查一次")));
                            checkNow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
                            if (action == "late") dialog.Close();
                            respond.SetResult();
                            var deadline = DateTime.UtcNow.AddSeconds(10);
                            while (!checkNow.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(20);
                            Check(checkNow.IsEnabled, "manual check finishes: " + mode + "/" + action);
                            if (action != "late")
                            {
                                if (action == "close") dialog.Close();
                                else Descendants<Button>(dialog).Single(b => Equals(b.Content, Lang.T(action == "save" ? "保存设置" : "取消"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            }
                            completed.SetResult();
                        }
                        catch (Exception ex)
                        {
                            respond.TrySetResult();
                            if (dialog?.IsVisible == true) dialog.Close();
                            completed.SetException(ex);
                        }
                    }));
                    Invoke(widget, "OpenSettings");
                    await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    await server;
                    widget.UpdateLayout();
                    var bar = Field<Border>(widget, "updateBar");
                    var button = Field<Button>(widget, "updateAction");
                    Check(bar.IsVisible && button.IsVisible && button.IsEnabled, "update action visible after " + mode + "/" + action);
                    Check(Equals(button.Content, Lang.T(action == "failure" ? "现在再试一次" : "下载并安装")), "correct update action after " + mode + "/" + action);
                    Check(storage.Load().Settings.UpdateMode == (action == "save" ? "always" : "never"), "update result does not save cancelled preferences: " + mode + "/" + action);
                    Check(!listener.Pending() && !Field<bool>(widget, "updating"), "checking never starts a download: " + mode + "/" + action);
                    if (action == "close") Snapshot(widget, "update-after-close-" + mode);
                }
                Check(SyncProtocol.Diff(baseline, Field<Library>(widget, "library")).Count == 0, "manual update checks leave paper data untouched");
            }
            finally { Updates.SingleManifestUrl = previousUrl; }
        }

        private async Task CheckMinimal(MainWindow widget, Storage storage, StackPanel cards)
        {
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            widget.UpdateLayout();
            var baseline = Storage.CloneLibrary(Field<Library>(widget, "library"));
            var fullHeight = ((FrameworkElement)cards.Children[0]).ActualHeight;
            foreach (bool save in new[] { false, true })
            {
                Exception? failure = null;
                _ = Dispatcher.BeginInvoke(new Action(async () =>
                {
                    SettingsWindow? dialog = null;
                    try
                    {
                        dialog = Windows.OfType<SettingsWindow>().Single();
                        var picker = Descendants<ComboBox>(dialog).First(b => b.Items.OfType<Choice>().Any(c => c.Value == "minimal"));
                        picker.SelectedIndex = 1; await Task.Delay(150);
                        Check(!Descendants<CheckBox>(cards).Any() && Field<Button>(widget, "minimalMenu").IsVisible, "settings preview applies minimal mode: " + save);
                        Snapshot(dialog, "12-minimal-settings");
                        Descendants<Button>(dialog).Single(b => Equals(b.Content, Lang.T(save ? "保存设置" : "取消"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception ex) { failure = ex; if (dialog != null) dialog.DialogResult = false; }
                }));
                Invoke(widget, "OpenSettings");
                if (failure != null) throw failure;
                Check(storage.Load().Settings.DisplayMode == (save ? "minimal" : "full"), "minimal preference respects save or cancel: " + save);
            }
            Check(SyncProtocol.Diff(baseline, Field<Library>(widget, "library")).Count == 0, "changing display mode leaves paper data and shared events untouched");
            Check(!Descendants<CheckBox>(cards).Any() && !Descendants<Button>(cards).Any(), "minimal rows contain no stage checkboxes or settings buttons");
            Check(((FrameworkElement)cards.Children[0]).ActualHeight < fullHeight, "minimal paper rows take less height than full cards");
            var scroll = Field<ScrollViewer>(widget, "scroller");
            widget.Height = widget.MinHeight; await Task.Delay(200);
            var row = (FrameworkElement)cards.Children[0];
            Check(scroll.ViewportHeight + 1 >= row.ActualHeight && scroll.ViewportHeight < row.ActualHeight * 2, "minimal window shrinks to one whole paper row");
            scroll.ScrollToBottom(); await Task.Delay(100);
            Check(scroll.VerticalOffset > 0, "minimal view scrolls to all remaining papers");
            scroll.ScrollToTop(); Snapshot(widget, "13-minimal-small");
            foreach (var scene in new[] { ("夜航 · 霜蓝", Themes.CardLayout, "14-minimal-dark"), ("极简 · 白", Themes.ListLayout, "15-minimal-list") })
            {
                var prefs = Storage.CloneLibrary(Field<Library>(widget, "library")).Settings;
                prefs.Theme = scene.Item1; prefs.ListLayout = scene.Item2;
                Invoke(widget, "PreviewAppearance", prefs); await Task.Delay(150);
                Check(!Descendants<CheckBox>(cards).Any(), "theme changes preserve minimal mode: " + scene.Item3);
                Snapshot(widget, scene.Item3);
            }
            var current = Field<Library>(widget, "library");
            current.Settings.PageMode = ViewRules.PageModes[1]; current.Settings.PageIndex = 0;
            Invoke(widget, "Render"); await Task.Delay(150);
            Check(cards.Children.Count == 3 && Field<StackPanel>(widget, "pager").IsVisible, "minimal view retains priority paging");
            Exception? editFailure = null;
            var target = current.Papers.First(p => p.Priority == "高");
            bool nextDone = !target.Stages[1].Done;
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                PaperEditor? editor = null;
                try
                {
                    editor = Windows.OfType<PaperEditor>().Single();
                    Field<System.Windows.Controls.WrapPanel>(editor, "doneRow").Children.OfType<CheckBox>().Skip(1).First().IsChecked = nextDone;
                    Descendants<Button>(editor).Single(b => Equals(b.Content, Lang.T("保存资料"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { editFailure = ex; if (editor != null) editor.DialogResult = false; }
            }));
            ((UIElement)cards.Children[0]).RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(widget), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            if (editFailure != null) throw editFailure;
            var saved = storage.Load();
            Check(saved.Papers.Single(p => p.Id == target.Id).Stages[1].Done == nextDone && saved.Settings.DisplayMode == "minimal", "minimal row opens details and saves stage changes without leaving minimal mode");
            Check(Descendants<TextBlock>(cards).Any(t => t.Text == saved.Papers.Single(p => p.Id == target.Id).Progress + "%"), "minimal percentage refreshes after editing paper details");
            Invoke(widget, "SaveWindow");
            Check(storage.Load().Settings.Height == widget.Height, "minimal small geometry survives saving");
            Field<Button>(widget, "minimalMenu").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = Field<Button>(widget, "minimalMenu").ContextMenu;
            var restore = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, Lang.T("切回完整模式")));
            menu.IsOpen = false; restore.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(150);
            Check(storage.Load().Settings.DisplayMode == "full" && Descendants<CheckBox>(cards).Any(), "minimal menu restores full mode and stage controls");
        }

        private async Task CheckManagement(MainWindow widget, Storage storage, StackPanel cards)
        {
            var papers = Field<Library>(widget, "library").Papers;
            // Use six fresh synthetic papers for a standalone or combined run.
            Invoke(widget, "Commit", new Action<Library>(l =>
            {
                l.Papers = Enumerable.Range(1, 6).Select(i => new Paper { Title = "Synthetic management " + i }).ToList();
                l.Settings.DisplayMode = "full"; l.Settings.PageMode = ViewRules.PageModes[0]; l.Settings.VisiblePriorities = Paper.Priorities.ToList();
            }), "Synthetic setup");
            Field<ComboBox>(widget, "filter").SelectedIndex = 0;
            Field<TextBox>(widget, "search").Clear(); await Task.Delay(150);
            papers = Field<Library>(widget, "library").Papers;
            Exception? confirmationFailure = null;
            void ScheduleConfirmation(bool accept)
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    DeletePaperDialog? confirmation = null;
                    try
                    {
                        confirmation = Windows.OfType<DeletePaperDialog>().Single();
                        Check(Descendants<TextBlock>(confirmation).Any(t => t.Text == Lang.T("删除后不可恢复，确定执行删除吗？")), "deletion uses the requested warning");
                        Check(Descendants<Button>(confirmation).Single(b => b.IsDefault).Content.Equals(Lang.T("取消")), "deletion defaults to cancel");
                        Check(((SolidColorBrush)Descendants<TextBlock>(confirmation).First().Foreground).Color == (Color)ColorConverter.ConvertFromString(Appearance.Current.Ink), "confirmation text follows the current theme");
                        Snapshot(confirmation, "17-delete-confirmation");
                        Click(confirmation, accept ? "删除论文" : "取消");
                    }
                    catch (Exception ex) { confirmationFailure = ex; if (confirmation != null) confirmation.DialogResult = false; }
                }));
            }
            var firstId = papers[0].Id;
            foreach (bool accept in new[] { false, true })
            {
                var gear = Descendants<Button>(cards).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == Lang.F("{0} 的论文设置", papers[0].Title));
                gear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var menu = gear.ContextMenu;
                Check(menu.Items.OfType<MenuItem>().Any(i => Equals(i.Header, Lang.T("查看全部归档论文"))), "paper settings exposes the archive window");
                var delete = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, Lang.T("删除论文")));
                menu.IsOpen = false; ScheduleConfirmation(accept); delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (confirmationFailure != null) throw confirmationFailure;
                Check(storage.Load().Papers.Any(p => p.Id == firstId) != accept, "paper menu respects deletion confirmation: " + accept);
                await Task.Delay(100);
            }
            foreach (bool accept in new[] { false, true })
            {
                var paper = Field<Library>(widget, "library").Papers[0]; Exception? failure = null;
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    PaperEditor? editor = null;
                    try
                    {
                        editor = Windows.OfType<PaperEditor>().Single();
                        Field<WrapPanel>(editor, "doneRow").Children.OfType<CheckBox>().First().IsChecked = true;
                        ScheduleConfirmation(accept); Click(editor, "删除论文");
                        if (!accept)
                        {
                            Check(editor.IsVisible && !storage.Load().Papers.Single(p => p.Id == paper.Id).Stages[0].Done, "cancelled deletion keeps the draft open and unsaved");
                            Click(editor, "取消");
                        }
                    }
                    catch (Exception ex) { failure = ex; if (editor != null && editor.IsVisible) editor.DialogResult = false; }
                }));
                Invoke(widget, "EditPaper", paper);
                if (failure != null) throw failure;
                if (confirmationFailure != null) throw confirmationFailure;
                Check(storage.Load().Papers.Any(p => p.Id == paper.Id) != accept, "paper editor deletes only after confirmation: " + accept);
            }
            Invoke(widget, "Commit", new Action<Library>(l =>
            {
                foreach (var p in l.Papers.Take(3)) { p.Archived = true; p.Tags.Add("藏"); }
                l.Settings.TagHidingEnabled = true; l.Settings.HiddenTags = new() { "藏" };
                l.Settings.PageMode = ViewRules.PageModes[1]; l.Settings.PageIndex = 2; l.Settings.VisiblePriorities = new() { "低" };
            }), "Synthetic archive setup");
            Field<TextBox>(widget, "search").Text = "No synthetic match";
            Field<ComboBox>(widget, "filter").SelectedIndex = 2;
            var beforeSettings = System.Text.Json.JsonSerializer.Serialize(Field<Library>(widget, "library").Settings);
            Exception? archiveFailure = null;
            _ = Dispatcher.BeginInvoke(new Action(async () =>
            {
                ArchivedPapersWindow? archive = null;
                try
                {
                    archive = Windows.OfType<ArchivedPapersWindow>().Single(); await Task.Delay(150);
                    var rows = Field<StackPanel>(archive, "rows"); var search = Field<TextBox>(archive, "search");
                    Check(rows.Children.Count == 3, "archive window lists all archived papers regardless of widget filters");
                    Snapshot(archive, "18-archive-light");
                    var title = Field<Library>(widget, "library").Papers.First(p => p.Archived).Title;
                    search.Text = title; Check(rows.Children.Count == 1, "archive search finds a title");
                    search.Text = "No matching title"; Check(Descendants<TextBlock>(rows).Any(t => t.Text == Lang.T("没有匹配的归档论文。")), "archive search has an empty state"); search.Clear();
                    var dark = Storage.CloneLibrary(Field<Library>(widget, "library")).Settings; dark.Theme = "夜航 · 霜蓝";
                    Appearance.Apply(dark); await Task.Delay(100); Snapshot(archive, "19-archive-dark");
                    Check(((SolidColorBrush)archive.Background).Color == (Color)ColorConverter.ConvertFromString(Appearance.Current.Window), "archive window supports dark themes");
                    Check(((SolidColorBrush)Descendants<TextBlock>(rows).First().Foreground).Color == (Color)ColorConverter.ConvertFromString(Appearance.Current.Ink), "archive paper titles remain legible after a dark theme switch");
                    ScheduleConfirmation(false); Click((DependencyObject)rows.Children[0], "删除论文");
                    Appearance.Apply(Field<Library>(widget, "library").Settings);
                    Click((DependencyObject)rows.Children[0], "恢复到论文列表");
                    Check(storage.Load().Papers.Count(p => p.Archived) == 2 && rows.Children.Count == 2, "restore updates the archive list immediately");
                    var editId = (string)((FrameworkElement)rows.Children[0]).Tag;
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var editor = Windows.OfType<PaperEditor>().Single();
                        Field<WrapPanel>(editor, "doneRow").Children.OfType<CheckBox>().First().IsChecked = true;
                        Click(editor, "保存资料");
                    }));
                    Click((DependencyObject)rows.Children[0], "编辑论文资料");
                    Check(storage.Load().Papers.Single(p => p.Id == editId).Stages[0].Done && rows.Children.Count == 2, "archived paper details remain editable without restoring the paper");
                    ScheduleConfirmation(false); Click((DependencyObject)rows.Children[0], "删除论文");
                    Check(rows.Children.Count == 2, "archive delete cancellation leaves papers intact");
                    ScheduleConfirmation(true); Click((DependencyObject)rows.Children[0], "删除论文");
                    Check(rows.Children.Count == 1 && storage.Load().Papers.Count(p => p.Archived) == 1, "archive deletion removes the selected paper");
                    Click((DependencyObject)rows.Children[0], "恢复到论文列表");
                    Check(Descendants<TextBlock>(rows).Any(t => t.Text == Lang.T("还没有归档论文。")), "archive window has a no-papers state after restoring the last paper");
                    Check(Field<TextBox>(widget, "search").Text == "No synthetic match" && Field<ComboBox>(widget, "filter").SelectedIndex == 2, "archive actions do not change widget search or filter");
                    Check(System.Text.Json.JsonSerializer.Serialize(Field<Library>(widget, "library").Settings) == beforeSettings, "archive actions preserve all widget display preferences");
                    archive.Close();
                }
                catch (Exception ex) { archiveFailure = ex; archive?.Close(); }
            }));
            Invoke(widget, "OpenArchive", widget);
            if (archiveFailure != null) throw archiveFailure;
            if (confirmationFailure != null) throw confirmationFailure;
        }

        private static void Click(DependencyObject root, string text) => Descendants<Button>(root).Single(b => Equals(b.Content, Lang.T(text))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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

        private async Task CheckThemesAndOrder(MainWindow widget, Storage storage, StackPanel cards)
        {
            bool ColorIs(Brush brush, string color) => brush is SolidColorBrush solid && solid.Color == (Color)ColorConverter.ConvertFromString(color);
            Field<ComboBox>(widget, "filter").SelectedIndex = 0; Field<TextBox>(widget, "search").Clear();
            Invoke(widget, "Commit", (Action<Library>)(l =>
            {
                l.Papers = Enumerable.Range(1, 6).Select(i => new Paper { Title = "Synthetic paper " + i, Priority = i < 4 ? "高" : "中" }).ToList();
                l.Settings.VisiblePriorities = Paper.Priorities.ToList(); l.Settings.TagHidingEnabled = false;
            }), null!);
            foreach (string mode in new[] { "full", "minimal" })
            foreach (int sortIndex in new[] { 0, 1, 2, 3 })
            {
                Invoke(widget, "Commit", (Action<Library>)(l =>
                {
                    l.Settings.DisplayMode = mode;
                    l.Settings.PageMode = ViewRules.PageModes[1]; l.Settings.PageIndex = 0;
                }), null!);
                Field<ComboBox>(widget, "sort").SelectedIndex = sortIndex;
                var visible = cards.Children.OfType<Border>().Select(c => (string)c.Tag).ToArray();
                var before = storage.Load();
                // The sorted first case also needs to persist the order before switching modes.
                string id = visible[sortIndex == 1 ? 0 : ^1];
                var paper = Field<Library>(widget, "library").Papers.Single(p => p.Id == id);
                var anchor = (FrameworkElement)cards.Children.OfType<Border>().Single(c => Equals(c.Tag, id));
                Invoke(widget, "PaperMenu", paper, anchor);
                var menu = anchor.ContextMenu;
                var move = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, Lang.T("移到最前面")));
                menu.IsOpen = false; move.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Task.Delay(100);
                var after = storage.Load();
                Check(Equals(((Border)cards.Children[0]).Tag, id), "move-to-front menu works in " + mode + " sort " + sortIndex);
                Check(after.Settings.SortMode == ViewRules.SortModes[0] && Field<ComboBox>(widget, "sort").SelectedIndex == 0, "move switches and saves manual order");
                Check(after.Papers.Where(p => visible.Contains(p.Id)).Select(p => p.Id).SequenceEqual(new[] { id }.Concat(visible.Where(x => x != id))), "rest of visible order survives");
                Check(before.Papers.Select((p, i) => visible.Contains(p.Id) || after.Papers[i].Id == p.Id).All(x => x), "other priority-page slots are untouched");
                Check(after.Papers.All(p => before.Papers.Single(b => b.Id == p.Id).Priority == p.Priority), "move does not change priorities");
            }
            var current = Field<Library>(widget, "library");
            var prefs = Storage.CloneLibrary(current).Settings;
            var sample = Storage.Clone(current.Papers[0]);
            var editor = new PaperEditor(sample, Array.Empty<string>(), Array.Empty<StageScheme>()) { Owner = widget };
            var dialogs = new Window[]
            {
                editor, new NewPaperDialog(), new TextPrompt("Synthetic scheme", "Synthetic hint", ""),
                new StageEditorDialog("Synthetic stages", "Synthetic hint", sample.SchemeName, sample.Stages.Select(s => s.Name).ToList(), false),
                new DeletePaperDialog("Synthetic paper"), new UpdateNotesDialog("0.0.0", "Synthetic notes"),
                new ThemeMessageBox("Synthetic confirmation", "Synthetic title", MessageBoxButton.OKCancel, MessageBoxImage.Question)
            };
            foreach (var dialog in dialogs) { dialog.Owner = widget; dialog.Show(); }
            foreach (var scene in new[] { (Themes.Default, "light"), ("夜航 · 霜蓝", "dark"), ("柔光 · 藕荷", "color") })
            {
                prefs.Theme = scene.Item1; Appearance.Apply(prefs); await Task.Delay(100);
                foreach (var dialog in dialogs)
                    Check(ColorIs(dialog.Background, Appearance.Current.Window) && ColorIs(dialog.Foreground, Appearance.Current.Ink), dialog.GetType().Name + " follows live " + scene.Item2 + " theme");
                var anchor = (FrameworkElement)cards.Children[0];
                Invoke(widget, "PaperMenu", sample, anchor); await Task.Delay(100);
                var menu = anchor.ContextMenu;
                Check(ColorIs(menu.Background, Appearance.Current.Card), "paper menu background follows " + scene.Item2);
                Check(menu.Items.OfType<MenuItem>().All(i => ColorIs(i.Foreground, Appearance.Current.Ink)), "paper menu text follows " + scene.Item2);
                Snapshot(menu, "theme-menu-" + scene.Item2); menu.IsOpen = false;
                Invoke(widget, "PriorityMenu", sample, anchor); await Task.Delay(50);
                menu = anchor.ContextMenu;
                var selected = menu.Items.OfType<MenuItem>().Single(i => i.IsChecked);
                Check(ColorIs(menu.Background, Appearance.Current.Card) && ((System.Windows.Shapes.Path)selected.Template.FindName("Tick", selected)).Visibility == Visibility.Visible, "priority menu retains its themed checkmark");
                Snapshot(menu, "theme-priority-" + scene.Item2); menu.IsOpen = false;
                var tip = new ToolTip { Content = "Synthetic tooltip", PlacementTarget = anchor, IsOpen = true }; await Task.Delay(50);
                Check(ColorIs(tip.Background, Appearance.Current.Card) && ColorIs(tip.Foreground, Appearance.Current.Ink), "tooltip follows " + scene.Item2); tip.IsOpen = false;
                var input = Descendants<TextBox>(editor).First(t => t.Text == sample.Title);
                var editMenu = input.ContextMenu; editMenu.PlacementTarget = input; editMenu.IsOpen = true;
                await Task.Delay(50);
                var selectAll = editMenu.Items.OfType<MenuItem>().Single(i => i.Command == ApplicationCommands.SelectAll);
                Check(ColorIs(editMenu.Background, Appearance.Current.Card) && selectAll.CommandTarget == input, "input menu is themed and targets the correct field");
                editMenu.IsOpen = false; ApplicationCommands.SelectAll.Execute(null, selectAll.CommandTarget);
                Check(input.SelectedText == input.Text, "input menu commands still work");
                dynamic tray = Field<object>(widget, "tray");
                object trayMenu = tray.ContextMenuStrip;
                Invoke(trayMenu, "OnOpening", new System.ComponentModel.CancelEventArgs());
                Check(((dynamic)trayMenu).BackColor.ToArgb() == System.Drawing.ColorTranslator.FromHtml(Appearance.Current.Card).ToArgb(), "tray menu uses the current theme");
                var date = Descendants<DatePicker>(editor).First();
                date.IsDropDownOpen = true; await Task.Delay(100);
                var popup = (System.Windows.Controls.Primitives.Popup)date.Template.FindName("PART_Popup", date);
                var calendar = (Calendar)popup.Child;
                var item = Descendants<System.Windows.Controls.Primitives.CalendarItem>(calendar).Single();
                Check(ColorIs(item.Background, Appearance.Current.Card) && ColorIs(item.Foreground, Appearance.Current.Ink), "calendar follows " + scene.Item2);
                Check(Descendants<System.Windows.Controls.Primitives.CalendarDayButton>(calendar).Count() == 42, "calendar displays all day cells");
                var original = date.SelectedDate;
                calendar.SelectedDate = original!.Value.AddDays(1);
                Check(date.SelectedDate == original.Value.AddDays(1), "calendar selection still updates date input");
                date.IsDropDownOpen = true; await Task.Delay(50);
                Snapshot(calendar, "theme-calendar-" + scene.Item2);
                calendar.DisplayMode = CalendarMode.Year; await Task.Delay(50);
                Check(Descendants<System.Windows.Controls.Primitives.CalendarButton>(calendar).Count(b => b.IsVisible) == 12, "month navigation stays available");
                calendar.DisplayMode = CalendarMode.Decade; await Task.Delay(50);
                Check(Descendants<System.Windows.Controls.Primitives.CalendarButton>(calendar).Count(b => b.IsVisible) == 12, "year navigation stays available");
                calendar.DisplayMode = CalendarMode.Month; date.IsDropDownOpen = false;
                date.Text = original.Value.ToShortDateString();
                Check(date.SelectedDate == original, "typing a date still commits through the themed input");
                Snapshot(editor, "theme-editor-" + scene.Item2);
                Snapshot(dialogs[^1], "theme-confirm-" + scene.Item2);
            }
            foreach (var dialog in dialogs) dialog.Close();
            foreach (var buttons in new[] { MessageBoxButton.OK, MessageBoxButton.OKCancel, MessageBoxButton.YesNoCancel })
            foreach (var action in new[] { "close", "accept" })
            {
                Exception? failure = null;
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    var dialog = Windows.OfType<ThemeMessageBox>().Single();
                    try
                    {
                        Check(Descendants<Button>(dialog).Single(b => b.IsDefault).Content.Equals(buttons == MessageBoxButton.OK ? Lang.T("好") : Lang.T("取消")), "message defaults to a safe answer");
                        if (action == "accept") Click(dialog, buttons == MessageBoxButton.YesNoCancel ? "是" : "好");
                        else dialog.Close();
                    }
                    catch (Exception ex) { failure = ex; dialog.Close(); }
                }));
                var result = ThemeMessageBox.Show(widget, "Synthetic message", "Synthetic title", buttons);
                if (failure != null) throw failure;
                var expected = action == "accept" ? (buttons == MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK) : buttons == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.Cancel;
                Check(result == expected, "message preserves result for " + buttons + " " + action);
            }
        }

        private void Snapshot(FrameworkElement window, string name)
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
