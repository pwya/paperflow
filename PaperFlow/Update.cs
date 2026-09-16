using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PaperFlow;

// 一次检查失败的原因：给人看的一句话，加上"是不是根本没连上"。
public sealed record UpdateFailure(string Message, bool Network);

// 一份更新清单，就是发布时一起上传的 update.json 的内容。
public sealed record UpdateManifest(string Version, string Url, string Sha256, long Length)
{
    // 清单是别人（Release 附件）给的，坏数据必须响亮拒绝，不能猜。
    public static UpdateManifest Parse(string json, bool allowInsecureUrl)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string version = Text(root, "version"), url = Text(root, "url"), sha = Text(root, "sha256");
        long length = root.TryGetProperty("length", out var raw) && raw.TryGetInt64(out var value) ? value : 0;
        if (!Regex.IsMatch(version, "^[0-9]+\\.[0-9]+\\.[0-9]+$")) throw new InvalidDataException(Lang.T("更新清单的版本号无效。"));
        if (!Regex.IsMatch(sha, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException(Lang.T("更新清单的校验值无效。"));
        if (length < 1 || length > 300_000_000) throw new InvalidDataException(Lang.T("更新清单的文件大小无效。"));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !(allowInsecureUrl && uri.Scheme == Uri.UriSchemeHttp)))
            throw new InvalidDataException(Lang.T("更新清单的下载地址无效。"));
        if (!uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Lang.T("更新清单的下载地址不是程序文件。"));
        return new UpdateManifest(version, url, sha.ToLowerInvariant(), length);
    }
    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : "";
}

public static class Updates
{
    // 两个候选清单地址，都指向各自发布页里一份同名清单，描述的是同一个程序文件
    // （同哈希、同长度）。Gitee 放前面是因为国内直连它快得多；GitHub 那份仍然要读，
    // 否则镜像一旦落后就会漏掉新版本——所以是"两边都取，谁新用谁"，不是"谁先通用谁"。
    public const string GiteeManifestUrl = "https://gitee.com/pan-wang-yuang/paperflow/releases/download/latest/update.json";
    public const string DefaultManifestUrl = "https://github.com/pwya/paperflow/releases/latest/download/update.json";
    public static List<string> ManifestUrls { get; } = new() { GiteeManifestUrl, DefaultManifestUrl };
    // 只给开发和自动化测试用：把清单地址指到一个固定地址（本地假服务器），此时只用它。
    public static string? SingleManifestUrl { get; set; }
    public static bool UsingOverride => SingleManifestUrl != null;
    public static IReadOnlyList<string> Candidates() => SingleManifestUrl is string only ? new[] { only } : ManifestUrls.ToArray();
    // 设置窗口手动检查到的版本，交给挂件去显示提示条和下载按钮。
    public static UpdateManifest? Offered { get; set; }
    // 最近一次检查为什么失败（成功时清空）。挂件和设置页都靠它说话。
    public static UpdateFailure? LastFailure { get; set; }

    // 把异常翻成一句人话。Network 为真表示"根本没连上"——那种情况要告诉用户
    // 可以稍后再试、或者干脆关掉更新提示，而不是甩一串技术错误。
    public static UpdateFailure Describe(Exception ex) => ex switch
    {
        TaskCanceledException or OperationCanceledException => new(Lang.T("连接更新服务器超时，可能是网络慢或被拦住了。"), true),
        HttpRequestException => new(Lang.T("连不上更新服务器（Gitee 和 GitHub 都没连上），大概是网络的问题。"), true),
        System.Net.Sockets.SocketException => new(Lang.T("连不上更新服务器（Gitee 和 GitHub 都没连上），大概是网络的问题。"), true),
        JsonException => new(Lang.T("更新清单读不出来。"), false),
        _ => new(ex.Message, false)
    };

    // 不做应用内代理设置：跟随 Windows 自己的网络配置，国内访问慢的问题是靠
    // 以后加 Gitee 镜像解决，而不是让用户在设置里填一个地址。
    private static HttpClient Build(TimeSpan timeout)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, UseCookies = false }) { Timeout = timeout };
        // 只报自己的名字和版本；不带设备标识、不带论文信息、不带 Cookie。
        client.DefaultRequestHeaders.Add("User-Agent", "PaperFlow/" + Product.Version);
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        return client;
    }
    // 清单很小，十分钟足够；下载另算（见 DownloadAsync 的卡住检测）。
    public static HttpClient Client() => Build(TimeSpan.FromMinutes(10));
    public static HttpClient DownloadClient() => Build(Timeout.InfiniteTimeSpan);

    // 三档：always 每次启动都看，daily 大约一天一次，never 完全不看。
    public static bool ShouldCheck(string mode, DateTime? lastCheckUtc, DateTime nowUtc) => mode switch
    {
        "never" => false,
        "always" => true,
        _ => lastCheckUtc is not DateTime last || nowUtc - last >= TimeSpan.FromHours(20)
    };

    public static bool IsNewer(string candidate, string current)
        => Version.TryParse(candidate, out var a) && Version.TryParse(current, out var b) && a > b;

    public static async Task<UpdateManifest?> FetchAsync(HttpClient client, CancellationToken token)
    {
        var found = new List<UpdateManifest>();
        Exception? last = null;
        foreach (var url in Candidates())
        {
            try
            {
                using var response = await client.GetAsync(url, token);
                if (!response.IsSuccessStatusCode) throw new InvalidDataException(Lang.F("检查更新失败：{0}", (int)response.StatusCode));
                found.Add(UpdateManifest.Parse(await response.Content.ReadAsStringAsync(token), UsingOverride));
            }
            catch (Exception ex) { last = ex; }
        }
        if (found.Count == 0) throw last ?? new InvalidDataException(Lang.T("更新清单读不出来。"));
        var newest = found.OrderByDescending(m => Version.Parse(m.Version)).First();
        return IsNewer(newest.Version, Product.Version) ? newest : null;
    }

    // 下载到临时文件并校验长度与 SHA-256；对不上就删掉临时文件并响亮报错。
    public static async Task<string> DownloadAsync(HttpClient client, UpdateManifest manifest, string folder, IProgress<double>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "PaperFlow-" + manifest.Version + "-" + Guid.NewGuid().ToString("N") + ".download");
        // 慢不等于坏：直连 GitHub 下载常常只有几十 KB/s，所以不设总时长上限，
        // 只设"多久收不到数据就当作断了"（停顿 90 秒）与一个 60 分钟的兜底。
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(token);
        overall.CancelAfter(TimeSpan.FromMinutes(60));
        try
        {
            using (var response = await client.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, overall.Token))
            {
                if (!response.IsSuccessStatusCode) throw new InvalidDataException(Lang.F("检查更新失败：{0}", (int)response.StatusCode));
                await using var source = await response.Content.ReadAsStreamAsync(overall.Token);
                await using var destination = File.Create(target);
                var buffer = new byte[81920];
                long written = 0;
                while (true)
                {
                    int read;
                    try { read = await source.ReadAsync(buffer, overall.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(90), overall.Token); }
                    catch (TimeoutException) { throw new InvalidDataException(Lang.T("下载卡住了：很久没有收到数据。")); }
                    if (read == 0) break;
                    await destination.WriteAsync(buffer.AsMemory(0, read), token);
                    written += read;
                    progress?.Report(manifest.Length == 0 ? 0 : Math.Min(100, 100.0 * written / manifest.Length));
                }
            }
            if (new FileInfo(target).Length != manifest.Length || !Hash(target).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                throw new InvalidDataException(Lang.T("更新包校验失败，已丢弃，没有改动现在的程序。"));
            }
            return target;
        }
        catch
        {
            if (File.Exists(target)) { try { File.Delete(target); } catch (IOException) { } }
            throw;
        }
    }

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    // 安装就是把它放成“启动器认识的样子”：versions/<版本>/PaperFlow.exe 加新的 channel.json。
    // 两个文件都先写临时文件再替换，channel.json 的旧值留一份 .previous，和发布脚本一致。
    public static string Install(UpdateManifest manifest, string verifiedFile, string packageFolder)
    {
        var versionFolder = Path.Combine(packageFolder, "versions", manifest.Version);
        Directory.CreateDirectory(versionFolder);
        var executable = Path.Combine(versionFolder, "PaperFlow.exe");
        var staging = executable + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.Copy(verifiedFile, staging, true);
        if (!Hash(staging).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(staging); throw new InvalidDataException(Lang.T("更新包校验失败，已丢弃，没有改动现在的程序。")); }
        if (File.Exists(executable)) File.Replace(staging, executable, null, true); else File.Move(staging, executable);
        Field(Path.Combine(packageFolder, "channel.json"), "{\"version\":\"" + manifest.Version + "\",\"exePath\":\"versions/" + manifest.Version + "/PaperFlow.exe\",\"sha256\":\"" + manifest.Sha256 + "\",\"length\":" + manifest.Length.ToString(CultureInfo.InvariantCulture) + "}");
        try { File.Delete(verifiedFile); } catch (IOException) { }
        return executable;
    }

    private static void Field(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temporary, path, path + ".previous", true); else File.Move(temporary, path);
    }

    public static string CacheFolder()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperFlow", "update-cache");
        return local;
    }
    public static void ClearCache()
    {
        try { foreach (var file in Directory.GetFiles(CacheFolder(), "*.download")) File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
