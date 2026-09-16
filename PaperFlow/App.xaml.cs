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
        // 还没读到设置之前先跟随系统语言，这样连“资料打不开”这类早期提示也是对的语言。
        Lang.Apply(null);
        // 换语言/装完更新后的自动重开：新进程先等旧进程退出，免得单实例锁把挂件弄丢。
        int waitIndex = Array.IndexOf(e.Args, "--wait-for");
        if (waitIndex >= 0 && e.Args.Length > waitIndex + 1 && int.TryParse(e.Args[waitIndex + 1], out int previous))
        {
            try { System.Diagnostics.Process.GetProcessById(previous).WaitForExit(20000); }
            catch (Exception) { }
        }
        int updateUrlIndex = Array.IndexOf(e.Args, "--update-url");
        if (updateUrlIndex >= 0 && e.Args.Length > updateUrlIndex + 1)
        {
            // 只给开发和自动化测试用：把更新清单指到本地地址，验证整条下载安装链。
            if (Uri.TryCreate(e.Args[updateUrlIndex + 1], UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)) Updates.SingleManifestUrl = url.ToString();
        }
        int promoIndex = Array.IndexOf(e.Args, "--promotional-assets");
        int galleryIndex = Array.IndexOf(e.Args, "--theme-gallery");
        int chimeIndex = Array.IndexOf(e.Args, "--sound-check");
        if (chimeIndex >= 0)
        {
            try
            {
                if (chimeIndex + 1 >= e.Args.Length) throw new ArgumentException(Lang.T("请指定音效输出目录。"));
                Chime.ExportSamples(Path.GetFullPath(e.Args[chimeIndex + 1]));
                Shutdown(0);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Shutdown(1); }
            return; // 只导出音效，不加载任何资料。
        }
        if (galleryIndex >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (galleryIndex + 1 >= e.Args.Length) throw new ArgumentException(Lang.T("请指定主题一览图的输出目录。"));
                    await ThemeGallery.Generate(Path.GetFullPath(e.Args[galleryIndex + 1]));
                    Shutdown(0);
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); Shutdown(1); }
            }));
            return; // 主题一览同样不加载正式资料。
        }
        if (promoIndex >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (promoIndex + 1 >= e.Args.Length) throw new ArgumentException(Lang.T("请指定宣传图输出目录。"));
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
            if (storage.RecoveryNotice != null) MessageBox.Show(window, storage.RecoveryNotice, Lang.T("已恢复备份"));
            else if (migrationNotice != null) MessageBox.Show(window, migrationNotice + Lang.T("\n\n原目录 %LOCALAPPDATA%\\PaperProgress 未被修改，确认新版本正常后可以自行删除。"), Lang.T("PaperFlow 已升级"), MessageBoxButton.OK, MessageBoxImage.Information);
            // 启动后过几秒再看更新：不挡启动，也不在演示导出里联网。
            var updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            updateTimer.Tick += (_, _) => { updateTimer.Stop(); _ = window.CheckForUpdatesAsync(false); };
            updateTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Lang.T("无法打开论文资料，程序没有覆盖原数据。\n\n") + ex.Message, "PaperFlow", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
