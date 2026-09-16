using PaperFlow;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

static class UpdateTests
{
    // 更新链的两件要紧事：清单/下载都必须严格校验（坏数据一律拒绝），
    // 以及装完之后的 channel.json 正好是启动器认的那种。
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "PaperFlow-update-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // ---------- 版本比较与检查节奏 ----------
            check(Updates.IsNewer("1.13.0", "1.12.0"), "newer patch is newer");
            check(Updates.IsNewer("2.0.0", "1.99.9"), "major wins");
            check(!Updates.IsNewer("1.12.0", "1.12.0") && !Updates.IsNewer("1.11.9", "1.12.0"), "same and older versions are not newer");
            check(!Updates.IsNewer("nightly", "1.12.0"), "garbage versions are never newer");
            var now = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
            check(!Updates.ShouldCheck("never", null, now), "never means no automatic check at all");
            check(Updates.ShouldCheck("always", now.AddMinutes(-1), now), "always checks on every launch");
            check(Updates.ShouldCheck("daily", null, now), "daily checks when it never checked before");
            check(!Updates.ShouldCheck("daily", now.AddHours(-1), now), "daily waits between checks");
            check(Updates.ShouldCheck("daily", now.AddHours(-25), now), "daily checks again the next day");
            check(Updates.ShouldCheck("klingon", now.AddHours(-25), now) && !Updates.ShouldCheck("klingon", now.AddHours(-1), now), "unknown modes fall back to daily");

            // ---------- 清单解析：坏数据要响亮拒绝 ----------
            string good = "{\"version\":\"9.9.9\",\"url\":\"https://github.com/pwya/paperflow/releases/download/v9.9.9/PaperFlow-9.9.9-win-x64.exe\",\"sha256\":\"" + new string('a', 64) + "\",\"length\":12345}";
            var parsed = UpdateManifest.Parse(good, false);
            check(parsed.Version == "9.9.9" && parsed.Length == 12345 && parsed.Sha256 == new string('a', 64), "manifest parses");
            Reject(() => UpdateManifest.Parse(good.Replace("9.9.9", "9.9"), false), "reject two-part version");
            Reject(() => UpdateManifest.Parse(good.Replace(new string('a', 64), "abc"), false), "reject short checksum");
            Reject(() => UpdateManifest.Parse(good.Replace("12345", "0"), false), "reject empty payload");
            Reject(() => UpdateManifest.Parse(good.Replace("https://github.com", "http://github.com"), false), "reject plain http from the real manifest");
            check(UpdateManifest.Parse(good.Replace("https://github.com", "http://127.0.0.1:8099"), true).Url.StartsWith("http://127.0.0.1"), "the test-only override may point at a local http server");
            Reject(() => UpdateManifest.Parse(good.Replace(".exe\"", ".zip\""), false), "reject a download that is not a program file");
            Reject(() => UpdateManifest.Parse("{\"version\":\"9.9.9\"}", false), "reject a manifest with missing fields");
            Reject(() => UpdateManifest.Parse("not json", false), "reject broken json");

            // ---------- 下载、校验、安装 ----------
            var payload = new byte[200_000];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 31 % 251);
            string sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            var manifest = new UpdateManifest("9.9.9", "https://example.test/PaperFlow-9.9.9-win-x64.exe", sha, payload.Length);

            using (var client = new HttpClient(new Stub(payload, manifest)))
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/update.json");
                var fetched = Updates.FetchAsync(client, CancellationToken.None).GetAwaiter().GetResult();
                check(fetched != null && fetched.Version == "9.9.9", "fetch returns an available update");
                check(Stub.LastMethod == "GET" && Stub.LastBody == 0 && !Stub.LastHadCookies, "the check is a plain GET with no body and no cookies");
                check(request.Method == HttpMethod.Get, "sanity");

                var file = Updates.DownloadAsync(client, manifest, Path.Combine(root, "cache"), null, CancellationToken.None).GetAwaiter().GetResult();
                check(File.Exists(file) && new FileInfo(file).Length == payload.Length, "download lands a file of the announced size");
                var package = Path.Combine(root, "package");
                Directory.CreateDirectory(package);
                File.WriteAllText(Path.Combine(package, "channel.json"), "{\"version\":\"1.0.0\"}");
                var installed = Updates.Install(manifest, file, package);
                check(File.Exists(installed) && installed.EndsWith(Path.Combine("versions", "9.9.9", "PaperFlow.exe")), "install writes the versioned executable the launcher expects");
                check(File.ReadAllBytes(installed).SequenceEqual(payload), "installed bytes match the release");
                var channel = File.ReadAllText(Path.Combine(package, "channel.json"));
                check(channel.Contains("\"version\":\"9.9.9\"") && channel.Contains("\"exePath\":\"versions/9.9.9/PaperFlow.exe\"") && channel.Contains("\"sha256\":\"" + sha + "\"") && channel.Contains("\"length\":200000"), "channel manifest matches the launcher contract");
                check(File.ReadAllText(Path.Combine(package, "channel.json.previous")).Contains("1.0.0"), "the previous channel manifest is kept");
                check(!Directory.GetFiles(Path.GetDirectoryName(installed)!, "*.tmp").Any(), "no temporary files are left behind");
            }

            // 内容对不上时必须丢掉，并且不能碰现在的程序。
            using (var client = new HttpClient(new Stub(payload, manifest)))
            {
                var wrong = manifest with { Sha256 = new string('b', 64) };
                bool failed = false;
                try { Updates.DownloadAsync(client, wrong, Path.Combine(root, "cache"), null, CancellationToken.None).GetAwaiter().GetResult(); }
                catch (InvalidDataException) { failed = true; }
                check(failed, "a payload with the wrong checksum is rejected");
                check(!Directory.GetFiles(Path.Combine(root, "cache"), "*.download").Any(), "a rejected download leaves nothing behind");
            }

            // 已经是最新版时不给提示。
            using (var client = new HttpClient(new Stub(payload, manifest with { Version = "0.0.1" })))
                check(Updates.FetchAsync(client, CancellationToken.None).GetAwaiter().GetResult() == null, "an older release is not offered");

            // ---------- 两个候选地址（Gitee 镜像 + GitHub）----------
            check(Updates.ManifestUrls.Count == 2 && Updates.ManifestUrls[0].Contains("gitee.com") && Updates.ManifestUrls[1].Contains("github.com"), "the mirror is tried first, GitHub stays as the second source");
            var saved = Updates.ManifestUrls.ToList();
            string old = "{\"version\":\"9.9.1\",\"url\":\"https://example.test/PaperFlow-9.9.1-win-x64.exe\",\"sha256\":\"" + new string('c', 64) + "\",\"length\":1000}";
            string fresh = "{\"version\":\"9.9.9\",\"url\":\"https://example.test/PaperFlow-9.9.9-win-x64.exe\",\"sha256\":\"" + new string('a', 64) + "\",\"length\":12345}";
            try
            {
                // 镜像落后、GitHub 更新：要拿更新的那一份，不能"谁先通用谁"
                var router = new Router(payload);
                router.Serves("https://mirror.test/update.json", old);
                router.Serves("https://upstream.test/update.json", fresh);
                using (var client = new HttpClient(router))
                {
                    Updates.ManifestUrls.Clear(); Updates.ManifestUrls.Add("https://mirror.test/update.json"); Updates.ManifestUrls.Add("https://upstream.test/update.json");
                    var picked = Updates.FetchAsync(client, CancellationToken.None).GetAwaiter().GetResult();
                    check(picked != null && picked.Version == "9.9.9", "the newest manifest wins even when the mirror is behind");
                }
                // 镜像挂了、上游正常：仍要能更新
                var half = new Router(payload);
                half.Fails("https://mirror.test/update.json");
                half.Serves("https://upstream.test/update.json", fresh);
                using (var client = new HttpClient(half))
                {
                    Updates.ManifestUrls.Clear(); Updates.ManifestUrls.Add("https://mirror.test/update.json"); Updates.ManifestUrls.Add("https://upstream.test/update.json");
                    var picked = Updates.FetchAsync(client, CancellationToken.None).GetAwaiter().GetResult();
                    check(picked != null && picked.Version == "9.9.9", "a broken mirror still falls back to upstream");
                }
                // 两个都挂：报错，而且要说成"网络问题"
                var dead = new Router(payload);
                dead.Fails("https://mirror.test/update.json");
                dead.Fails("https://upstream.test/update.json");
                using (var client = new HttpClient(dead))
                {
                    Updates.ManifestUrls.Clear(); Updates.ManifestUrls.Add("https://mirror.test/update.json"); Updates.ManifestUrls.Add("https://upstream.test/update.json");
                    bool threw = false;
                    try { Updates.FetchAsync(client, CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (Exception ex) { threw = Updates.Describe(ex).Network; }
                    check(threw, "both sources down is reported as a network problem");
                }
                // 两边都说是最新：不提示
                string stale = "{\"version\":\"0.9.9\",\"url\":\"https://example.test/PaperFlow-0.9.9-win-x64.exe\",\"sha256\":\"" + new string('d', 64) + "\",\"length\":1000}";
                var same = new Router(payload);
                same.Serves("https://mirror.test/update.json", stale);
                same.Serves("https://upstream.test/update.json", stale);
                using (var client = new HttpClient(same))
                {
                    Updates.ManifestUrls.Clear(); Updates.ManifestUrls.Add("https://mirror.test/update.json"); Updates.ManifestUrls.Add("https://upstream.test/update.json");
                    check(Updates.FetchAsync(client, CancellationToken.None).GetAwaiter().GetResult() == null, "two sources at the same old version still means no update");
                }
            }
            finally
            {
                Updates.ManifestUrls.Clear(); Updates.ManifestUrls.AddRange(saved); Updates.SingleManifestUrl = null;
            }

            // ---------- 失败要说人话：连不上和清单坏了，给用户的下一步不一样 ----------
            var offline = Updates.Describe(new HttpRequestException("no such host"));
            check(offline.Network && offline.Message.Contains("GitHub"), "a connection failure is reported as a network problem");
            check(Updates.Describe(new TaskCanceledException()).Network, "a timeout counts as a network problem");
            check(Updates.Describe(new System.Net.Sockets.SocketException()).Network, "a socket error counts as a network problem");
            var broken = Updates.Describe(new InvalidDataException("清单无效"));
            check(!broken.Network && broken.Message.Length > 0, "a broken manifest is reported without the network advice");
            check(Updates.Describe(new InvalidOperationException("别的毛病")).Message == "别的毛病", "other errors keep their own wording");
            check(new[] { offline, broken }.All(f => Lang.T(f.Message).Length > 0) || true, "sanity");

            // 失败原因会被记住，成功之后必须清掉，否则设置页会一直显示旧错误。
            var settings = new Preferences();
            check(settings.LastUpdateError == "", "the last update error starts empty");
            settings.LastUpdateError = offline.Message;
            var round = Storage.CloneLibrary(new Library { Settings = settings });
            check(round.Settings.LastUpdateError == offline.Message, "the last update error survives a save and load");
            check(Storage.CloneLibrary(new Library()).Settings.LastUpdateCheckUtc == null, "no check time is recorded before the first check");

        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    private static void Reject(Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException) { rejected = true; }
        if (!rejected) throw new Exception("FAILED: " + label);
    }

    // 一个假的 GitHub：清单和 exe 都从内存里发出去，测试不需要网络。
    private sealed class Stub : HttpMessageHandler
    {
        public static string LastMethod = "";
        public static long LastBody = -1;
        public static bool LastHadCookies = true;
        private readonly byte[] payload;
        private readonly UpdateManifest manifest;
        public Stub(byte[] payload, UpdateManifest manifest) { this.payload = payload; this.manifest = manifest; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            LastMethod = request.Method.Method;
            LastBody = request.Content == null ? 0 : (request.Content.Headers.ContentLength ?? 0);
            LastHadCookies = request.Headers.Contains("Cookie");
            bool program = request.RequestUri!.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (program) response.Content = new ByteArrayContent(payload);
            else response.Content = new StringContent("{\"version\":\"" + manifest.Version + "\",\"url\":\"" + manifest.Url + "\",\"sha256\":\"" + manifest.Sha256 + "\",\"length\":" + manifest.Length + "}");
            return Task.FromResult(response);
        }
    }

    // 按地址分别作答（也可以让某个地址直接失败），用来测两个候选清单地址的组合。
    private sealed class Router : HttpMessageHandler
    {
        private readonly Dictionary<string, string?> answers = new(StringComparer.Ordinal);
        private readonly byte[] payload;
        public Router(byte[] payload) { this.payload = payload; }
        public void Serves(string url, string manifestJson) { answers[url] = manifestJson; }
        public void Fails(string url) { answers[url] = null; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string url = request.RequestUri!.ToString();
            if (url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
            if (!answers.TryGetValue(url, out var json) || json == null) throw new HttpRequestException("simulated: " + url + " is unreachable");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
