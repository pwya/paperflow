using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace PaperProgress;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperProgress");
        int overrideIndex = Array.IndexOf(e.Args, "--data-dir");
        if (overrideIndex >= 0 && e.Args.Length > overrideIndex + 1) dataDirectory = Path.GetFullPath(e.Args[overrideIndex + 1]);
        string suffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant())))[..16];
        instance = new Mutex(true, "Local\\PaperProgress-" + suffix, out bool created);
        if (!created)
        {
            // The running instance polls this local signal to reveal its window.
            File.WriteAllText(Path.Combine(dataDirectory, "show.signal"), "show");
            Shutdown(); return;
        }
        try
        {
            var storage = new Storage(dataDirectory);
            var library = storage.Load();
            int syncIndex = Array.IndexOf(e.Args, "--sync-dir");
            if (syncIndex >= 0 && e.Args.Length > syncIndex + 1) library.Settings.SyncFolder = Path.GetFullPath(e.Args[syncIndex + 1]);
            int launcherIndex = Array.IndexOf(e.Args, "--launcher");
            if (launcherIndex >= 0 && e.Args.Length > launcherIndex + 1) library.Settings.LauncherPath = Path.GetFullPath(e.Args[launcherIndex + 1]);
            var sync = new SyncEngine(dataDirectory, library.Settings.SyncFolder, library);
            library.Papers = sync.Snapshot().Papers;
            storage.Save(library);
            Appearance.Apply(library.Settings);
            var window = new MainWindow(storage, library, sync);
            MainWindow = window;
            window.Show();
            if (storage.RecoveryNotice != null) MessageBox.Show(window, storage.RecoveryNotice, "已恢复备份");
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法打开论文资料，程序没有覆盖原数据。\n\n" + ex.Message, "论文进度", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
