using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCL.Horizon;

/// <summary><c>--launch-api</c> 回复中 <c>options</c> 的一项，<see cref="Value"/> 为占位名，开关为 <see langword="null"/>。</summary>
public sealed record HorizonLaunchOption(string Name, string? Value);

/// <summary>
/// <c>horizon-client --launch-api</c> 或 <c>horizon-prepare --launch-api</c> 的回复。未知字段保留在 <see cref="Raw"/> 中。
/// </summary>
public sealed class HorizonLaunchApi
{
    /// <summary>支持的 Launch API 主线：同一条线内的新版本只做增加。</summary>
    public const string SupportedLine = "0.1";

    /// <summary>崩溃报告行前缀的默认值，回复中有 <c>crash_report_line</c> 时以回复为准。</summary>
    public const string DefaultCrashReportLine = "Horizon: crash report saved to ";

    public required string LaunchApiVersion { get; init; }
    public string? Horizon { get; init; }
    public string? Minecraft { get; init; }
    public int? JavaMajor { get; init; }

    /// <summary>资源布局版本（仅 prepare），按原样保留为 JSON。</summary>
    public JsonNode? Schema { get; init; }

    /// <summary>资源内容版本（仅 prepare），按原样保留为 JSON。</summary>
    public JsonNode? OutputVersion { get; init; }

    public IReadOnlyList<HorizonLaunchOption> Options { get; init; } = [];
    public JsonObject? Content { get; init; }
    public string CrashReportLine { get; init; } = DefaultCrashReportLine;
    public IReadOnlyList<int> ExitCodes { get; init; } = [];
    public required JsonObject Raw { get; init; }

    public bool Supports(string option) => Options.Any(o => o.Name == option);

    /// <summary>解析回复并确认 Launch API 属于 0.1.x，否则抛出 <see cref="HorizonLaunchApiException"/>。</summary>
    public static HorizonLaunchApi Parse(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                   ?? throw new HorizonLaunchApiException("--launch-api 的输出不是 JSON 对象。");
        }
        catch (JsonException ex)
        {
            throw new HorizonLaunchApiException($"--launch-api 的输出不是 JSON：{ex.Message}", ex);
        }

        var version = _String(root, "launch_api")
                      ?? throw new HorizonLaunchApiException("--launch-api 的输出缺少 launch_api。");
        if (!IsSupportedVersion(version))
            throw new HorizonLaunchApiException(
                $"此 Horizon 构建的 Launch API 为 {version}，本启动器只支持 {SupportedLine}.x，请更新启动器或换用其他构建。");

        var options = new List<HorizonLaunchOption>();
        if (root["options"] is JsonArray list)
            foreach (var item in list)
                if (item is JsonObject o && _String(o, "name") is { } name)
                    options.Add(new HorizonLaunchOption(name, _String(o, "value")));

        var exitCodes = new List<int>();
        if (root["exit_codes"] is JsonArray codes)
            foreach (var code in codes)
                if (code is JsonValue v && v.TryGetValue<int>(out var n))
                    exitCodes.Add(n);

        int? javaMajor = root["java_major"] is JsonValue jv && jv.TryGetValue<int>(out var major) ? major : null;

        return new HorizonLaunchApi
        {
            LaunchApiVersion = version,
            Horizon = _String(root, "horizon"),
            Minecraft = _String(root, "minecraft"),
            JavaMajor = javaMajor,
            Schema = root["schema"]?.DeepClone(),
            OutputVersion = root["output_version"]?.DeepClone(),
            Options = options,
            Content = root["content"]?.DeepClone() as JsonObject,
            CrashReportLine = _String(root, "crash_report_line") is { Length: > 0 } line ? line : DefaultCrashReportLine,
            ExitCodes = exitCodes,
            Raw = root,
        };
    }

    public static bool IsSupportedVersion(string version)
    {
        var parts = version.Split('.');
        return parts.Length >= 2 && $"{parts[0]}.{parts[1]}" == SupportedLine;
    }

    /// <summary>参数不在回复的 <c>options</c> 中时抛出 <see cref="HorizonLaunchApiException"/>。</summary>
    public void Require(string program, IEnumerable<string> options)
    {
        var missing = options.Where(o => !Supports(o)).Distinct().ToList();
        if (missing.Count > 0)
            throw new HorizonLaunchApiException(
                $"此 Horizon 构建的 {program} 不支持参数 {string.Join("、", missing)}，请换用其他构建。");
    }

    private static string? _String(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>同一构建两个程序的 <c>--launch-api</c> 回复。</summary>
public sealed record HorizonBuildApi(HorizonLaunchApi Client, HorizonLaunchApi Prepare)
{
    /// <summary>准备资源需要的 Minecraft 版本。</summary>
    public string? Minecraft => Prepare.Minecraft ?? Client.Minecraft;

    /// <summary>准备资源需要的 Java 主版本。</summary>
    public int? JavaMajor => Prepare.JavaMajor;
}

/// <summary><c>--launch-api</c> 无法执行、无法解析或版本不受支持，或构建不支持所需参数。</summary>
public class HorizonLaunchApiException : Exception
{
    public HorizonLaunchApiException(string message) : base(message) { }
    public HorizonLaunchApiException(string message, Exception? inner) : base(message, inner) { }
}
