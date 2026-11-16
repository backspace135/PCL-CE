using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCL.Horizon;

/// <summary>安装时写入构建目录的记录，便于在 release 过期后仍知道它的来源。</summary>
public sealed record HorizonBuildInfo(string Tag, string Version, string Asset, string Sha256, DateTimeOffset InstalledAt)
{
    public const string FileName = ".pcl-horizon-build.json";

    internal void Write(string dir)
    {
        var json = new JsonObject
        {
            ["tag"] = Tag,
            ["version"] = Version,
            ["asset"] = Asset,
            ["sha256"] = Sha256,
            ["installed_at"] = InstalledAt.ToString("O"),
        };
        File.WriteAllText(Path.Combine(dir, FileName), json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static HorizonBuildInfo? TryRead(string dir)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(Path.Combine(dir, FileName))) is not JsonObject o) return null;
            return new HorizonBuildInfo(
                (string?)o["tag"] ?? "",
                (string?)o["version"] ?? "",
                (string?)o["asset"] ?? "",
                (string?)o["sha256"] ?? "",
                DateTimeOffset.TryParse((string?)o["installed_at"], out var at) ? at : DateTimeOffset.MinValue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>一个已安装的 Horizon 构建目录（<c>versions/&lt;版本&gt;/</c>），需保持完整。</summary>
public sealed class HorizonBuild
{
    private Task<HorizonBuildApi>? _api;

    public HorizonBuild(string directory)
    {
        Directory = Path.GetFullPath(directory);
        Info = HorizonBuildInfo.TryRead(Directory);
    }

    public string Directory { get; }

    /// <summary>构建版本，即目录名，如 <c>nightly-2026-11-13-9f42064</c>。</summary>
    public string Version => Info?.Version is { Length: > 0 } v ? v : Path.GetFileName(Directory);

    public HorizonBuildInfo? Info { get; }

    public string ClientPath => Path.Combine(Directory, "horizon-client" + HorizonPlatform.ExecutableSuffix);

    public string PreparePath => Path.Combine(Directory, "horizon-prepare" + HorizonPlatform.ExecutableSuffix);

    /// <summary>两个可执行文件都在。</summary>
    public bool IsComplete => File.Exists(ClientPath) && File.Exists(PreparePath);

    /// <summary>
    /// 运行 <c>horizon-client --launch-api</c> 和 <c>horizon-prepare --launch-api</c> 并解析，结果在本对象内缓存。
    /// 失败或 Launch API 不受支持时抛出 <see cref="HorizonLaunchApiException"/>。
    /// </summary>
    public Task<HorizonBuildApi> QueryLaunchApiAsync(CancellationToken cancellationToken = default)
    {
        var task = _api;
        if (task is null || task.IsFaulted || task.IsCanceled)
            _api = task = _QueryAsync(cancellationToken);
        return task;
    }

    private async Task<HorizonBuildApi> _QueryAsync(CancellationToken cancellationToken)
    {
        var client = _QueryOneAsync(ClientPath, cancellationToken);
        var prepare = _QueryOneAsync(PreparePath, cancellationToken);
        return new HorizonBuildApi(await client, await prepare);
    }

    private async Task<HorizonLaunchApi> _QueryOneAsync(string exe, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(exe);
        if (!File.Exists(exe))
            throw new HorizonLaunchApiException($"构建 {Version} 不完整：缺少 {name}。");
        HorizonProcessResult result;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            result = await HorizonProcess.RunAsync(exe, ["--launch-api"], Directory, cancellationToken: timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HorizonLaunchApiException($"{name} --launch-api 超时。");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            throw new HorizonLaunchApiException($"无法运行 {name}：{ex.Message}", ex);
        }
        if (result.ExitCode != 0)
            throw new HorizonLaunchApiException(
                $"{name} --launch-api 退出码为 {result.ExitCode}：{string.Join('\n', result.Stderr).Trim()}");
        return HorizonLaunchApi.Parse(string.Join('\n', result.Stdout));
    }

    public override string ToString() => Version;
}
