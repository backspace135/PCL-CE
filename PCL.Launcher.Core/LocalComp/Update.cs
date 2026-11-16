using PCL.Core.Utils;
using static PCL.Comp;
using static PCL.Loading;

namespace PCL;

public static partial class LocalComp
{
    public static readonly LoaderTask<CompLocalLoaderData, int> CompUpdateDetailLoader =
        new("Comp List Detail Loader", CompUpdateDetailLoad);

    private static void CompUpdateDetailLoad(LoaderTask<CompLocalLoaderData, int> loader)
    {
        var data = loader.input ?? throw new InvalidOperationException("资源更新加载器输入为空");
        var mods = data.detailInfo.Key;
        var cache = data.detailInfo.Value;
        var modLoaders = data.loaders;
        var compType = data.compType;
        var mcInstance = data.gameVersion.Info.VanillaName;

        LauncherCommon.Log($"[Mod] 目标加载器：{string.Join("/", modLoaders)}，版本：{mcInstance}");
        var endedThreadCount = 0;
        var isFailed = false;
        var currentTaskId = Task.CurrentId ?? -1;

        StartUpdateProvider(
            "Mod List Detail Loader Modrinth",
            () =>
                _LoadModrinthUpdates(
                    mods,
                    mcInstance,
                    modLoaders,
                    compType,
                    loader,
                    currentTaskId));

        while (endedThreadCount < 1)
        {
            if (loader.IsAborted)
                return;
            Thread.Sleep(10);
        }

        if (!_SaveUpdateCache(cache, mods, mcInstance, modLoaders, isFailed))
            return;

        _RefreshUpdateList();
        return;

        void StartUpdateProvider(string name, Func<bool> load)
        {
            LauncherCommon.RunInNewThread(
                () =>
                {
                    try
                    {
                        if (load())
                            isFailed = true;
                    }
                    finally
                    {
                        Interlocked.Increment(ref endedThreadCount);
                    }
                },
                name);
        }
    }

    private static void _RefreshUpdateList()
    {
        CompLocalUi.Current?.RefreshModList();
    }

    private static bool _SaveUpdateCache(
        JsonObject cache,
        List<LocalCompFile> mods,
        string mcInstance,
        List<CompLoaderType> modLoaders,
        bool isFailed)
    {
        var cachedMods = mods.Where(m => m.Comp is not null).ToList();
        LauncherCommon.Log($"[Mod] 联网获取本地 Mod 信息完成，为 {cachedMods.Count} 个 Mod 更新缓存");
        if (cachedMods.Count == 0)
            return false;

        foreach (var entry in cachedMods)
        {
            entry.CompLoaded = !isFailed;
            cache[entry.ModrinthHash + mcInstance + string.Join("", modLoaders)] = entry.ToJson();
        }

        LauncherCommon.WriteFile(
            Path.Combine(LauncherPaths.Temp, "Cache", "LocalComp.json"),
            cache.ToJsonString(
                LauncherCommon.ModeDebug
                    ? new JsonSerializerOptions(JsonCompat.SerializerOptions)
                    {
                        WriteIndented = true
                    }
                    : null));

        return true;
    }

    private static bool _LoadModrinthUpdates(
        List<LocalCompFile> mods,
        string mcInstance,
        List<CompLoaderType> modLoaders,
        CompType compType,
        LoaderTask<CompLocalLoaderData, int> loader,
        int currentTaskId)
    {
        try
        {
            var modrinthVersion = _FetchModrinthVersionMap(mods);
            LauncherCommon.Log($"[Mod] 从 Modrinth 获取到 {modrinthVersion.Count} 个本地 Mod 的对应信息");

            if (modrinthVersion.Count == 0)
                return false;
            var modrinthMapping = _MapModrinthLocalFiles(mods, modrinthVersion);

            if (loader.IsAbortedWithThread(currentTaskId))
                return false;
            LauncherCommon.Log($"[Mod] 需要从 Modrinth 获取 {modrinthMapping.Count} 个本地 Mod 的工程信息");

            if (modrinthMapping.Count == 0)
                return false;
            _ApplyModrinthProjects(modrinthMapping);

            LauncherCommon.Log("[Mod] 已从 Modrinth 获取本地 Mod 信息，继续获取更新信息");
            var modrinthUpdate = _FetchModrinthUpdateMap(
                modrinthMapping,
                mcInstance,
                modLoaders,
                compType);

            foreach (var entry in mods)
                _ApplyModrinthUpdate(entry, modrinthUpdate, mcInstance);

            LauncherCommon.Log("[Mod] 从 Modrinth 获取本地 Mod 信息结束");
            return false;
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(ex, "从 Modrinth 获取本地 Mod 信息失败");
            return true;
        }
    }

    private static JsonObject _FetchModrinthVersionMap(List<LocalCompFile> mods)
    {
        var hashes = string.Join("\",\"", mods.Select(m => m.ModrinthHash));
        var body = $$"""{"hashes": ["{{hashes}}"], "algorithm": "sha1"}""";
        return (JsonObject)
            LauncherCommon.GetJson(
                GameDownload.DlModRequest(
                    "https://api.modrinth.com/v2/version_files",
                    "POST",
                    body,
                    "application/json"));
    }

    private static void _ApplyModrinthProjects(
        Dictionary<string, List<LocalCompFile>> modrinthMapping)
    {
        var modrinthProject = (JsonArray)
            LauncherCommon.GetJson(
                GameDownload.DlModRequest(
                    $"https://api.modrinth.com/v2/projects?ids=[\"{string.Join("\",\"", modrinthMapping.Keys)}\"]",
                    "GET",
                    "",
                    "application/json"));

        foreach (var projectJson in modrinthProject)
        {
            if (projectJson is not JsonObject projectObject)
                continue;
            var project = new CompProject(projectObject);
            if (project.Id is null)
                continue;
            foreach (var entry in modrinthMapping[project.Id])
                entry.Comp = project;
        }
    }

    private static JsonObject _FetchModrinthUpdateMap(
        Dictionary<string, List<LocalCompFile>> modrinthMapping,
        string mcInstance,
        List<CompLoaderType> modLoaders,
        CompType compType)
    {
        var targetLoaders =
            compType == CompType.DataPack
                ? "datapack"
                : string.Join("\",\"", modLoaders).ToLower();
        var updateHashes = string.Join(
            "\",\"",
            modrinthMapping.SelectMany(l =>
                l.Value.Select(m => m.ModrinthHash)));
        var body = $$"""
                     {
                       "hashes": ["{{updateHashes}}"],
                       "algorithm": "sha1",
                       "loaders": ["{{targetLoaders}}"],
                       "game_versions": ["{{mcInstance}}"]
                     }
                     """;
        return (JsonObject)
            LauncherCommon.GetJson(
                GameDownload.DlModRequest(
                    "https://api.modrinth.com/v2/version_files/update",
                    "POST",
                    body,
                    "application/json"));
    }

    private static Dictionary<string, List<LocalCompFile>> _MapModrinthLocalFiles(
        List<LocalCompFile> mods,
        JsonObject modrinthVersion)
    {
        var modrinthMapping = new Dictionary<string, List<LocalCompFile>>();
        foreach (var entry in mods)
        {
            if (modrinthVersion[entry.ModrinthHash] is not JsonObject versionJson)
                continue;
            if (versionJson["files"]?[0]?["hashes"]?["sha1"]?.ToString() != entry.ModrinthHash)
                continue;

            var projectId = versionJson["project_id"]?.ToString();
            if (projectId is null)
                continue;
            if (compProjectCache.TryGetValue(projectId, out var value)
                && entry.Comp is null)
                entry.Comp = value;

            if (!modrinthMapping.ContainsKey(projectId))
                modrinthMapping[projectId] = [];
            modrinthMapping[projectId].Add(entry);

            var fileInfo = new CompFile(versionJson, CompType.Mod);
            if (entry.CompFile is null || entry.CompFile.ReleaseDate < fileInfo.ReleaseDate)
                entry.CompFile = fileInfo;
        }

        return modrinthMapping;
    }

    private static void _ApplyModrinthUpdate(
        LocalCompFile entry,
        JsonObject modrinthUpdate,
        string mcInstance)
    {
        if (modrinthUpdate[entry.ModrinthHash] is not JsonObject updateJson
            || entry.CompFile is null)
            return;

        var updateFile = new CompFile(updateJson, CompType.Mod);
        if (!updateFile.Available)
            return;

        if (LauncherCommon.ModeDebug)
            LauncherCommon.Log(
                $"[Mod] 本地文件 {entry.CompFile.FileName} 在 Modrinth 上的最新版为 {updateFile.FileName}");
        if (entry.CompFile.ReleaseDate >= updateFile.ReleaseDate
            || entry.CompFile.Hash == updateFile.Hash)
            return;

        var changelogUrl =
            $"https://modrinth.com/mod/{updateJson["project_id"]}/changelog?g={mcInstance}";

        if (entry.UpdateFile is not null
            && updateFile.Hash == entry.UpdateFile.Hash)
        {
            entry.ChangelogUrls.Add(changelogUrl);
            entry.UpdateFile.DownloadUrls.AddRange(updateFile.DownloadUrls);
            entry.UpdateFile = updateFile;
        }
        else if (entry.UpdateFile is null
                 || updateFile.ReleaseDate >= entry.UpdateFile.ReleaseDate)
        {
            entry.ChangelogUrls = [changelogUrl];
            entry.UpdateFile = updateFile;
        }
    }
}