using System.Text;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using PCL.Horizon;
using PCL.Network.Loaders;

namespace PCL;

public static partial class Comp
{
    /// <summary>
    ///     资源卡片的快速下载入口：按 <see cref="Config.Download.Comp.QuickDownloadBehavior" /> 指定的行为，
    ///     下载该资源最新（优先 Release、其次最新发布）的兼容版本到目标实例或文件夹。
    ///     由 <see cref="MyCompItem" /> 上的快速下载按钮调用。快速下载不会自动安装前置。
    /// </summary>
    public static void QuickDownload(CompProject project)
    {
        LauncherCommon.RunInNewThread(
            () =>
            {
                try
                {
                    LauncherUi.Interaction?.ShowHint(
                        Lang.Text("Download.Comp.QuickDownload.Hint.Loading"));
                    var files = FilterFilesByType(
                        [
                            .. CompFilesGet(project.Id)
                                .Where(f => f.Available)
                        ],
                        project.Type);
                    if (files.Count == 0)
                    {
                        LauncherUi.Interaction?.ShowHint(
                            Lang.Text("Download.Comp.QuickDownload.Hint.NoFile"));
                        return;
                    }

                    var behavior = Config.Download.Comp.QuickDownloadBehavior;
                    if (behavior == 0)
                    {
                        var choice = LauncherCommon.RunInUiWait(() =>
                            CompUi.Current?.ShowSelect(
                                Lang.Text("Download.Comp.QuickDownload.ChooseMethod.Title"),
                                null,
                                [
                                    Lang.Text(
                                        "Download.Comp.QuickDownload.ChooseMethod.CurrentInstance"),
                                    Lang.Text(
                                        "Download.Comp.QuickDownload.ChooseMethod.AskInstance"),
                                    Lang.Text(
                                        "Download.Comp.QuickDownload.ChooseMethod.AskPath")
                                ],
                                Lang.Text("Common.Action.Continue"),
                                Lang.Text("Common.Action.Cancel")));
                        if (choice is null) return;
                        behavior = choice.Value + 1;
                    }

                    var file = _PickLatestFile(files);
                    if (file is null)
                    {
                        LauncherUi.Interaction?.ShowHint(
                            Lang.Text("Download.Comp.QuickDownload.Hint.NoFile"));
                        return;
                    }

                    switch (behavior)
                    {
                        case 1:
                            _QuickDownloadToInstance(
                                project,
                                file,
                                HorizonEnvironment.SelectedInstance);
                            break;
                        case 2:
                        {
                            var instances = _QuickDownloadPickInstances();
                            if (instances is null) return;
                            var startedTargets =
                                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var instance in instances)
                                _QuickDownloadToInstance(project, file, instance, startedTargets);
                            break;
                        }
                        case 3:
                            _QuickDownloadToFolder(project, file);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    LauncherCommon.Log(
                        ex,
                        "[Comp] 快速下载失败",
                        LauncherLogLevel.Feedback,
                        userSummary: Lang.Text("Minecraft.Comp.Error.OperationFailed"));
                }
            },
            "Comp QuickDownload");
    }

    /// <summary>下载到指定实例。数据包需要先选择一个存档。</summary>
    private static void _QuickDownloadToInstance(
        CompProject project,
        CompFile file,
        HorizonInstance? instance,
        ISet<string>? startedTargets = null)
    {
        if (instance is null)
        {
            LauncherUi.Interaction?.ShowHint(
                Lang.Text("Download.Comp.QuickDownload.Hint.NoInstance"));
            return;
        }

        var folder = GetInstanceFolder(instance, project.Type, true);
        if (folder is null) return;
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, CompFileNameGet(project, file));
        if (startedTargets is not null && !startedTargets.Add(target))
            return;
        _StartQuickDownload(file, target);
        LauncherUi.Interaction?.ShowHint(
            Lang.Text("Download.Comp.QuickDownload.Hint.DownloadStarted", project.RawName),
            NotificationType.Success);
    }

    /// <summary>
    ///     返回某类资源在 Horizon 实例内的存放文件夹。
    ///     数据包存放在存档内：<paramref name="askWorld" /> 为 true 时让用户选择存档，否则返回存档文件夹。
    ///     不支持的资源类型或用户取消时返回 null。
    /// </summary>
    public static string? GetInstanceFolder(HorizonInstance instance, CompType type, bool askWorld)
    {
        switch (type)
        {
            case CompType.ResourcePack:
                return instance.ResourcePacksDir;
            case CompType.Shader:
                return instance.ShaderPacksDir;
            case CompType.World:
                return instance.SavesDir;
            case CompType.DataPack:
            {
                if (!askWorld) return instance.SavesDir;
                var worlds = Directory.Exists(instance.SavesDir)
                    ? Directory.GetDirectories(instance.SavesDir)
                        .Where(d => File.Exists(Path.Combine(d, "level.dat")))
                        .OrderByDescending(Directory.GetLastWriteTimeUtc)
                        .ToList()
                    : [];
                if (worlds.Count == 0)
                {
                    LauncherUi.Interaction?.ShowHint(
                        Lang.Text("Download.Comp.QuickDownload.Hint.NoWorld", instance.Name));
                    return null;
                }

                var index = worlds.Count == 1
                    ? 0
                    : LauncherCommon.RunInUiWait(() =>
                        CompUi.Current?.ShowSelect(
                            Lang.Text("Download.Comp.QuickDownload.ChooseWorld.Title", instance.Name),
                            null,
                            worlds.Select(d => Path.GetFileName(d)).ToList(),
                            Lang.Text("Common.Action.Continue"),
                            Lang.Text("Common.Action.Cancel")));
                return index is null ? null : instance.DatapacksDir(Path.GetFileName(worlds[index.Value]));
            }
            default:
                return null;
        }
    }

    /// <summary>弹实例列表让用户选择（当前选中实例居首）；取消或没有实例返回 null。</summary>
    private static List<HorizonInstance>? _QuickDownloadPickInstances()
    {
        var current = HorizonEnvironment.SelectedInstance;
        List<HorizonInstance> instances =
        [
            .. HorizonEnvironment.Instances
                .OrderBy(v => v.Id == current?.Id ? 0 : 1)
                .ThenBy(v => v.Name)
        ];
        if (instances.Count == 0)
        {
            LauncherUi.Interaction?.ShowHint(
                Lang.Text("Download.Comp.QuickDownload.Hint.NoInstance"));
            return null;
        }

        var indices = LauncherCommon.RunInUiWait(() =>
            CompUi.Current?.ShowMultiSelect(
                Lang.Text("Download.Comp.QuickDownload.ChooseInstance.Title"),
                null,
                instances.Select(v => v.Name).ToList(),
                Lang.Text("Common.Action.Continue"),
                Lang.Text("Common.Action.Cancel")));
        if (indices is null) return null;
        return [.. indices.Select(i => instances[i])];
    }

    /// <summary>下载最新版本到用户选择的文件夹。</summary>
    private static void _QuickDownloadToFolder(CompProject project, CompFile file)
    {
        var saveFolder = LauncherCommon.RunInUiWait(() =>
            SystemDialogs.SelectFolder(Lang.Text("Download.Comp.QuickDownload.Hint.SelectFolder")));
        if (string.IsNullOrWhiteSpace(saveFolder)) return;
        var target = Path.Combine(saveFolder, CompFileNameGet(project, file));
        _StartQuickDownload(file, target);
        LauncherUi.Interaction?.ShowHint(
            Lang.Text("Download.Comp.QuickDownload.Hint.DownloadStarted", project.RawName),
            NotificationType.Success);
    }

    /// <summary>构造并启动单文件下载任务（与详情页 Save_Click 末段一致）。</summary>
    private static void _StartQuickDownload(CompFile file, string target)
    {
        var desc = file.Type switch
        {
            CompType.Mod => Lang.Text("Download.Comp.Type.Mod"),
            CompType.ResourcePack => Lang.Text("Download.Comp.Type.ResourcePack"),
            CompType.Shader => Lang.Text("Download.Comp.Type.Shader"),
            CompType.DataPack => Lang.Text("Download.Comp.Type.DataPack"),
            CompType.World => Lang.Text("Download.Comp.Type.World"),
            _ => Lang.Text("Download.Comp.Type.Mod")
        };
        var loaderName = Lang.Text(
            "Download.Comp.Detail.DownloadResource",
            desc,
            Path.GetFileNameWithoutExtension(target));
        var loaders = new List<Loading.LoaderBase>
        {
            new LoaderDownload(
                Lang.Text("Download.Comp.Detail.DownloadFile"),
                [
                    file.Type == CompType.Mod
                        ? file.ToNetFile(target)
                        : file.ToNetFile(target, DownloadReason.Standalone, null)
                ])
            {
                ProgressWeight = 6,
                block = true
            }
        };
        if (file.Type == CompType.World)
        {
            var extractDir = Path.GetDirectoryName(target);
            if (extractDir is not null)
                loaders.Add(
                    new Loading.LoaderTask<int, int>(
                        Lang.Text("Download.Comp.Detail.InstallWorld"),
                        _ => LauncherCommon.ExtractFile(target, extractDir, Encoding.UTF8))
                    {
                        ProgressWeight = 0.1d,
                        block = true
                    });
            loaders.Add(
                new Loading.LoaderTask<int, int>(
                    Lang.Text("Download.Comp.Detail.CleanCache"),
                    _ => File.Delete(target)));
        }

        var loader = new Loading.LoaderCombo<int>(loaderName, loaders)
        {
            OnStateChanged = loader => CompUi.Current?.LoaderStateChangedHintOnly(loader)
        };
        loader.Start(1);
        Loading.LoaderTaskbarAdd(loader);
        CompUi.Current?.RefreshDownloadButton();
    }

    /// <summary>
    ///     按资源类型筛选文件，与详情页 GetResults 一致：Modrinth 会返回 Mod / 服务端插件 / 数据包混合的列表，
    ///     需过滤回当前类型，避免快速下载到另一种产物。光影与资源包不筛（原版光影以资源包格式发布）。
    /// </summary>
    private static List<CompFile> FilterFilesByType(List<CompFile> files, CompType type)
    {
        return type is CompType.Shader or CompType.ResourcePack
            ? files
            : ([.. files.Where(f => f.Type == type)]);
    }

    /// <summary>挑选最新文件：优先 Release，其次按发布日期最新。compatFilter 为空时不做兼容过滤。</summary>
    private static CompFile? _PickLatestFile(
        List<CompFile> files,
        Func<CompFile, bool>? compatFilter = null)
    {
        var candidates = (compatFilter is null
            ? files
            : files.Where(compatFilter)).ToList();
        if (candidates.Count == 0) return null;
        return candidates
            .OrderByDescending(f => f.Status == CompFileStatus.Release)
            .ThenByDescending(f => f.ReleaseDate)
            .First();
    }
}