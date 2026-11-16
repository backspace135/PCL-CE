using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PCL.Horizon;

/// <summary>注册表中一个版本的文件。<see cref="Signature"/> 在项目有密钥时为 <c>.minisig</c> 文本。</summary>
public sealed record HorizonRegistryFile(string Name, string Url, long Size, string Sha256, string? Signature);

/// <summary>注册表中项目的一个版本。<see cref="Api"/> 对模组是 API 线，对光影是光影 API 线。</summary>
public sealed record HorizonRegistryVersion(
    string Version, string? Published, string? Changelog, string? Api, string? Side, string? Game,
    HorizonRegistryFile File);

/// <summary>注册表中的一个项目。<see cref="Versions"/> 新的在前。</summary>
public sealed record HorizonRegistryProject(
    string Id, string Kind, string Name, string Summary, string? Description, IReadOnlyList<string> Authors,
    string? License, string? Source, string? Icon, string? Key, long Downloads, long Stars, string? Updated,
    IReadOnlyList<string> Categories, IReadOnlyList<string> Tags, IReadOnlyList<HorizonRegistryVersion> Versions);

/// <summary>被撤下的文件：<see cref="Sha256"/> 为空时该项目的所有版本都被撤下。</summary>
public sealed record HorizonRegistryRevoked(string Id, string? Sha256, string Reason, string? Date);

/// <summary>注册表索引、校验或安装失败。</summary>
public class HorizonRegistryException : Exception
{
    public HorizonRegistryException(string message) : base(message) { }
    public HorizonRegistryException(string message, Exception? inner) : base(message, inner) { }
}

/// <summary>
/// Horizon 内容注册表（<c>horizon-rust/horizon-mods</c> 的 <c>index.json</c>，schema 1）：读取索引，
/// 按构建的 <c>content</c> 判断兼容性，并在安装前做与 <c>horizon_mod_package::index::check_file</c> 相同的检查
/// （来源地址、大小、SHA-256、minisign 签名、撤下列表、包内清单）。
/// </summary>
public sealed class HorizonRegistry
{
    public const string IndexUrl = "https://horizon-rust.github.io/horizon-mods/index.json";
    public const int SupportedSchema = 1;
    public const long MaxFileSize = 64L * 1024 * 1024;

    public const string KindMod = "mod";
    public const string KindNativeMod = "native-mod";
    public const string KindModpack = "modpack";
    public const string KindResourcePack = "resourcepack";
    public const string KindShader = "shader";
    public const string KindShaderPack = "shaderpack";
    public const string KindDatapack = "datapack";

    private static readonly Regex IdPattern = new("^[a-z0-9_]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    private HorizonRegistry(IReadOnlyList<HorizonRegistryProject> projects, IReadOnlyList<HorizonRegistryRevoked> revoked)
    {
        Projects = projects;
        Revoked = revoked;
    }

    public IReadOnlyList<HorizonRegistryProject> Projects { get; }
    public IReadOnlyList<HorizonRegistryRevoked> Revoked { get; }

    /// <summary>下载并解析索引。</summary>
    public static async Task<HorizonRegistry> FetchAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, IndexUrl);
        request.Headers.UserAgent.ParseAdd(HorizonReleaseService.UserAgent);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// 解析索引。schema 更高时抛出异常；不符合规则的版本被跳过，没有版本的项目、重复 id 的项目一并跳过。
    /// </summary>
    public static HorizonRegistry Parse(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new HorizonRegistryException("注册表索引不是 JSON 对象。");
        }
        catch (JsonException ex)
        {
            throw new HorizonRegistryException("注册表索引不是有效的 JSON。", ex);
        }

        var schema = _Int(root["schema"]) ?? 0;
        if (schema > SupportedSchema)
            throw new HorizonRegistryException($"注册表索引的 schema {schema} 比启动器支持的更新，请更新启动器。");

        var projects = new List<HorizonRegistryProject>();
        foreach (var node in root["projects"] as JsonArray ?? [])
            if (node is JsonObject o && _ParseProject(o) is { } project)
                projects.Add(project);
        var duplicates = projects.GroupBy(p => p.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        projects.RemoveAll(p => duplicates.Contains(p.Id));

        var revoked = new List<HorizonRegistryRevoked>();
        foreach (var node in root["revoked"] as JsonArray ?? [])
        {
            if (node is not JsonObject o || _String(o["id"]) is not { } id) continue;
            var sha = _String(o["sha256"])?.ToLowerInvariant();
            revoked.Add(new HorizonRegistryRevoked(id, sha, _String(o["reason"]) ?? "", _String(o["date"])));
        }
        return new HorizonRegistry(projects, revoked);
    }

    /// <summary>该版本被撤下的原因，没有被撤下时为 <see langword="null"/>。</summary>
    public string? RevokedReason(string projectId, string sha256) =>
        Revoked.FirstOrDefault(r => r.Id == projectId && (r.Sha256 is null || r.Sha256 == sha256.ToLowerInvariant()))
            ?.Reason;

    /// <summary>任何项目中 SHA-256 为 <paramref name="sha256"/> 的文件被撤下的原因。</summary>
    public string? RevokedReasonForFile(string sha256)
    {
        sha256 = sha256.ToLowerInvariant();
        var direct = Revoked.FirstOrDefault(r => r.Sha256 == sha256);
        if (direct is not null) return direct.Reason;
        foreach (var project in Projects)
            if (project.Versions.Any(v => v.File.Sha256 == sha256))
                return RevokedReason(project.Id, sha256);
        return null;
    }

    /// <summary>
    /// 这个构建能否加载该版本：游戏版本相同，模组的 API 线在 <c>content.mod.api</c> 中，光影的在 <c>content.shader.api</c> 中。
    /// 原生模组需要重新构建游戏，启动器不提供。
    /// </summary>
    public static bool IsCompatible(HorizonRegistryProject project, HorizonRegistryVersion version, HorizonLaunchApi client)
    {
        if (!IsInstallableKind(project.Kind)) return false;
        if (version.Game is not null && client.Minecraft is not null && version.Game != client.Minecraft) return false;
        return project.Kind switch
        {
            KindMod => _ContentApis(client, "mod").Contains(version.Api ?? ""),
            KindShader => _ContentApis(client, "shader").Contains(version.Api ?? ""),
            _ => true
        };
    }

    /// <summary>启动器能安装的种类：内容模组、Horizon 光影、shaders 光影包、资源包和数据包。</summary>
    public static bool IsInstallableKind(string kind) =>
        kind is KindMod or KindShader or KindShaderPack or KindResourcePack or KindDatapack;

    /// <summary>该种类在实例中的目标文件夹；数据包需要指定存档。</summary>
    public static string TargetFolder(HorizonInstance instance, string kind, string? world = null) => kind switch
    {
        KindMod => instance.ModsDir,
        KindShader or KindShaderPack => instance.ShaderPacksDir,
        KindResourcePack => instance.ResourcePacksDir,
        KindDatapack => world is null
            ? throw new ArgumentException("安装数据包需要指定存档。", nameof(world))
            : instance.DatapacksDir(world),
        _ => throw new HorizonRegistryException($"启动器还不能安装 {kind} 类型的内容。")
    };

    /// <summary>下载地址是否为注册表的 release，或项目自己仓库的 release 资源。</summary>
    public static bool IsAllowedUrl(HorizonRegistryProject project, HorizonRegistryFile file)
    {
        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host != "github.com")
            return false;
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        // /<owner>/<repo>/releases/download/<tag>/<name>
        if (segments.Length != 6 || segments[2] != "releases" || segments[3] != "download" ||
            Uri.UnescapeDataString(segments[5]) != file.Name)
            return false;
        var repo = $"{segments[0]}/{segments[1]}";
        if (string.Equals(repo, "horizon-rust/horizon-mods", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!Uri.TryCreate(project.Source, UriKind.Absolute, out var source) || source.Host != "github.com")
            return false;
        var sourceRepo = source.AbsolutePath.Trim('/');
        if (sourceRepo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            sourceRepo = sourceRepo[..^4];
        return string.Equals(sourceRepo, repo, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 安装前的检查：来源地址、大小、SHA-256、撤下列表、签名、包内清单。不通过时抛出 <see cref="HorizonRegistryException"/>。
    /// </summary>
    public void CheckFile(HorizonRegistryProject project, HorizonRegistryVersion version, byte[] data)
    {
        var file = version.File;
        if (!IsAllowedUrl(project, file))
            throw new HorizonRegistryException($"{file.Name} 的下载地址不是注册表或项目仓库的发布地址，未安装。");
        if (data.LongLength != file.Size || data.LongLength > MaxFileSize)
            throw new HorizonRegistryException($"{file.Name} 的大小与注册表不符，未安装。");
        if (Convert.ToHexStringLower(SHA256.HashData(data)) != file.Sha256)
            throw new HorizonRegistryException($"{file.Name} 的 SHA-256 与注册表不符（下载损坏或被篡改），未安装。");
        if (RevokedReason(project.Id, file.Sha256) is { } reason)
            throw new HorizonRegistryException($"{project.Name} {version.Version} 已被撤下：{reason}");
        if (project.Key is { } key)
        {
            if (string.IsNullOrWhiteSpace(file.Signature))
                throw new HorizonRegistryException($"{project.Name} 有签名密钥，但 {version.Version} 没有签名，未安装。");
            HorizonMinisign.Verify(key, file.Signature, data);
        }
        if (project.Kind == KindMod)
            _CheckModManifest(project, version, data);
    }

    /// <summary>
    /// 下载、检查并安装到 <paramref name="targetFolder"/>，返回安装后的路径。同名文件会被替换。
    /// </summary>
    public async Task<string> InstallAsync(
        HttpClient http, HorizonRegistryProject project, HorizonRegistryVersion version, string targetFolder,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstallableKind(project.Kind))
            throw new HorizonRegistryException($"启动器还不能安装 {project.Kind} 类型的内容。");
        var file = version.File;
        if (file.Size > MaxFileSize)
            throw new HorizonRegistryException($"{file.Name} 超过 64 MiB，未安装。");
        if (!IsAllowedUrl(project, file))
            throw new HorizonRegistryException($"{file.Name} 的下载地址不是注册表或项目仓库的发布地址，未安装。");

        using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
        request.Headers.UserAgent.ParseAdd(HorizonReleaseService.UserAgent);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxFileSize)
            throw new HorizonRegistryException($"{file.Name} 超过 64 MiB，未安装。");
        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        CheckFile(project, version, data);

        Directory.CreateDirectory(targetFolder);
        var target = Path.Combine(targetFolder, Path.GetFileName(file.Name));
        var temp = target + ".download";
        await File.WriteAllBytesAsync(temp, data, cancellationToken);
        File.Move(temp, target, true);
        return target;
    }

    private static void _CheckModManifest(HorizonRegistryProject project, HorizonRegistryVersion version, byte[] data)
    {
        string manifest;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            var entry = zip.GetEntry(HorizonMods.ManifestName)
                ?? throw new HorizonRegistryException($"{version.File.Name} 中没有 {HorizonMods.ManifestName}，未安装。");
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            manifest = reader.ReadToEnd();
        }
        catch (InvalidDataException ex)
        {
            throw new HorizonRegistryException($"{version.File.Name} 不是有效的模组包，未安装。", ex);
        }

        var id = HorizonMods.ReadManifestValue(manifest, "id");
        var ver = HorizonMods.ReadManifestValue(manifest, "version");
        if (id != project.Id || ver != version.Version)
            throw new HorizonRegistryException(
                $"{version.File.Name} 的清单（{id} {ver}）与注册表（{project.Id} {version.Version}）不符，未安装。");
        if (HorizonMods.ReadManifestValue(manifest, "api") is { } api && version.Api is not null && api != version.Api)
            throw new HorizonRegistryException($"{version.File.Name} 的 API 线 {api} 与注册表的 {version.Api} 不符，未安装。");
        if (Regex.IsMatch(manifest, @"^\s*\[native\]", RegexOptions.Multiline))
            throw new HorizonRegistryException($"{project.Name} 是原生模组，需要重新构建游戏，启动器不能安装。");
    }

    private static HashSet<string> _ContentApis(HorizonLaunchApi client, string kind) =>
        (client.Content?[kind]?["api"] as JsonArray ?? [])
        .Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)
        .OfType<string>()
        .ToHashSet();

    private static HorizonRegistryProject? _ParseProject(JsonObject o)
    {
        var id = _String(o["id"]);
        var kind = _String(o["kind"]);
        if (id is null || !IdPattern.IsMatch(id) || kind is null) return null;
        var versions = new List<HorizonRegistryVersion>();
        foreach (var node in o["versions"] as JsonArray ?? [])
            if (node is JsonObject v && _ParseVersion(v) is { } version)
                versions.Add(version);
        if (versions.Count == 0) return null;
        return new HorizonRegistryProject(
            id, kind, _String(o["name"]) ?? id, _String(o["summary"]) ?? "", _String(o["description"]),
            _Strings(o["authors"]), _String(o["license"]), _String(o["source"]), _String(o["icon"]), _String(o["key"]),
            _Long(o["downloads"]) ?? 0, _Long(o["stars"]) ?? 0, _String(o["updated"]),
            _Strings(o["categories"]), _Strings(o["tags"]), versions);
    }

    private static HorizonRegistryVersion? _ParseVersion(JsonObject v)
    {
        var version = _String(v["version"]);
        if (version is null || v["file"] is not JsonObject f) return null;
        var name = _String(f["name"]);
        var url = _String(f["url"]);
        var size = _Long(f["size"]);
        var sha = _String(f["sha256"])?.ToLowerInvariant();
        if (name is null || name.IndexOfAny(['/', '\\']) >= 0 || url is null || size is null or <= 0 or > MaxFileSize ||
            sha is null || !Sha256Pattern.IsMatch(sha))
            return null;
        return new HorizonRegistryVersion(
            version, _String(v["published"]), _String(v["changelog"]), _String(v["api"]), _String(v["side"]),
            _String(v["game"]), new HorizonRegistryFile(name, url, size.Value, sha, _String(f["signature"])));
    }

    private static string? _String(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    private static long? _Long(JsonNode? node) => node is JsonValue v && v.TryGetValue<long>(out var n) ? n : null;

    private static int? _Int(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;

    private static IReadOnlyList<string> _Strings(JsonNode? node) =>
        (node as JsonArray ?? []).Select(n => _String(n)).OfType<string>().ToList();
}
