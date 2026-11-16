using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.Minecraft.IdentityModel;
using PCL.Core.Minecraft.Profile;
using PCL.Core.Minecraft.Profile.Authentication;
using PCL.Core.Minecraft.Profile.Authentication.Utils;
using PCL.Core.Minecraft.Profile.Models;
using PCL.Core.Utils;
using PCL.Horizon;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

/// <summary>
///     启动 Horizon：检查构建 → 读取启动接口 → 正版登录 → 检查资源（不通过则准备 Java、下载原版文件并准备资源）→ 启动游戏。
/// </summary>
public static partial class HorizonLaunch
{
    public const string StageBuildId = "Horizon:Build";
    public const string StageApiId = "Horizon:Api";
    public const string StageLoginId = "Horizon:Login";
    public const string StageCheckId = "Horizon:Check";
    public const string StageFilesId = "Horizon:Files";
    public const string StageJavaId = "Horizon:Java";
    public const string StagePrepareId = "Horizon:Prepare";
    public const string StageStartId = "Horizon:Start";
    public const string StageWaitWindowId = "Horizon:WaitWindow";
    public const string StageEndId = "Horizon:End";
    public const string StageRootId = "Horizon:Root";

    /// <summary>
    ///     外层加载器：负责预检查、运行 <see cref="LaunchLoaderReal" /> 并处理结果。
    /// </summary>
    public static readonly Loading.LoaderTask<HorizonLaunchOptions, object> LaunchLoader = new(
        "Loader Horizon Launch",
        _Run)
    {
        OnStateChanged = _ => _OnLaunchStateChanged()
    };

    /// <summary>
    ///     当前一次启动的各阶段，供启动页显示进度。
    /// </summary>
    public static Loading.LoaderCombo<object>? LaunchLoaderReal { get; private set; }

    /// <summary>
    ///     当前一次启动的参数。
    /// </summary>
    public static HorizonLaunchOptions? CurrentOptions { get; private set; }

    /// <summary>
    ///     是否正在启动。
    /// </summary>
    public static bool IsLaunching { get; private set; }

    private static LaunchContext? _context;

    /// <summary>
    ///     尝试启动游戏。必须在 UI 线程调用。返回是否开始了启动（没有开始时一定已经提示了原因）。
    /// </summary>
    public static bool Start(HorizonLaunchOptions? options = null)
    {
        if (!LauncherCommon.RunInUi())
            throw new InvalidOperationException("HorizonLaunch.Start 必须在 UI 线程调用！");
        if (LaunchLoader.State == LoadState.Loading)
        {
            LauncherUi.Interaction?.ShowHint(
                Lang.Text("Minecraft.Launch.Error.AlreadyLaunching"),
                NotificationType.Error);
            return false;
        }

        options ??= new HorizonLaunchOptions();
        var instance = options.Instance ?? HorizonEnvironment.SelectedInstance;
        if (instance is null)
        {
            LauncherUi.Interaction?.ShowHint(Lang.Text("Horizon.Launch.Error.NoInstance"), NotificationType.Error);
            return false;
        }

        if (options.Instance is not null && HorizonEnvironment.SelectedInstance?.Id != instance.Id)
        {
            Log($"在启动前切换到实例 {instance.Name}");
            HorizonEnvironment.SelectInstance(instance);
        }

        IsLaunching = true;
        CurrentOptions = options with { Instance = instance };
        LaunchUi.PrepareLaunchNavigation();
        LaunchLoader.Start(CurrentOptions, true);
        return true;
    }

    /// <summary>
    ///     记录启动日志，显示在启动页的日志卡片中，并过滤令牌与用户名。
    /// </summary>
    public static void Log(string text)
    {
        text = LogFilter.FilterUserName(LogFilter.FilterAccessToken(text, '*'), '*');
        LaunchUi.AppendLaunchLog($"\r\n[{TimeUtils.GetTimeNow()}] {text}");
        LauncherCommon.Log($"[Horizon] {text}");
    }

    private static void _OnLaunchStateChanged()
    {
        switch (LaunchLoader.State)
        {
            case LoadState.Finished:
            case LoadState.Failed:
            case LoadState.Waiting:
            case LoadState.Aborted:
                IsLaunching = false;
                LaunchUi.ShowLoginPage();
                break;

            case LoadState.Loading:
                LaunchUi.ClearLaunchLog();
                break;
        }
    }

    private static void _Run(Loading.LoaderTask<HorizonLaunchOptions, object> loader)
    {
        var options = loader.input!;
        LaunchUi.ShowLaunchingPage();
        try
        {
            _context = new LaunchContext(options, options.Instance!, HorizonEnvironment.Paths);
            _Precheck(_context);
            var real = _CreateStages();
            LaunchLoaderReal = real;
            real.Start();
            Loading.LoaderTaskbarAdd(real);
            while (real.State == LoadState.Loading)
            {
                LaunchUi.RefreshLaunching();
                Thread.Sleep(100);
            }

            LaunchUi.RefreshLaunching();
            switch (real.State)
            {
                case LoadState.Finished:
                    LauncherUi.Interaction?.ShowHint(
                        Lang.Text("Minecraft.Launch.Success", _context.Instance.Name),
                        NotificationType.Success);
                    break;
                case LoadState.Aborted:
                    LauncherUi.Interaction?.ShowHint(Lang.Text("Minecraft.Launch.Cancelled"));
                    break;
                case LoadState.Failed:
                    throw real.Error;
            }
        }
        catch (Exception ex)
        {
            _ReportFailure(ex);
            throw;
        }
        finally
        {
            IsLaunching = false;
        }
    }

    private static void _Precheck(LaunchContext context)
    {
        if (Config.Debug.AddRandomDelay)
            Thread.Sleep(RandomUtils.NextInt(100, 2000));
        if (!Directory.Exists(context.Instance.Directory))
            throw new Exception(Lang.Text("Horizon.Launch.Error.InstanceMissing", context.Instance.Name));
        if (ProfileService.Current is { ProfileType: not ProfileType.Microsoft })
            throw new Exception(Lang.Text("Horizon.Launch.Error.NeedMicrosoft"));
        Log("预检测已通过");
    }

    private static Loading.LoaderCombo<object> _CreateStages()
    {
        Loading.LoaderTask<int, int> Stage(string id, string key, Action<Loading.LoaderTask<int, int>> action,
            double weight) =>
            new(Lang.Text(key), action)
            {
                ProgressWeight = weight,
                Identifier = id,
                DisplayNameFactory = () => Lang.Text(key)
            };

        List<Loading.LoaderBase> stages =
        [
            Stage(StageBuildId, "Horizon.Launch.Stage.Build", _StageBuild, 10),
            Stage(StageApiId, "Horizon.Launch.Stage.Api", _StageApi, 1),
            Stage(StageLoginId, "Horizon.Launch.Stage.Login", _StageLogin, 5),
            Stage(StageCheckId, "Horizon.Launch.Stage.Check", _StageCheck, 1),
            new Loading.LoaderTask<int, List<DownloadFile>>(
                Lang.Text("Horizon.Launch.Stage.Files"),
                _StageCoreFiles)
            {
                ProgressWeight = 1,
                Identifier = StageFilesId,
                DisplayNameFactory = () => Lang.Text("Horizon.Launch.Stage.Files")
            },
            new LoaderDownload(Lang.Text("Horizon.Launch.Stage.DownloadFiles"), []) { ProgressWeight = 8 },
            new Loading.LoaderTask<int, List<DownloadFile>>(
                Lang.Text("Horizon.Launch.Stage.Assets"),
                _StageAssets)
            {
                ProgressWeight = 1,
                Identifier = StageFilesId,
                DisplayNameFactory = () => Lang.Text("Horizon.Launch.Stage.Assets")
            },
            new LoaderDownload(Lang.Text("Horizon.Launch.Stage.DownloadAssets"), []) { ProgressWeight = 15 },
            Stage(StageJavaId, "Horizon.Launch.Stage.Java", _StageJava, 4),
            Stage(StagePrepareId, "Horizon.Launch.Stage.Prepare", _StagePrepare, 30),
            Stage(StageStartId, "Horizon.Launch.Stage.Start", _StageStart, 2),
            Stage(StageWaitWindowId, "Horizon.Launch.Stage.WaitWindow", _StageWaitWindow, 1),
            Stage(StageEndId, "Horizon.Launch.Stage.End", _StageEnd, 1)
        ];
        return new Loading.LoaderCombo<object>(Lang.Text("Horizon.Launch.Stage.Root"), stages)
        {
            show = false,
            Identifier = StageRootId,
            DisplayNameFactory = () => Lang.Text("Horizon.Launch.Stage.Root")
        };
    }

    private static LaunchContext Context => _context ?? throw new InvalidOperationException("没有正在进行的启动。");

    #region 阶段

    private static void _StageBuild(Loading.LoaderTask<int, int> task)
    {
        var context = Context;
        var installed = HorizonEnvironment.InstalledBuilds();
        var build = context.Instance.ResolveBuild(installed);
        if (build is not null)
        {
            Log($"使用 Horizon 构建 {build.Version}");
            context.Build = build;
            return;
        }

        if (context.Instance.BuildVersion is { } pinned)
            Log($"实例固定的构建 {pinned} 未安装，尝试下载");
        else
            Log("尚未安装任何 Horizon 构建，下载最新的 Nightly");

        var service = HorizonEnvironment.ReleaseService;
        var releases = service.ListReleasesAsync(task.AbortedToken).GetAwaiter().GetResult();
        var release = context.Instance.BuildVersion is { } version
            ? releases.FirstOrDefault(r => r.Version == version)
                ?? throw new Exception(Lang.Text("Horizon.Launch.Error.PinnedBuildGone", version))
            : releases.FirstOrDefault()
                ?? throw new Exception(Lang.Text("Horizon.Launch.Error.NoRelease"));
        Log($"下载并校验 Horizon 构建 {release.Version}");
        context.Build = service.InstallAsync(
                release,
                context.Paths,
                new Progress<double>(p => task.Progress = p),
                task.AbortedToken)
            .GetAwaiter()
            .GetResult();
        Log($"已安装 Horizon 构建 {context.Build.Version}");
    }

    private static void _StageApi(Loading.LoaderTask<int, int> task)
    {
        var context = Context;
        context.Api = context.Build!.QueryLaunchApiAsync(task.AbortedToken).GetAwaiter().GetResult();
        context.Minecraft = context.Api.Minecraft
            ?? throw new Exception(Lang.Text("Horizon.Launch.Error.NoMinecraftVersion", context.Build.Version));
        Log($"Launch API {context.Api.Client.LaunchApiVersion}，需要 Minecraft {context.Minecraft} 与 Java {context.Api.JavaMajor}");
    }

    private static void _StageLogin(Loading.LoaderTask<int, int> task)
    {
        var context = Context;
        var existing = ProfileService.Current is { ProfileType: ProfileType.Microsoft } current ? current : null;
        LogWrapper.Info("Profile", $"验证方式：正版（{(existing is null ? "尚未登录" : existing.UserName)}）");
        McProfile profile;
        try
        {
            profile = ProfileService
                .AuthenticateAsync(
                    ProfileType.Microsoft,
                    new AuthenticationRequest
                    {
                        DeviceCodeHandler = LoginProfile.ShowDeviceCodeLoginAsync,
                        RefreshFailureHandler = (_, _) => Task.FromResult(false)
                    },
                    existing,
                    true,
                    task.AbortedToken)
                .GetAwaiter()
                .GetResult();
        }
        catch (IdentityModelAuthenticationException ex)
            when (LoginProfile.HandleMicrosoftXstsError(ex)
                  || LoginProfile.HandleMicrosoftNotOwnedError(ex)
                  || LoginProfile.HandleMicrosoftCreateProfileError(ex))
        {
            throw new IdentityModelException("$$", ex);
        }

        // Horizon 只为拥有 Java 版的正版账户启动：每次启动前都确认一次
        if (!MojangUtils.CheckLicenseAsync(profile, task.AbortedToken).GetAwaiter().GetResult())
        {
            LoginProfile.HandleMicrosoftNotOwnedError(
                new IdentityModelAuthenticationException("minecraft_not_owned", "The Microsoft account does not own Minecraft."));
            throw new Exception("$$");
        }

        context.Session = new HorizonSession(profile.UserName, profile.Uuid, profile.AccessToken);
        LaunchUi.RefreshLaunchPage(false);
        Log($"已验证正版账户 {profile.UserName}");
    }

    private static void _StageCheck(Loading.LoaderTask<int, int> task)
    {
        var context = Context;
        var check = HorizonPrepareService
            .CheckAsync(context.Build!, context.Prepared, task.AbortedToken)
            .GetAwaiter()
            .GetResult();
        context.NeedPrepare = !check.IsCurrent;
        if (check.IsCurrent)
            Log("游戏资源已就绪");
        else
            Log($"游戏资源需要准备：{string.Join("；", check.Gaps)}");
    }

    private static void _StageCoreFiles(Loading.LoaderTask<int, List<DownloadFile>> task)
    {
        var context = Context;
        if (!context.NeedPrepare)
        {
            task.output = [];
            return;
        }

        var minecraft = context.Minecraft!;
        context.VersionJson = HorizonGameFiles.EnsureVersionJson(context.Paths, minecraft);
        task.output = HorizonGameFiles.CollectCoreFiles(context.Paths, minecraft, context.VersionJson);
        Log($"需要下载 Minecraft {minecraft} 的 {task.output.Count} 个文件");
    }

    private static void _StageAssets(Loading.LoaderTask<int, List<DownloadFile>> task)
    {
        var context = Context;
        if (!context.NeedPrepare)
        {
            task.output = [];
            return;
        }

        task.output = HorizonGameFiles.CollectAssetObjects(context.Paths, context.Minecraft!, context.VersionJson!);
        Log($"需要下载 {task.output.Count} 个资源文件");
    }

    private static void _StageJava(Loading.LoaderTask<int, int> task)
    {
        var context = Context;
        if (!context.NeedPrepare)
            return;
        var major = context.Api!.JavaMajor
            ?? throw new Exception(Lang.Text("Horizon.Launch.Error.NoJavaMajor"));
        context.JavaHome = HorizonJava.EnsureJavaHome(
            task,
            major,
            HorizonGameFiles.GetJavaComponent(context.VersionJson!, major));
    }

    private static void _StagePrepare(Loading.LoaderTask<int, int> task)
    {
        var context = Context;
        if (!context.NeedPrepare)
            return;

        var minecraft = context.Minecraft!;
        Log("开始准备游戏资源，首次准备需要几分钟");
        HorizonPrepareService
            .PrepareAsync(
                context.Build!,
                new HorizonPrepareRequest
                {
                    Output = context.Prepared,
                    MinecraftDir = context.Paths.MinecraftDir,
                    ServerJar = context.Paths.MinecraftServerJar(minecraft),
                    JavaHome = context.JavaHome
                },
                new Progress<HorizonPrepareProgress>(p =>
                {
                    if (p.Fraction is { } fraction)
                        task.Progress = Math.Clamp(fraction, 0, 1);
                    if (!string.IsNullOrWhiteSpace(p.Message))
                        Log(p.Message);
                }),
                task.AbortedToken)
            .GetAwaiter()
            .GetResult();
        Log("游戏资源准备完成");
    }

    private static void _StageStart(Loading.LoaderTask<int, int> task)
    {
        var context = Context;
        var request = HorizonLaunchRequest.For(context.Paths, context.Instance, context.Build!, context.Session!) with
        {
            QuickPlaySingleplayer = context.Options.QuickPlayWorld,
            QuickPlayMultiplayer = context.Options.QuickPlayServer
        };
        Log($"启动参数：{string.Join(" ", HorizonLaunchService.BuildArguments(request, context.Api!.Client))}");
        var game = HorizonLaunchService.StartAsync(request, null, task.AbortedToken).GetAwaiter().GetResult();
        context.Game = game;
        context.Instance.MarkPlayed();
        States.System.LaunchCount += 1;
        HorizonGames.Track(new RunningGame(context.Instance, context.Build!, game, context.Api.Client));
        Log($"游戏进程已启动（PID {game.Process.Id}）");
    }

    private static void _StageWaitWindow(Loading.LoaderTask<int, int> task)
    {
        var game = Context.Game!;
        // 游戏用 winit 创建窗口；等到出现主窗口、进程退出或 60 秒后继续
        var deadline = DateTime.Now.AddSeconds(60);
        while (DateTime.Now < deadline && !task.IsAborted)
        {
            game.Process.Refresh();
            if (game.Process.HasExited)
                throw new Exception(Lang.Text("Horizon.Launch.Error.ExitedEarly", game.Process.ExitCode));
            if (game.Process.MainWindowHandle != IntPtr.Zero)
            {
                Log("游戏窗口已出现");
                return;
            }

            Thread.Sleep(200);
        }
    }

    private static void _StageEnd(Loading.LoaderTask<int, int> task)
    {
        switch (Config.Launch.LauncherVisibility)
        {
            case LauncherVisibility.ExitImmediately:
                Log("已根据设置，在启动后关闭启动器");
                LaunchUi.SetMainWindowState(LaunchWindowState.Exit);
                break;
            case LauncherVisibility.HideAndExit:
            case LauncherVisibility.HideAndReopen:
                Log("已根据设置，在启动后隐藏启动器");
                LaunchUi.SetMainWindowState(LaunchWindowState.Hide);
                break;
            case LauncherVisibility.MinimizeAndReopen:
                Log("已根据设置，在启动后最小化启动器");
                LaunchUi.SetMainWindowState(LaunchWindowState.Minimize);
                break;
        }
    }

    #endregion

    private static void _ReportFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (!current.Message.StartsWithF("$"))
                continue;
            if (current.Message != "$$")
                LaunchUi.ShowMessageBox(
                    Lang.Text("Minecraft.Launch.Error.SpecialMessage.WithDetail", current.Message.TrimStart('$')),
                    Lang.Text("Launch.Error.Title"));
            return;
        }

        Log($"错误：{ex}");
        LauncherCommon.Log(
            ex,
            "Horizon launch failed",
            LauncherLogLevel.Msgbox,
            Lang.Text("Launch.Error.Title"),
            Lang.Text("Minecraft.Launch.Error.LaunchFailed"));
    }

    private sealed class LaunchContext(HorizonLaunchOptions options, HorizonInstance instance, HorizonPaths paths)
    {
        public HorizonLaunchOptions Options { get; } = options;
        public HorizonInstance Instance { get; } = instance;
        public HorizonPaths Paths { get; } = paths;
        public HorizonBuild? Build { get; set; }
        public HorizonBuildApi? Api { get; set; }
        public string? Minecraft { get; set; }
        public HorizonSession? Session { get; set; }
        public bool NeedPrepare { get; set; }
        public JsonObject? VersionJson { get; set; }
        public string? JavaHome { get; set; }
        public HorizonGameProcess? Game { get; set; }
        public string Prepared => Paths.PreparedFor(Build!.Version);
    }
}

/// <summary>
///     一次启动的参数。
/// </summary>
public sealed record HorizonLaunchOptions
{
    /// <summary>
    ///     要启动的实例，为空则使用当前选中的实例。
    /// </summary>
    public HorizonInstance? Instance { get; init; }

    /// <summary>
    ///     启动后直接进入的存档（<c>saves/</c> 下的文件夹名）。
    /// </summary>
    public string? QuickPlayWorld { get; init; }

    /// <summary>
    ///     启动后直接加入的服务器（<c>HOST[:PORT]</c>）。
    /// </summary>
    public string? QuickPlayServer { get; init; }
}
