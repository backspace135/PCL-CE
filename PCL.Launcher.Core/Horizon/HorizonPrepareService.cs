using System.Text.Json;

namespace PCL.Horizon;

/// <summary>
/// <c>horizon-prepare --prepare</c> 的输入。原版文件二选一：<see cref="MinecraftDir"/>（官方启动器布局），
/// 或 <see cref="VersionJson"/>、<see cref="ClientJar"/>、<see cref="LibrariesDir"/>、<see cref="AssetsDir"/> 四项都给。
/// </summary>
public sealed record HorizonPrepareRequest
{
    /// <summary>资源输出目录，通常为 <see cref="HorizonPaths.PreparedFor"/>。</summary>
    public required string Output { get; init; }

    /// <summary>服务端 jar，首次准备必需。</summary>
    public string? ServerJar { get; init; }

    public string? MinecraftDir { get; init; }
    public string? VersionJson { get; init; }
    public string? ClientJar { get; init; }
    public string? LibrariesDir { get; init; }
    public string? AssetsDir { get; init; }

    /// <summary>启动器管理的 Java，主版本需为 <see cref="HorizonBuildApi.JavaMajor"/>。</summary>
    public string? JavaHome { get; init; }

    /// <summary>同时复制声音，且结果不能通过 <c>--check</c> 时视为失败。</summary>
    public bool Complete { get; init; } = true;
}

/// <summary><c>--progress json</c> 的一行：<see cref="Message"/> 供展示，<see cref="Fraction"/> 为 0 到 1 的进度。</summary>
public sealed record HorizonPrepareProgress(string? Message, double? Fraction);

/// <summary><c>--check</c> 的结果。不通过时 <see cref="Gaps"/> 为 stdout 中列出的缺口及补齐命令。</summary>
public sealed record HorizonCheckResult(bool IsCurrent, int ExitCode, IReadOnlyList<string> Gaps, string Stderr);

/// <summary>
/// 准备资源失败。<see cref="ExitCode"/> 为 1 表示准备失败（<see cref="Message"/> 取自 <c>{"error"}</c>），
/// 为 2 表示命令行被拒绝（取自 stderr）。
/// </summary>
public class HorizonPrepareException : Exception
{
    public HorizonPrepareException(string message, int exitCode, string stderr) : base(message)
    {
        ExitCode = exitCode;
        Stderr = stderr;
    }

    public int ExitCode { get; }
    public string Stderr { get; }
    public bool IsRefusedCommandLine => ExitCode == 2;
}

/// <summary>封装 <c>horizon-prepare</c>：检查资源是否最新、准备资源。</summary>
public static class HorizonPrepareService
{
    /// <summary>运行 <c>--check --output PREPARED</c>。退出码 0、1 以外（如 2）抛出 <see cref="HorizonPrepareException"/>。</summary>
    public static async Task<HorizonCheckResult> CheckAsync(
        HorizonBuild build, string prepared, CancellationToken cancellationToken = default)
    {
        var api = await build.QueryLaunchApiAsync(cancellationToken);
        List<string> args = ["--check", "--output", Path.GetFullPath(prepared)];
        api.Prepare.Require("horizon-prepare", _OptionNames(args));
        var result = await HorizonProcess.RunAsync(
            build.PreparePath, args, build.Directory, cancellationToken: cancellationToken);
        var stderr = string.Join('\n', result.Stderr).Trim();
        return result.ExitCode switch
        {
            0 => new HorizonCheckResult(true, 0, [], stderr),
            1 => new HorizonCheckResult(false, 1, result.Stdout.Where(l => l.Trim().Length > 0).ToList(), stderr),
            _ => throw new HorizonPrepareException(
                $"horizon-prepare --check 退出码为 {result.ExitCode}：{stderr}", result.ExitCode, stderr),
        };
    }

    /// <summary>
    /// 运行 <c>--prepare … --progress json</c>，逐行解析进度。取消时结束进程并抛出 <see cref="OperationCanceledException"/>；
    /// 失败时抛出 <see cref="HorizonPrepareException"/>。
    /// </summary>
    public static async Task PrepareAsync(
        HorizonBuild build, HorizonPrepareRequest request, IProgress<HorizonPrepareProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var api = await build.QueryLaunchApiAsync(cancellationToken);
        var args = BuildPrepareArguments(request);
        api.Prepare.Require("horizon-prepare", _OptionNames(args));
        Directory.CreateDirectory(request.Output);

        string? error = null;
        var result = await HorizonProcess.RunAsync(build.PreparePath, args, build.Directory, line =>
        {
            var parsed = ParseProgressLine(line);
            if (parsed is null) return;
            if (parsed.Value.Error is { } e) error = e;
            else progress?.Report(parsed.Value.Progress!);
        }, cancellationToken);

        if (result.ExitCode == 0) return;
        var stderr = string.Join('\n', result.Stderr).Trim();
        var message = result.ExitCode switch
        {
            1 => error ?? _LastLine(result.Stderr) ?? "准备资源失败。",
            2 => $"horizon-prepare 拒绝了命令行：{(stderr.Length > 0 ? stderr : error)}",
            _ => $"horizon-prepare 异常退出（退出码 {result.ExitCode}）：{error ?? _LastLine(result.Stderr)}",
        };
        throw new HorizonPrepareException(message, result.ExitCode, stderr);
    }

    /// <summary>按请求拼出 <c>horizon-prepare</c> 的参数列表。</summary>
    public static List<string> BuildPrepareArguments(HorizonPrepareRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Output);
        var separate = new[] { request.VersionJson, request.ClientJar, request.LibrariesDir, request.AssetsDir };
        var separateCount = separate.Count(s => !string.IsNullOrEmpty(s));
        if (!string.IsNullOrEmpty(request.MinecraftDir) && separateCount > 0)
            throw new ArgumentException("MinecraftDir 与 VersionJson/ClientJar/LibrariesDir/AssetsDir 只能二选一。");
        if (string.IsNullOrEmpty(request.MinecraftDir) && separateCount != 4)
            throw new ArgumentException("需要 MinecraftDir，或者 VersionJson、ClientJar、LibrariesDir、AssetsDir 四项都给。");

        List<string> args = ["--prepare"];
        void Add(string option, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            args.Add(option);
            args.Add(Path.GetFullPath(value));
        }
        Add("--server-jar", request.ServerJar);
        Add("--minecraft-dir", request.MinecraftDir);
        Add("--version-json", request.VersionJson);
        Add("--client-jar", request.ClientJar);
        Add("--libraries-dir", request.LibrariesDir);
        Add("--assets-dir", request.AssetsDir);
        Add("--java-home", request.JavaHome);
        Add("--output", request.Output);
        if (request.Complete) args.Add("--complete");
        args.Add("--progress");
        args.Add("json");
        return args;
    }

    /// <summary>
    /// 解析 <c>--progress json</c> 的一行。<c>{"error"}</c> 返回 Error，<c>{"message"}</c>/<c>{"fraction"}</c> 返回 Progress，
    /// 非 JSON 或没有已知字段的行返回 <see langword="null"/>。
    /// </summary>
    public static (HorizonPrepareProgress? Progress, string? Error)? ParseProgressLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                return (null, e.GetString());
            string? message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() : null;
            double? fraction = root.TryGetProperty("fraction", out var f) && f.ValueKind == JsonValueKind.Number
                ? Math.Clamp(f.GetDouble(), 0, 1) : null;
            return message is null && fraction is null ? null : (new HorizonPrepareProgress(message, fraction), null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> _OptionNames(IEnumerable<string> args) =>
        args.Where(a => a.StartsWith("--", StringComparison.Ordinal));

    private static string? _LastLine(IReadOnlyList<string> lines) =>
        lines.LastOrDefault(l => l.Trim().Length > 0)?.Trim();
}
