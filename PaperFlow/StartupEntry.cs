using System;
using System.IO;
using Microsoft.Win32;

namespace PaperFlow;

// The autostart entry was named PaperProgress until 1.4.0. Rewrite it in place, because
// the old launcher cannot read the new channel manifest and would stay on the old build.
public static class StartupEntry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "PaperFlow";
    private const string LegacyName = "PaperProgress";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) is string || key?.GetValue(LegacyName) is string;
    }
    public static void Write(bool enabled, string launcher)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (!enabled) { key.DeleteValue(Name, false); key.DeleteValue(LegacyName, false); return; }
        if (launcher == "" || !File.Exists(launcher)) throw new IOException("启动入口不可用，请从 OneDrive 中的启动器打开后再设置。");
        key.SetValue(Name, "\"" + launcher + "\"");
        key.DeleteValue(LegacyName, false);
    }
    public static void Migrate(string launcher)
    {
        if (launcher == "" || !File.Exists(launcher)) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(LegacyName) is not string) return;
            Write(true, launcher);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }
}
