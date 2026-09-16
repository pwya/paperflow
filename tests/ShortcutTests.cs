using PaperFlow;

// 整台测试机就是 Windows：快捷方式、注册表这些本来就只在 Windows 上有意义。
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

static class ShortcutTests
{
    // 真建 .lnk：快捷方式是文件，能在这里验证“建了没有、指向哪里、删的是不是自己那个”。
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "PaperFlow-shortcut-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string Fake(string name) { var path = Path.Combine(root, name); File.WriteAllText(path, "not a real program"); return path; }
        var launcher = Fake("PaperFlow.Launcher.exe");
        var foreignTarget = Fake("Explorer.exe");
        var link = Path.Combine(root, "PaperFlow.lnk");

        check(!Shortcuts.Exists(link), "shortcut starts absent");
        check(Shortcuts.TargetOf(link) == null, "a missing shortcut has no target");

        Shortcuts.Create(link, launcher, root);
        check(Shortcuts.Exists(link), "create writes the shortcut file");
        check(string.Equals(Shortcuts.TargetOf(link), launcher, StringComparison.OrdinalIgnoreCase), "the shortcut points at the launcher");
        check(Shortcuts.LooksLikeOurs(link), "our own shortcut is recognized");
        check(Shortcuts.Remove(link), "our own shortcut is removed");
        check(!Shortcuts.Exists(link), "the removed shortcut is gone");
        check(!Shortcuts.Remove(link), "removing a missing shortcut is a no-op");

        // 同一个位置也有可能是用户自己放的同名快捷方式：只能拒绝删除，不能替他做决定。
        Shortcuts.Create(link, foreignTarget, root);
        check(!Shortcuts.LooksLikeOurs(link), "a shortcut pointing elsewhere is not treated as ours");
        bool refused = false;
        try { Shortcuts.Remove(link); } catch (InvalidOperationException) { refused = true; }
        check(refused && File.Exists(link), "removing a foreign shortcut is refused loudly");
        File.Delete(link);

        // 目标选择：有固定启动入口就指向它；没有就退回当前程序，绝不留一个空目标。
        check(Shortcuts.HasLauncher(launcher), "an existing launcher is accepted");
        check(Shortcuts.ResolveTarget(launcher) == Path.GetFullPath(launcher), "the launcher wins when it exists");
        var missing = Path.Combine(root, "gone", "PaperFlow.Launcher.exe");
        check(!Shortcuts.HasLauncher(missing), "a missing launcher is rejected");
        check(Shortcuts.ResolveTarget(missing) == Environment.ProcessPath, "a missing launcher falls back to the running program");
        check(!string.IsNullOrWhiteSpace(Shortcuts.ResolveTarget("")), "an empty launcher path still yields a target");
        check(Shortcuts.ProgramFolder(launcher) == root, "the program folder follows the launcher");
        check(Shortcuts.DesktopPath().EndsWith(Shortcuts.FileName) && Shortcuts.StartMenuPath().EndsWith("Programs" + Path.DirectorySeparatorChar + Shortcuts.FileName), "both shortcut names are fixed");
    }
}
