using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace PaperFlow;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        int promoIndex = Array.IndexOf(e.Args, "--promotional-assets");
        if (promoIndex >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (promoIndex + 1 >= e.Args.Length) throw new ArgumentException("请指定宣传图输出目录。");
                    await PromotionExporter.Generate(Path.GetFullPath(e.Args[promoIndex + 1]));
                    Shutdown(0);
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); Shutdown(1); }
            }));
            return; // Never load the normal data folder in promotional export mode.
        }
        var localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataDirectory = Path.Combine(localRoot, "PaperFlow");
        var legacyDirectory = Path.Combine(localRoot, "PaperProgress");
        bool explicitDataDirectory = false;
        string? migrationNotice = null;
        int overrideIndex = Array.IndexOf(e.Args, "--data-dir");
        if (overrideIndex >= 0 && e.Args.Length > overrideIndex + 1) { dataDirectory = Path.GetFullPath(e.Args[overrideIndex + 1]); explicitDataDirectory = true; }
        string suffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant())))[..16];
        instance = new Mutex(true, "Local\\PaperFlow-" + suffix, out bool created);
        if (!created)
        {
            // The running instance polls this local signal to reveal its window.
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(Path.Combine(dataDirectory, "show.signal"), "show");
            Shutdown(); return;
        }
        try
        {
            // Runs after the single-instance check so two windows never migrate at once.
            if (!explicitDataDirectory) migrationNotice = Storage.MigrateLegacyRoot(legacyDirectory, dataDirectory);
            var storage = new Storage(dataDirectory);
            var library = storage.Load();
            int syncIndex = Array.IndexOf(e.Args, "--sync-dir");
            if (syncIndex >= 0 && e.Args.Length > syncIndex + 1) library.Settings.SyncFolder = Path.GetFullPath(e.Args[syncIndex + 1]);
            int launcherIndex = Array.IndexOf(e.Args, "--launcher");
            if (launcherIndex >= 0 && e.Args.Length > launcherIndex + 1) library.Settings.LauncherPath = Path.GetFullPath(e.Args[launcherIndex + 1]);
            StartupEntry.Migrate(library.Settings.LauncherPath);
            var sync = new SyncEngine(dataDirectory, library.Settings.SyncFolder, library);
            library.Papers = sync.Snapshot().Papers;
            storage.Save(library);
            Appearance.Apply(library.Settings);
            var window = new MainWindow(storage, library, sync);
            MainWindow = window;
            window.Show();
            if (storage.RecoveryNotice != null) MessageBox.Show(window, storage.RecoveryNotice, "已恢复备份");
            else if (migrationNotice != null) MessageBox.Show(window, migrationNotice + "\n\n原目录 %LOCALAPPDATA%\\PaperProgress 未被修改，确认新版本正常后可以自行删除。", "PaperFlow 已升级", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法打开论文资料，程序没有覆盖原数据。\n\n" + ex.Message, "PaperFlow", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
