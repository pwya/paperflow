using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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
// 现在下载的是一个 zip（里面是 versions/<版本>/PaperFlow.exe）：Sha256/Length 是压缩包的，
// ExeSha256/ExeLength 是解压出来的那个程序文件的。老格式（url 直接指向 exe）仍然读得懂：
// 那时 ExeSha256/ExeLength 就等于 Sha256/Length。
public sealed record UpdateManifest(string Version, string Url, string Sha256, long Length, string ExeSha256, long ExeLength, string Notes = "", string NotesEn = "")
{
    // 更新说明就是 CHANGELOG 里这一版的小节，发布脚本抽出来写进清单——程序里看到的和
    // Release 页面上看到的是同一段字。中英各一份，英文没写就退回中文。
    public const int MaxNotesLength = 4000;
    public string NotesFor(bool english) => english && NotesEn.Length > 0 ? NotesEn : Notes;
    public bool IsArchive => Url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
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
        bool archive = uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        if (!archive && !uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Lang.T("更新清单的下载地址不是程序文件。"));
        string exeSha = root.TryGetProperty("exeSha256", out var rawExe) && rawExe.ValueKind == JsonValueKind.String ? rawExe.GetString()!.Trim().ToLowerInvariant() : "";
        long exeLength = root.TryGetProperty("exeLength", out var rawExeLength) && rawExeLength.TryGetInt64(out var parsedExeLength) ? parsedExeLength : 0;
        if (archive)
        {
            if (!Regex.IsMatch(exeSha, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException(Lang.T("更新清单的校验值无效。"));
            if (exeLength < 1 || exeLength > 300_000_000) throw new InvalidDataException(Lang.T("更新清单的文件大小无效。"));
        }
        else { exeSha = sha.ToLowerInvariant(); exeLength = length; }
        string notes = Text(root, "notes"), notesEn = Text(root, "notesEn");
        if (notes.Length > MaxNotesLength || notesEn.Length > MaxNotesLength) throw new InvalidDataException(Lang.T("更新清单里的更新说明太长。"));
        return new UpdateManifest(version, url, sha.ToLowerInvariant(), length, exeSha, exeLength, notes, notesEn);
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
        var found = new List<(string Url, UpdateManifest Manifest)>();
        Exception? last = null;
        foreach (var url in Candidates())
        {
            try
            {
                using var response = await client.GetAsync(url, token);
                if (!response.IsSuccessStatusCode) throw new InvalidDataException(Lang.F("检查更新失败：{0}", (int)response.StatusCode));
                found.Add((url, UpdateManifest.Parse(await response.Content.ReadAsStringAsync(token), UsingOverride)));
            }
            catch (Exception ex) { last = ex; }
        }
        if (found.Count == 0) throw last ?? new InvalidDataException(Lang.T("更新清单读不出来。"));
        var pick = Choose(found, GiteeManifestUrl, DefaultManifestUrl);
        return IsNewer(pick.Version, Product.Version) ? pick : null;
    }

    // 两个来源都在时怎么定：
    // - 镜像**不许抢先**：它报的版本比上游高就忽略它（镜像只能落后，不能领头），
    //   这样即使 Gitee 那边被人动了手脚，也骗不到比上游更高的版本；
    // - 两边同版本：必须**哈希和长度完全一致**才用镜像那份（用它是为了走国内下载），
    //   对不上说明有人改过其中一份，宁可这次不更新；
    // - 只有镜像能读到（国内连不上 GitHub）：用镜像，这一条是刻意的取舍，写在文档里。
    public static UpdateManifest Choose(IReadOnlyList<(string Url, UpdateManifest Manifest)> found, string mirrorUrl, string upstreamUrl)
    {
        var mirror = found.FirstOrDefault(f => f.Url == mirrorUrl).Manifest;
        var upstream = found.FirstOrDefault(f => f.Url == upstreamUrl).Manifest;
        if (mirror != null && upstream != null)
        {
            int order = Version.Parse(mirror.Version).CompareTo(Version.Parse(upstream.Version));
            if (order > 0) return upstream;   // 镜像不许抢先
            if (order < 0) return upstream;   // 上游更新
            if (!string.Equals(mirror.Sha256, upstream.Sha256, StringComparison.OrdinalIgnoreCase) || mirror.Length != upstream.Length
                || !string.Equals(mirror.ExeSha256, upstream.ExeSha256, StringComparison.OrdinalIgnoreCase) || mirror.ExeLength != upstream.ExeLength)
                throw new InvalidDataException(Lang.T("两个更新来源对同一个版本给的文件不一样，这次先不更新。"));
            return mirror;                    // 同版本同哈希：用镜像（走国内下载）
        }
        return upstream ?? mirror ?? found.OrderByDescending(f => Version.Parse(f.Manifest.Version)).First().Manifest;
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
            // 压缩包：从里面取出 versions/<版本>/PaperFlow.exe，再按清单里的 exeSha256/exeLength 校验一次。
            if (!manifest.IsArchive) return target;
            var extracted = target + ".exe";
            try
            {
                using (var archive = System.IO.Compression.ZipFile.OpenRead(target))
                {
                    string wanted = "versions/" + manifest.Version + "/PaperFlow.exe";
                    var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), wanted, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException(Lang.T("更新包里没有找到程序文件。"));
                    if (entry.Length != manifest.ExeLength) throw new InvalidDataException(Lang.T("更新包里的程序文件大小不对。"));
                    entry.ExtractToFile(extracted, true);
                }
                if (new FileInfo(extracted).Length != manifest.ExeLength || !Hash(extracted).Equals(manifest.ExeSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(Lang.T("更新包里的程序文件校验失败。"));
            }
            catch
            {
                if (File.Exists(extracted)) { try { File.Delete(extracted); } catch (IOException) { } }
                throw;
            }
            finally { try { File.Delete(target); } catch (IOException) { } }
            return extracted;
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
        // channel.json 是启动器要读的，里面必须是那个程序文件自己的哈希与长度。
        if (!Hash(staging).Equals(manifest.ExeSha256, StringComparison.OrdinalIgnoreCase) || new FileInfo(staging).Length != manifest.ExeLength)
        { File.Delete(staging); throw new InvalidDataException(Lang.T("更新包校验失败，已丢弃，没有改动现在的程序。")); }
        if (File.Exists(executable)) File.Replace(staging, executable, null, true); else File.Move(staging, executable);
        Field(Path.Combine(packageFolder, "channel.json"), "{\"version\":\"" + manifest.Version + "\",\"exePath\":\"versions/" + manifest.Version + "/PaperFlow.exe\",\"sha256\":\"" + manifest.ExeSha256 + "\",\"length\":" + manifest.ExeLength.ToString(CultureInfo.InvariantCulture) + "}");
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
