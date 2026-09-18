using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;

namespace PaperFlow;

// 快捷方式是普通文件（.lnk），用 Windows 自带的 WScript.Shell 组件创建：
// 不写注册表、不引入第三方库，删掉文件就等于移除。
// 位置和文件名都是固定的（桌面 / 开始菜单里的 PaperFlow.lnk），
// 所以只会动自己做的这一个文件，不会碰用户自己建的其它快捷方式。
[SupportedOSPlatform("windows")]
public static class Shortcuts
{
    public const string FileName = "PaperFlow.lnk";
    private static string Description => Lang.T("PaperFlow · 论文投稿进度");

    private static object? Shell() => Type.GetTypeFromProgID("WScript.Shell") is Type type ? Activator.CreateInstance(type) : null;

    public static string DesktopFolder() => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public static string StartMenuFolder() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs");
    public static string DesktopPath() => Path.Combine(DesktopFolder(), FileName);
    public static string StartMenuPath() => Path.Combine(StartMenuFolder(), FileName);

    // 快捷方式必须指向固定的启动入口，不能指向某个版本目录里的 exe：
    // 启动入口每次都会算出该跑哪个版本，直接指向某个版本的 exe，升级后就会一直打开旧版本。
    public static bool HasLauncher(string launcherPath) => !string.IsNullOrWhiteSpace(launcherPath) && File.Exists(launcherPath);
    public static string ResolveTarget(string launcherPath) => HasLauncher(launcherPath) ? Path.GetFullPath(launcherPath) : Environment.ProcessPath ?? "";

    // 回答“以后更新程序要去哪个文件夹”，也是快捷方式的工作目录。
    public static string ProgramFolder(string launcherPath) => HasLauncher(launcherPath)
        ? Path.GetDirectoryName(Path.GetFullPath(launcherPath))!
        : AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    public static bool Exists(string shortcutPath) => File.Exists(shortcutPath);

    public static void Create(string shortcutPath, string target, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException(Lang.T("找不到可以指向的程序文件。"));
        var folder = Path.GetDirectoryName(shortcutPath);
        if (string.IsNullOrEmpty(folder)) throw new InvalidOperationException(Lang.T("快捷方式保存位置无效。"));
        Directory.CreateDirectory(folder);
        dynamic shell = Shell() ?? throw new InvalidOperationException(Lang.T("这台电脑缺少 Windows 脚本组件，无法创建快捷方式。"));
        dynamic link = shell.CreateShortcut(shortcutPath);
        link.TargetPath = target;
        link.WorkingDirectory = workingDirectory;
        link.Description = Description;
        link.IconLocation = target + ",0";
        link.Save();
    }

    public static string? TargetOf(string shortcutPath)
    {
        if (!File.Exists(shortcutPath)) return null;
        dynamic shell = Shell() ?? throw new InvalidOperationException(Lang.T("这台电脑缺少 Windows 脚本组件，无法读取快捷方式。"));
        dynamic link = shell.CreateShortcut(shortcutPath);
        var target = link.TargetPath as string;
        return string.IsNullOrWhiteSpace(target) ? null : target;
    }

    // 只认“指向 PaperFlow 自己”的快捷方式。用户若在同一个位置放了一个同名但指向别处的文件，
    // 这里会拒绝删除并说明原因，而不是替用户做决定。
    public static bool LooksLikeOurs(string shortcutPath)
    {
        var target = TargetOf(shortcutPath);
        return target != null && Path.GetFileName(target).StartsWith("PaperFlow", StringComparison.OrdinalIgnoreCase);
    }

    public static bool Remove(string shortcutPath)
    {
        if (!File.Exists(shortcutPath)) return false;
        if (!LooksLikeOurs(shortcutPath)) throw new InvalidOperationException(shortcutPath + Lang.T(" 不是本程序创建的快捷方式，没有删除。请自己确认后手动删除它。"));
        File.Delete(shortcutPath);
        return true;
    }

    // 把桌面、开始菜单两处的状态对齐到用户刚选的勾选框；某一处失败不影响另一处，
    // 最后把出问题的地方一次性报出来。
    public static void Apply(bool desktop, bool startMenu, string launcherPath)
    {
        if (Product.Portable) throw new InvalidOperationException(Lang.T("试用模式（指定了资料目录）不会改动桌面快捷方式和开机启动，免得动到你正式在用的那套设置。"));
        var target = ResolveTarget(launcherPath);
        var working = ProgramFolder(launcherPath);
        var problems = new List<string>();
        Sync(desktop, DesktopPath(), Lang.T("桌面"), target, working, problems);
        Sync(startMenu, StartMenuPath(), Lang.T("开始菜单"), target, working, problems);
        if (problems.Count > 0) throw new InvalidOperationException(string.Join("\n", problems));
    }

    private static void Sync(bool wanted, string path, string label, string target, string working, List<string> problems)
    {
        try
        {
            if (wanted) { if (!Exists(path) || !LooksLikeOurs(path)) Create(path, target, working); }
            else Remove(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { problems.Add(Lang.F("{0}：{1}", label, ex.Message)); }
    }
}
