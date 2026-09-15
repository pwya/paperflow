using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;

[DataContract]
public sealed class Channel
{
    [DataMember(Name="version")] public string Version;
    [DataMember(Name="exePath")] public string ExePath;
    [DataMember(Name="sha256")] public string Sha256;
    [DataMember(Name="length")] public long Length;
}
internal static class Program
{
    static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    static Channel Read(string path)
    {
        using (var stream = File.OpenRead(path))
        {
            var value = (Channel)new DataContractJsonSerializer(typeof(Channel)).ReadObject(stream);
            Version parsed;
            if (value == null || !Version.TryParse(value.Version, out parsed) || !Regex.IsMatch(value.Version, "^[0-9]+\\.[0-9]+\\.[0-9]+$") || !Regex.IsMatch(value.Sha256 ?? "", "^[a-fA-F0-9]{64}$") || value.Length < 1 || value.ExePath != "versions/" + value.Version + "/PaperFlow.exe") throw new InvalidDataException("更新清单无效。");
            return value;
        }
    }
    static string CachedExe(string root, Channel value) { return Path.Combine(root, value.Version + "-" + value.Sha256.Substring(0, 12), "PaperFlow.exe"); }
    static bool Valid(string path, Channel value) { return File.Exists(path) && new FileInfo(path).Length == value.Length && string.Equals(Hash(path), value.Sha256, StringComparison.OrdinalIgnoreCase); }
    static string Quote(string text) { return "\"" + text.TrimEnd('\\') + "\""; }
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        var shared = AppDomain.CurrentDomain.BaseDirectory;
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperFlow", "app-cache");
        Directory.CreateDirectory(local);
        // Diagnostic roots are explicit flags; normal users always use this launcher's
        // own folder, so two machines can have different OneDrive absolute paths.
        for (int i = 0; i + 1 < args.Length; i++) { if (args[i] == "--package-dir") shared = Path.GetFullPath(args[++i]); else if (args[i] == "--cache-dir") local = Path.GetFullPath(args[++i]); }
        Directory.CreateDirectory(local);
        var remembered = Path.Combine(local, "last-good.json");
        Channel channel = null; string executable = null; string problem = null;
        try
        {
            channel = Read(Path.Combine(shared, "channel.json")); executable = CachedExe(local, channel);
            if (!Valid(executable, channel))
            {
                var source = Path.Combine(shared, channel.ExePath.Replace('/', Path.DirectorySeparatorChar));
                if (!Valid(source, channel)) throw new IOException("新版程序尚未完整同步。");
                Directory.CreateDirectory(Path.GetDirectoryName(executable));
                var temp = executable + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.Copy(source, temp, false);
                if (!Valid(temp, channel)) { File.Delete(temp); throw new IOException("程序校验失败。"); }
                if (File.Exists(executable)) File.Delete(executable);
                File.Move(temp, executable);
            }
            var marker = remembered + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = File.Create(marker)) new DataContractJsonSerializer(typeof(Channel)).WriteObject(stream, channel);
            if (File.Exists(remembered)) File.Replace(marker, remembered, null); else File.Move(marker, remembered);
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            try { channel = Read(remembered); executable = CachedExe(local, channel); if (!Valid(executable, channel)) executable = null; } catch { executable = null; }
        }
        if (executable == null) { MessageBox.Show("程序还没有完整同步到这台电脑。请等待 OneDrive 完成同步后重试。\n\n" + problem, "PaperFlow", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try
        {
            // Test mode verifies hydration/checksum/fallback without opening a UI.
            string report = null; for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--check-only") report = args[i + 1];
            if (report != null) { File.WriteAllText(report, channel.Version + "\n" + executable + "\n" + (problem ?? "OK")); return; }
            var launcher = Path.Combine(shared, "PaperFlow.Launcher.exe");
            Process.Start(new ProcessStartInfo(executable, "--sync-dir " + Quote(Path.Combine(shared, "data")) + " --launcher " + Quote(launcher)) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) });
        }
        catch (Exception ex) { MessageBox.Show("无法启动 PaperFlow。\n" + ex.Message, "PaperFlow", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
