using System.Diagnostics;
using System.Text.Json;

namespace PCL.Horizon;

/// <summary>正版账户的会话，经 stdin 交给游戏，绝不出现在命令行中。<see cref="ToString"/> 不含令牌。</summary>
public sealed class HorizonSession
{
    public HorizonSession(string name, string uuid, string accessToken, string? xuid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(uuid);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        Name = name;
        Uuid = uuid;
        AccessToken = accessToken;
        Xuid = string.IsNullOrWhiteSpace(xuid) ? null : xuid;
    }

    public string Name { get; }
    public string Uuid { get; }
    public string AccessToken { get; }
    public string? Xuid { get; }

    /// <summary>会话 JSON：<c>{"name","uuid","access_token","user_type":"msa","xuid"?}</c>。</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("name", Name);
            writer.WriteString("uuid", Uuid);
            writer.WriteString("access_token", AccessToken);
            writer.WriteString("user_type", "msa");
            if (Xuid is not null) writer.WriteString("xuid", Xuid);
            writer.WriteEndObject();
        }
        return HorizonProcess.Utf8.GetString(buffer.ToArray());
    }

    public override string ToString() => $"{Name} ({Uuid})";
}

/// <summary>一次启动的参数。<see cref="Home"/> 为空时与 <see cref="GameDir"/> 相同。</summary>
public sealed record HorizonLaunchRequest
{
    public required HorizonBuild Build { get; init; }

    /// <summary>准备好的资源目录（<c>--resources</c>）。</summary>
    public required string Resources { get; init; }

    /// <summary>实例目录（<c>--game-dir</c>）。</summary>
    public required string GameDir { get; init; }

    /// <summary>日志与崩溃报告目录（<c>--home</c>）。</summary>
    public string? Home { get; init; }

    public required HorizonSession Session { get; init; }
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public bool Fullscreen { get; init; }

    /// <summary><c>saves/</c> 下的存档文件夹名，不能是路径。</summary>
    public string? QuickPlaySingleplayer { get; init; }

    /// <summary><c>HOST[:PORT]</c>。</summary>
    public string? QuickPlayMultiplayer { get; init; }

    /// <summary>
    /// 实例的创建入口：资源取自 <see cref="HorizonPaths.PreparedFor"/>，游戏目录与 home 都是实例目录，窗口参数取自实例。
    /// </summary>
    public static HorizonLaunchRequest For(
        HorizonPaths paths, HorizonInstance instance, HorizonBuild build, HorizonSession session) => new()
    {
        Build = build,
        Resources = paths.PreparedFor(build.Version),
        GameDir = instance.Directory,
        Home = instance.Directory,
        Session = session,
        Width = instance.Width,
        Height = instance.Height,
        Fullscreen = instance.Fullscreen,
    };
}

/// <summary>游戏结束的方式，对应 <c>horizon-client</c> 的退出码 0、1、2、101。</summary>
public enum HorizonExitKind
{
    Quit,
    Error,
    RefusedCommandLine,
    Panic,
    Unknown,
}

/// <summary>游戏退出后的结果。<see cref="CrashReportPath"/> 在退出码为 1 或 101 且 stderr 最后一行给出路径时有值。</summary>
public sealed record HorizonExitResult(
    int ExitCode,
    HorizonExitKind Kind,
    string? CrashReportPath,
    string? LastStderrLine,
    IReadOnlyList<string> StderrTail)
{
    public static HorizonExitKind Classify(int exitCode) => exitCode switch
    {
        0 => HorizonExitKind.Quit,
        1 => HorizonExitKind.Error,
        2 => HorizonExitKind.RefusedCommandLine,
        101 => HorizonExitKind.Panic,
        _ => HorizonExitKind.Unknown,
    };

    /// <summary>由退出码与 stderr 末尾得出结果，崩溃报告行前缀来自 <c>crash_report_line</c>。</summary>
    public static HorizonExitResult From(int exitCode, IReadOnlyList<string> stderrTail, string crashReportLine)
    {
        var last = stderrTail.LastOrDefault(l => l.Trim().Length > 0)?.TrimEnd();
        string? crash = null;
        var kind = Classify(exitCode);
        if (last is not null && kind is HorizonExitKind.Error or HorizonExitKind.Panic or HorizonExitKind.Unknown)
        {
            var prefix = crashReportLine.TrimEnd();
            if (last.StartsWith(prefix, StringComparison.Ordinal) && last.Length > prefix.Length)
                crash = last[prefix.Length..].Trim();
        }
        return new HorizonExitResult(exitCode, kind, crash, last, stderrTail);
    }
}

/// <summary>游戏输出的一行。</summary>
public sealed record HorizonOutputLine(string Text, bool IsError);

/// <summary>运行中的 <c>horizon-client</c>。</summary>
public sealed class HorizonGameProcess : IDisposable
{
    private const int TailSize = 200;
    private readonly Queue<string> _stderrTail = new();
    private readonly string _crashReportLine;

    internal HorizonGameProcess(Process process, string crashReportLine, Action<HorizonOutputLine>? onOutput)
    {
        Process = process;
        _crashReportLine = crashReportLine;
        if (onOutput is not null) OutputReceived += onOutput;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) OutputReceived?.Invoke(new HorizonOutputLine(e.Data, false));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_stderrTail)
            {
                _stderrTail.Enqueue(e.Data);
                while (_stderrTail.Count > TailSize) _stderrTail.Dequeue();
            }
            OutputReceived?.Invoke(new HorizonOutputLine(e.Data, true));
        };
    }

    public Process Process { get; }

    /// <summary>stdout 与 stderr 的每一行，在读取线程上触发。</summary>
    public event Action<HorizonOutputLine>? OutputReceived;

    /// <summary>stderr 最后 200 行。</summary>
    public IReadOnlyList<string> StderrTail
    {
        get { lock (_stderrTail) return _stderrTail.ToList(); }
    }

    /// <summary>等待游戏退出（及输出读完）。取消只停止等待，不结束游戏。</summary>
    public async Task<HorizonExitResult> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        await Process.WaitForExitAsync(cancellationToken);
        return HorizonExitResult.From(Process.ExitCode, StderrTail, _crashReportLine);
    }

    /// <summary>强制结束游戏。</summary>
    public void Kill() => HorizonProcess.Kill(Process);

    internal void BeginRead()
    {
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
    }

    public void Dispose() => Process.Dispose();
}

/// <summary>启动 <c>horizon-client</c>，会话经 stdin 传入。</summary>
public static class HorizonLaunchService
{
    /// <summary>按请求拼出参数，并确认每个参数都在 <paramref name="client"/> 的 <c>options</c> 中。</summary>
    public static List<string> BuildArguments(HorizonLaunchRequest request, HorizonLaunchApi client)
    {
        if (request.Width <= 0 || request.Height <= 0)
            throw new ArgumentException("窗口宽高必须为正数。");
        if (request.QuickPlaySingleplayer is not null && request.QuickPlayMultiplayer is not null)
            throw new ArgumentException("不能同时快速进入单人世界和服务器。");
        if (request.QuickPlaySingleplayer is { } world &&
            (string.IsNullOrWhiteSpace(world) || world is "." or ".." || world.IndexOfAny(['/', '\\']) >= 0 ||
             world.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new ArgumentException($"“{world}”不是 saves/ 下的存档文件夹名。");
        if (request.QuickPlayMultiplayer is { } server && string.IsNullOrWhiteSpace(server))
            throw new ArgumentException("服务器地址为空。");

        var gameDir = Path.GetFullPath(request.GameDir);
        var args = new List<string>();
        var options = new List<string>();
        void Add(string option, string? value = null)
        {
            options.Add(option);
            args.Add(option);
            if (value is not null) args.Add(value);
        }
        Add("--resources", Path.GetFullPath(request.Resources));
        Add("--game-dir", gameDir);
        Add("--home", Path.GetFullPath(request.Home ?? gameDir));
        Add("--width", request.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add("--height", request.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (request.Fullscreen) Add("--fullscreen");
        if (request.QuickPlaySingleplayer is { } w) Add("--quickPlaySingleplayer", w);
        if (request.QuickPlayMultiplayer is { } s) Add("--quickPlayMultiplayer", s.Trim());
        Add("--session-stdin");
        client.Require("horizon-client", options);
        return args;
    }

    /// <summary>
    /// 启动游戏：先确认构建支持全部参数，再以参数列表启动进程，把会话 JSON 写入 stdin 后关闭。
    /// <paramref name="onOutput"/> 在开始读取输出之前订阅，不会漏掉最早的输出。
    /// </summary>
    public static async Task<HorizonGameProcess> StartAsync(
        HorizonLaunchRequest request, Action<HorizonOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var api = await request.Build.QueryLaunchApiAsync(cancellationToken);
        var args = BuildArguments(request, api.Client);
        var gameDir = Path.GetFullPath(request.GameDir);
        Directory.CreateDirectory(gameDir);

        var process = new Process { StartInfo = HorizonProcess.CreateStartInfo(request.Build.ClientPath, args, gameDir) };
        var game = new HorizonGameProcess(process, api.Client.CrashReportLine, onOutput);
        try
        {
            process.Start();
            game.BeginRead();
            await process.StandardInput.WriteAsync(request.Session.ToJson().AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();
        }
        catch (IOException) when (process.HasExited)
        {
            // 游戏在读取会话之前就退出了（例如命令行被拒绝），结果由 WaitForExitAsync 给出。
        }
        catch
        {
            game.Kill();
            game.Dispose();
            throw;
        }
        return game;
    }
}
