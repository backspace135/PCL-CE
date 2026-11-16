using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Horizon;

namespace PCL;

/// <summary>
///     一个正在运行的游戏。
/// </summary>
public sealed record RunningGame(
    HorizonInstance Instance,
    HorizonBuild Build,
    HorizonGameProcess Process,
    HorizonLaunchApi Client)
{
    public DateTime StartedAt { get; } = DateTime.Now;
}

/// <summary>
///     游戏结束时交给界面处理的结果，包括崩溃报告与日志路径。
/// </summary>
public sealed record GameExitInfo(RunningGame Game, HorizonExitResult Result)
{
    public string LatestLogPath => Game.Instance.LatestLogPath;
}

/// <summary>
///     游戏结束后的界面处理（崩溃提示等）。
/// </summary>
public interface IGameExitUi
{
    void OnGameExited(GameExitInfo info);
}

/// <summary>
///     跟踪由启动器启动的游戏进程：结束时恢复启动器窗口，出错时交给 <see cref="IGameExitUi" />。
/// </summary>
public static class HorizonGames
{
    private static readonly List<RunningGame> Games = [];
    private static readonly HashSet<RunningGame> KilledByUser = [];

    public static IGameExitUi? ExitUi { get; set; }

    /// <summary>
    ///     是否有正在运行的游戏。
    /// </summary>
    public static bool HasRunningGame
    {
        get
        {
            lock (Games)
            {
                return Games.Count != 0;
            }
        }
    }

    /// <summary>
    ///     正在运行的游戏的快照。
    /// </summary>
    public static IReadOnlyList<RunningGame> Running
    {
        get
        {
            lock (Games)
            {
                return Games.ToList();
            }
        }
    }

    /// <summary>
    ///     有游戏开始或结束时触发。
    /// </summary>
    public static event Action? RunningChanged;

    internal static void Track(RunningGame game)
    {
        lock (Games)
        {
            Games.Add(game);
        }

        WatcherUi.Current?.RefreshShutdownButton();
        RunningChanged?.Invoke();
        LauncherCommon.RunInNewThread(() => _Watch(game), $"Horizon Watcher {game.Process.Process.Id}");
    }

    /// <summary>
    ///     强制结束所有游戏。
    /// </summary>
    public static void KillAll()
    {
        foreach (var game in Running)
        {
            try
            {
                lock (Games)
                {
                    KilledByUser.Add(game);
                }

                game.Process.Kill();
                LauncherCommon.Log($"[Horizon] 已强制结束游戏：{game.Instance.Name}");
            }
            catch (Exception ex)
            {
                LauncherCommon.Log(ex, $"[Horizon] 结束游戏 {game.Instance.Name} 失败");
            }
        }
    }

    private static void _Watch(RunningGame game)
    {
        HorizonExitResult result;
        try
        {
            result = game.Process.WaitForExitAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(ex, "[Horizon] 等待游戏退出失败");
            return;
        }

        bool anyLeft;
        bool killed;
        lock (Games)
        {
            Games.Remove(game);
            killed = KilledByUser.Remove(game);
            anyLeft = Games.Count != 0;
        }

        LauncherCommon.Log(
            $"[Horizon] 游戏已退出：{game.Instance.Name}，退出码 {result.ExitCode}（{result.Kind}），"
            + $"运行了 {(DateTime.Now - game.StartedAt).TotalSeconds:F0} 秒"
            + (result.CrashReportPath is null ? "" : $"，崩溃报告 {result.CrashReportPath}"));
        game.Process.Dispose();
        WatcherUi.Current?.RefreshShutdownButton();
        RunningChanged?.Invoke();

        var failed = !killed && result.Kind is not HorizonExitKind.Quit;
        if (!anyLeft)
            _RestoreLauncher(failed);
        if (failed)
            ExitUi?.OnGameExited(new GameExitInfo(game, result));
    }

    private static void _RestoreLauncher(bool failed)
    {
        switch (Config.Launch.LauncherVisibility)
        {
            case LauncherVisibility.HideAndExit when !failed:
                LauncherCommon.RunInUi(() => WatcherUi.Current?.ExitApplication());
                break;
            case LauncherVisibility.HideAndExit:
            case LauncherVisibility.HideAndReopen:
                LauncherCommon.RunInUi(() => WatcherUi.Current?.ShowApplication());
                break;
        }
    }

    /// <summary>
    ///     出错退出时的简短说明，用于没有更详细界面时的提示。
    /// </summary>
    public static string Describe(HorizonExitResult result) => result.Kind switch
    {
        HorizonExitKind.Error => Lang.Text("Horizon.Exit.Error", result.ExitCode),
        HorizonExitKind.Panic => Lang.Text("Horizon.Exit.Panic", result.ExitCode),
        HorizonExitKind.RefusedCommandLine => Lang.Text("Horizon.Exit.RefusedCommandLine", result.LastStderrLine ?? ""),
        _ => Lang.Text("Horizon.Exit.Unknown", result.ExitCode)
    };
}
