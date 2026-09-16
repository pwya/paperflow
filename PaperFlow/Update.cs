using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PaperFlow;

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
    // 永远指向“最新那个 Release 的附件”，不需要调 GitHub API，也就没有速率和账号问题。
    public const string DefaultManifestUrl = "https://github.com/pwya/paperflow/releases/latest/download/update.json";
    // 只给开发和自动化测试用：把清单地址指到本地，方便端到端验证整条更新链。
    public static string ManifestUrl { get; set; } = DefaultManifestUrl;
    public static bool UsingOverride => ManifestUrl != DefaultManifestUrl;
    // 设置窗口手动检查到的版本，交给挂件去显示提示条和下载按钮。
    public static UpdateManifest? Offered { get; set; }

    public static HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = true, UseCookies = false })
    {
        Timeout = TimeSpan.FromMinutes(10),
        // 只报自己的名字和版本；不带设备标识、不带论文信息、不带 Cookie。
        DefaultRequestHeaders = { { "User-Agent", "PaperFlow/" + Product.Version }, { "Accept", "application/json" } }
    };

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
        using var response = await client.GetAsync(ManifestUrl, token);
        if (!response.IsSuccessStatusCode) throw new InvalidDataException(Lang.F("检查更新失败：{0}", (int)response.StatusCode));
        var manifest = UpdateManifest.Parse(await response.Content.ReadAsStringAsync(token), UsingOverride);
        return IsNewer(manifest.Version, Product.Version) ? manifest : null;
    }

    // 下载到临时文件并校验长度与 SHA-256；对不上就删掉临时文件并响亮报错。
    public static async Task<string> DownloadAsync(HttpClient client, UpdateManifest manifest, string folder, IProgress<double>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "PaperFlow-" + manifest.Version + "-" + Guid.NewGuid().ToString("N") + ".download");
        try
        {
            using (var response = await client.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                if (!response.IsSuccessStatusCode) throw new InvalidDataException(Lang.F("检查更新失败：{0}", (int)response.StatusCode));
                await using var source = await response.Content.ReadAsStreamAsync(token);
                await using var destination = File.Create(target);
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
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
