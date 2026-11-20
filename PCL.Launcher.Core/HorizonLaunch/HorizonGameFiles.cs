using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PCL.Core.App.Localization;
using PCL.Horizon;
using PCL.Network;

namespace PCL;

/// <summary>
///     为 <c>horizon-prepare</c> 下载正版玩家自己的原版文件：版本 JSON、客户端、依赖库、资源与服务端 jar。
///     文件放在 <see cref="HorizonPaths.MinecraftDir" />，布局与官方启动器相同，可直接作为 <c>--minecraft-dir</c>。
///     只应在已确认账户拥有 Java 版之后调用。
/// </summary>
public static class HorizonGameFiles
{
    /// <summary>
    ///     确保版本 JSON 存在并返回其内容；本地文件缺失、损坏或与 Mojang 发布的不一致时从版本列表获取地址后下载。
    ///     horizon-prepare 按 sha1 校验版本 JSON，所以文件必须按原始字节保存，不能重新格式化。
    /// </summary>
    public static JsonObject EnsureVersionJson(HorizonPaths paths, string minecraft)
    {
        var jsonPath = paths.MinecraftVersionJson(minecraft);
        // 拿不到版本列表（如离线）时信任本地文件
        var url = GameDownload.DlClientListGet(minecraft) as string;
        var sha1 = url is null ? null : _Sha1FromUrl(url);
        if (_TryReadJson(jsonPath) is { } existing && (string?)existing["id"] == minecraft &&
            (sha1 is null || _Sha1Of(jsonPath) == sha1))
            return existing;

        if (url is null)
            throw new Exception(Lang.Text("Horizon.Files.Error.VersionNotFound", minecraft));
        LauncherCommon.Log($"[Horizon] 下载 Minecraft {minecraft} 版本 JSON：{url}");
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        var temp = jsonPath + ".download";
        Exception? lastException = null;
        foreach (var source in GameDownload.DlSourceLauncherOrMetaGet(url))
        {
            try
            {
                File.Delete(temp);
                FileDownloader.DownloadAsync(source, temp, enableParallelChunks: false).GetAwaiter().GetResult();
                if (sha1 is not null && _Sha1Of(temp) != sha1)
                    throw new InvalidDataException($"版本 JSON 的 sha1 与 {sha1} 不符：{source}");
                var json = LauncherCommon.GetJson(File.ReadAllText(temp)) as JsonObject
                    ?? throw new Exception(Lang.Text("Horizon.Files.Error.VersionJsonInvalid", minecraft));
                File.Move(temp, jsonPath, true);
                return json;
            }
            catch (Exception ex)
            {
                lastException = ex;
                LauncherCommon.Log(ex, $"[Horizon] 从 {source} 下载版本 JSON 失败，尝试下一个源");
            }
        }

        File.Delete(temp);
        throw new Exception(Lang.Text("Horizon.Files.Error.VersionJsonInvalid", minecraft), lastException);
    }

    /// <summary>
    ///     Mojang 的版本 JSON 地址形如 <c>.../v1/packages/&lt;sha1&gt;/&lt;版本&gt;.json</c>，取出其中的 sha1；其他地址返回
    ///     <see langword="null" />。
    /// </summary>
    private static string? _Sha1FromUrl(string url)
    {
        var match = Regex.Match(url, "/packages/([0-9a-f]{40})/");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string _Sha1Of(string path) => Convert.ToHexStringLower(SHA1.HashData(File.ReadAllBytes(path)));

    /// <summary>
    ///     版本 JSON 中要求的 Java 组件名（如 <c>java-runtime-epsilon</c>），主版本与 <paramref name="javaMajor" /> 不符时为
    ///     <see langword="null" />。
    /// </summary>
    public static string? GetJavaComponent(JsonObject versionJson, int javaMajor)
    {
        var javaVersion = versionJson["javaVersion"];
        return javaVersion?["majorVersion"]?.GetValue<int>() == javaMajor
            ? javaVersion["component"]?.GetValue<string>()
            : null;
    }

    /// <summary>
    ///     缺失或损坏的客户端 jar、服务端 jar、依赖库与资源索引。
    /// </summary>
    public static List<DownloadFile> CollectCoreFiles(HorizonPaths paths, string minecraft, JsonObject versionJson)
    {
        var files = new List<DownloadFile>();

        var client = versionJson["downloads"]?["client"]
            ?? throw new Exception(Lang.Text("Horizon.Files.Error.NoClientDownload", minecraft));
        _AddIfMissing(files, client, paths.MinecraftClientJar(minecraft), GameDownload.DlSourceLauncherOrMetaGet);

        // 服务端 jar 只在第一次准备时需要，官方启动器不保存它，由我们为正版玩家下载
        var server = versionJson["downloads"]?["server"]
            ?? throw new Exception(Lang.Text("Horizon.Files.Error.NoServerDownload", minecraft));
        _AddIfMissing(files, server, paths.MinecraftServerJar(minecraft), GameDownload.DlSourceLauncherOrMetaGet);

        foreach (var library in (versionJson["libraries"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (!_IsAllowedOnWindows(library["rules"] as JsonArray))
                continue;
            var artifact = library["downloads"]?["artifact"];
            var path = artifact?["path"]?.GetValue<string>();
            if (artifact is null || string.IsNullOrEmpty(path))
                continue;
            _AddIfMissing(
                files,
                artifact,
                Path.Combine(paths.MinecraftLibrariesDir, path.Replace('/', Path.DirectorySeparatorChar)),
                GameDownload.DlSourceLibraryGet);
        }

        var assetIndex = versionJson["assetIndex"]
            ?? throw new Exception(Lang.Text("Horizon.Files.Error.NoAssetIndex", minecraft));
        _AddIfMissing(
            files,
            assetIndex,
            _AssetIndexPath(paths, assetIndex),
            GameDownload.DlSourceLauncherOrMetaGet);

        return files;
    }

    /// <summary>
    ///     资源索引中缺失的资源文件。以文件大小判断是否已存在，内容由 <c>horizon-prepare</c> 按锁定的哈希再次校验。
    /// </summary>
    public static List<DownloadFile> CollectAssetObjects(HorizonPaths paths, string minecraft, JsonObject versionJson)
    {
        var assetIndex = versionJson["assetIndex"]
            ?? throw new Exception(Lang.Text("Horizon.Files.Error.NoAssetIndex", minecraft));
        var index = _TryReadJson(_AssetIndexPath(paths, assetIndex))
            ?? throw new Exception(Lang.Text("Horizon.Files.Error.AssetIndexInvalid", minecraft));

        var files = new List<DownloadFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, value) in index["objects"] as JsonObject ?? [])
        {
            var hash = value?["hash"]?.GetValue<string>();
            var size = value?["size"]?.GetValue<long>() ?? -1;
            if (hash is not { Length: 40 } || !seen.Add(hash))
                continue;
            var local = Path.Combine(paths.MinecraftAssetsDir, "objects", hash[..2], hash);
            var info = new FileInfo(local);
            if (info.Exists && (size < 0 || info.Length == size))
                continue;
            files.Add(new DownloadFile(
                GameDownload.DlSourceAssetsGet($"https://resources.download.minecraft.net/{hash[..2]}/{hash}"),
                local,
                new FileChecker(actualSize: size, hash: hash)));
        }

        return files;
    }

    private static string _AssetIndexPath(HorizonPaths paths, JsonNode assetIndex)
    {
        var id = assetIndex["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new Exception(Lang.Text("Horizon.Files.Error.AssetIndexInvalid", id ?? ""));
        return Path.Combine(paths.MinecraftAssetsDir, "indexes", id + ".json");
    }

    private static void _AddIfMissing(
        List<DownloadFile> files,
        JsonNode download,
        string localPath,
        Func<string, IEnumerable<string>> mirrors)
    {
        var url = download["url"]?.GetValue<string>();
        if (string.IsNullOrEmpty(url))
            return;
        var checker = new FileChecker(
            actualSize: download["size"]?.GetValue<long>() ?? -1,
            hash: download["sha1"]?.GetValue<string>());
        if (checker.Check(localPath) is null)
            return;
        files.Add(new DownloadFile(mirrors(url), localPath, checker));
    }

    /// <summary>
    ///     按官方启动器的规则判断依赖库在 Windows 上是否需要：没有规则即需要；否则按顺序应用匹配的规则。
    ///     带 <c>features</c> 的规则只用于启动参数，这里视为不匹配。
    /// </summary>
    private static bool _IsAllowedOnWindows(JsonArray? rules)
    {
        if (rules is null || rules.Count == 0)
            return true;
        var allowed = false;
        foreach (var rule in rules.OfType<JsonObject>())
        {
            if (rule["features"] is not null)
                continue;
            var os = rule["os"];
            var osName = os?["name"]?.GetValue<string>();
            var osArch = os?["arch"]?.GetValue<string>();
            if (osName is not null && osName != "windows")
                continue;
            if (osArch is not null && osArch != (Environment.Is64BitOperatingSystem ? "x64" : "x86"))
                continue;
            allowed = rule["action"]?.GetValue<string>() == "allow";
        }

        return allowed;
    }

    private static JsonObject? _TryReadJson(string path)
    {
        try
        {
            return File.Exists(path) ? LauncherCommon.GetJson(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(ex, $"[Horizon] 读取 {path} 失败，将重新下载");
            return null;
        }
    }
}
