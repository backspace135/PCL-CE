using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PCL.Horizon;

/// <summary>
/// 获取 Horizon Nightly 构建列表，下载、验签并安装到 <see cref="HorizonPaths.VersionsDir"/>，管理已安装的构建。
/// </summary>
public sealed class HorizonReleaseService
{
    public const string Repository = "horizon-rust/horizon";
    public const string UserAgent = "PCL-Horizon";
    private const int PageSize = 100;
    private const int MaxPages = 10;

    /// <summary>
    /// GitHub API 根地址，按顺序尝试。第二个是 Horizon 的公共只读镜像，路径与返回格式与 GitHub REST API 相同
    /// （2026-11-16 实测 <c>/repos/{repo}/releases?per_page=&amp;page=</c> 和 <c>/releases/tags/{tag}</c>），
    /// 在 GitHub 限流或不可达时使用；发布包本身仍从 <c>browser_download_url</c> 下载。
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultApiBases =
        ["https://api.github.com", "https://mcp.playhorizon.dev/github/public"];

    private readonly HttpClient _http;

    /// <param name="http">由调用方管理生命周期的 HttpClient。</param>
    /// <param name="assetName">要安装的发布包，默认为当前架构的 Windows 包。</param>
    public HorizonReleaseService(HttpClient http, string? assetName = null)
    {
        _http = http;
        AssetName = assetName ?? HorizonPlatform.WindowsX64;
    }

    public string AssetName { get; }

    public IReadOnlyList<string> ApiBases { get; init; } = DefaultApiBases;

    public IReadOnlyList<byte[]> TrustedKeys { get; init; } = HorizonReleaseSigning.TrustedKeys;

    /// <summary>
    /// 列出 Nightly 构建，最新的在前。其他 release（如 <c>launcher@…</c>）和标题无法严格解析的会被忽略；
    /// <c>nightly</c> 与同一构建的 <c>nightly-&lt;日期&gt;</c> 合并为一项。
    /// </summary>
    public async Task<IReadOnlyList<HorizonRelease>> ListReleasesAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<Exception>();
        foreach (var apiBase in ApiBases)
        {
            try
            {
                return Merge(await _ListFromAsync(apiBase, cancellationToken));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
                                           or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                errors.Add(new HorizonReleaseException($"{apiBase}: {ex.Message}", ex));
            }
        }
        throw new HorizonReleaseException("无法获取 Horizon 版本列表。", new AggregateException(errors));
    }

    /// <summary>当前平台的发布包；构建缺少该平台时抛出 <see cref="HorizonReleaseException"/>。</summary>
    public HorizonReleaseAsset GetPlatformAsset(HorizonRelease release) =>
        release.FindAsset(AssetName) ?? throw new HorizonReleaseException(
            $"{release.Version} 没有 {AssetName}（该平台的构建可能失败了），请选择其他版本。");

    /// <summary>
    /// 下载并安装 <paramref name="release"/>：先用 v2 签名校验 <c>SHA256SUMS</c>，再校验发布包的 SHA-256，
    /// 解压到同一根目录下的临时目录后整体改名为 <c>versions/&lt;版本&gt;/</c>。已安装时直接返回。
    /// </summary>
    public async Task<HorizonBuild> InstallAsync(
        HorizonRelease release, HorizonPaths paths, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var target = paths.BuildDir(release.Version);
        if (Directory.Exists(target)) return new HorizonBuild(target);

        var asset = GetPlatformAsset(release);
        var sumsAsset = release.FindAsset(HorizonReleaseSigning.SumsName);
        var sigAsset = release.FindAsset(HorizonReleaseSigning.SignatureV2Name);
        if (sigAsset is null)
            HorizonReleaseSigning.BuildDigest(release.Version, asset.Name, null, null, TrustedKeys);
        if (sumsAsset is null)
            throw new HorizonReleaseException($"{release.Version} 没有 {HorizonReleaseSigning.SumsName}，未安装。");

        var sums = await _GetBytesAsync(sumsAsset.DownloadUrl, cancellationToken);
        var signature = Encoding.UTF8.GetString(await _GetBytesAsync(sigAsset!.DownloadUrl, cancellationToken));
        var expected = HorizonReleaseSigning.BuildDigest(release.Version, asset.Name, sums, signature, TrustedKeys);

        Directory.CreateDirectory(paths.VersionsDir);
        var nonce = Guid.NewGuid().ToString("N")[..8];
        var downloadFile = Path.Combine(paths.VersionsDir, $".download-{nonce}.zip");
        var unpackDir = Path.Combine(paths.VersionsDir, $".unpack-{nonce}");
        try
        {
            var actual = await _DownloadAsync(asset, downloadFile, progress, cancellationToken);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new HorizonReleaseException(
                    $"{asset.Name} 与签名的校验和不符（下载损坏或被篡改），未安装。请重试。");

            cancellationToken.ThrowIfCancellationRequested();
            ZipFile.ExtractToDirectory(downloadFile, unpackDir);
            var content = _SingleTopLevelDirectory(unpackDir) ?? unpackDir;
            var info = new HorizonBuildInfo(release.Tag, release.Version, asset.Name, actual, DateTimeOffset.Now);
            info.Write(content);
            var probe = new HorizonBuild(content);
            if (!probe.IsComplete)
                throw new HorizonReleaseException(
                    $"{asset.Name} 中没有 {Path.GetFileName(probe.ClientPath)} 或 {Path.GetFileName(probe.PreparePath)}，未安装。");

            try
            {
                Directory.Move(content, target);
            }
            catch (IOException) when (Directory.Exists(target))
            {
                // 另一次安装已经完成。
            }
            progress?.Report(1);
            return new HorizonBuild(target);
        }
        finally
        {
            _TryDelete(downloadFile);
            _TryDelete(unpackDir);
        }
    }

    /// <summary>已安装的构建，最新的在前。</summary>
    public static IReadOnlyList<HorizonBuild> ListInstalled(HorizonPaths paths)
    {
        if (!Directory.Exists(paths.VersionsDir)) return [];
        return Directory.EnumerateDirectories(paths.VersionsDir)
            .Where(dir => HorizonReleaseSigning.IsBuild(Path.GetFileName(dir)))
            .Select(dir => new HorizonBuild(dir))
            .OrderByDescending(build => build.Version.Substring(0, 18), StringComparer.Ordinal)
            .ThenByDescending(build => build.Info?.InstalledAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>删除已安装的构建，可同时删除它准备的资源。先改名再删除，删到一半也不会被当成已安装。</summary>
    public static void DeleteInstalled(HorizonPaths paths, string version, bool deletePrepared = true)
    {
        _DeleteViaRename(paths.BuildDir(version), paths.VersionsDir);
        if (deletePrepared) _DeleteViaRename(paths.PreparedFor(version), paths.PreparedDir);
    }

    private async Task<List<HorizonRelease>> _ListFromAsync(string apiBase, CancellationToken cancellationToken)
    {
        var releases = new List<HorizonRelease>();
        var sawLatest = false;
        for (var page = 1; page <= MaxPages; page++)
        {
            var url = $"{apiBase.TrimEnd('/')}/repos/{Repository}/releases?per_page={PageSize}&page={page}";
            using var request = _Request(url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new JsonException("release 列表不是数组。");

            var count = 0;
            DateTimeOffset? oldest = null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                count++;
                var published = _Date(item, "published_at");
                if (published is { } p) oldest = oldest is { } o && o < p ? o : p;
                var release = _ParseRelease(item);
                if (release is null) continue;
                sawLatest |= release.Tag == HorizonReleaseSigning.NightlyTag;
                releases.Add(release);
            }
            if (count < PageSize) break;
            // 带日期的 Nightly 只保留 7 天，看到最新版且已翻过足够久远的部分就不必继续。
            if (sawLatest && oldest is { } last && last < DateTimeOffset.UtcNow.AddDays(-30)) break;
        }
        return releases;
    }

    private static HorizonRelease? _ParseRelease(JsonElement item)
    {
        var tag = _String(item, "tag_name");
        var title = _String(item, "name");
        if (tag is null || title is null || !HorizonReleaseSigning.IsNightlyTag(tag)) return null;
        if (item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        var version = HorizonReleaseSigning.BuildTitleVersion(title);
        if (version is null) return null;
        var assets = new List<HorizonReleaseAsset>();
        if (item.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var a in list.EnumerateArray())
            {
                var name = _String(a, "name");
                var url = _String(a, "browser_download_url");
                if (name is null || url is null) continue;
                var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                assets.Add(new HorizonReleaseAsset(name, size, url));
            }
        return new HorizonRelease(tag, title, version, _Date(item, "published_at"), assets);
    }

    private static List<HorizonRelease> Merge(List<HorizonRelease> releases) =>
        releases.GroupBy(r => r.Version)
            .Select(group =>
            {
                // 同一构建优先用带日期的标签：nightly 标签会被下一个构建移走。
                var chosen = group.FirstOrDefault(r => r.Tag != HorizonReleaseSigning.NightlyTag) ?? group.First();
                return chosen with { IsLatest = group.Any(r => r.Tag == HorizonReleaseSigning.NightlyTag) };
            })
            .OrderByDescending(r => r.Date, StringComparer.Ordinal)
            .ThenByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue)
            .ToList();

    private async Task<byte[]> _GetBytesAsync(string url, CancellationToken cancellationToken)
    {
        using var request = _Request(url);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<string> _DownloadAsync(
        HorizonReleaseAsset asset, string file, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var request = _Request(asset.DownloadUrl);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? asset.Size;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;
                if (total > 0) progress?.Report(Math.Min(done, total) * 0.95 / total);
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static HttpRequestMessage _Request(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        return request;
    }

    /// <summary>压缩包只含一个顶层目录时返回它，以便把它作为构建目录。</summary>
    private static string? _SingleTopLevelDirectory(string dir)
    {
        if (Directory.EnumerateFiles(dir).Any()) return null;
        var dirs = Directory.GetDirectories(dir);
        return dirs.Length == 1 ? dirs[0] : null;
    }

    private static void _DeleteViaRename(string dir, string parent)
    {
        if (!Directory.Exists(dir)) return;
        var trash = Path.Combine(parent, $".delete-{Guid.NewGuid():N}");
        Directory.Move(dir, trash);
        Directory.Delete(trash, true);
    }

    private static void _TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string? _String(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? _Date(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        value.TryGetDateTimeOffset(out var date) ? date : null;
}
