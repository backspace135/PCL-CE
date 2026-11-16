using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Utils;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

public static partial class GameDownload
{
    private static readonly Lock AllDropsLock = new();

    /// <summary>
    ///     Minecraft 客户端 版本列表，主加载器。
    ///     若要求镜像源必须包含某个版本，则将该版本 ID 作为输入。
    /// </summary>
    public static Loading.LoaderTask<string, DlClientListResult> dlClientListLoader =
        new("DlClientList Main", DlClientListMain);

    /// <summary>
    ///     Minecraft 客户端 版本列表，Mojang 官方源加载器。
    /// </summary>
    public static Loading.LoaderTask<string, DlClientListResult> dlClientListMojangLoader =
        new("DlClientList Mojang", DlClientListMojangMain);

    /// <summary>
    ///     Minecraft 客户端 版本列表，BMCLAPI 源加载器。
    /// </summary>
    public static Loading.LoaderTask<string, DlClientListResult> dlClientListBmclapiLoader =
        new("DlClientList Bmclapi", DlClientListBmclapiMain);

    /// <summary>
    ///     所有正式版的 Minecraft Drop 序数。
    ///     若从未完成过获取，返回 Nothing；否则必定存在元素，且从高到低排列。
    /// </summary>
    public static List<int>? AllDrops
    {
        get
        {
            lock (AllDropsLock)
            {
                field ??= _LoadAllDrops();
                return field.Count != 0 ? field : null;
            }
        }
        set
        {
            lock (AllDropsLock)
            {
                field = value;
                States.Game.Drops = (value ?? []).Join(",");
            }
        }
    }

    private static List<int> _LoadAllDrops()
    {
        var rawData = States.Game.Drops;
        if (string.IsNullOrEmpty(rawData))
            return [];

        return
        [
            .. rawData
                .Split([","], StringSplitOptions.RemoveEmptyEntries)
                .Select(d => (int)Math.Round(LauncherText.Val(d)))
        ];
    }

    private static void DlClientListMain(Loading.LoaderTask<string, DlClientListResult> loader)
    {
        DlSourceLoader(
            loader,
            _GetClientListSources(),
            loader.isForceRestarting);

        var drops = (loader.output.Value["versions"]?.AsArray() ?? [])
            .OfType<JsonObject>()
            .Select(version => McInstanceInfo.VersionToDrop(version["id"]?.ToString() ?? ""))
            .ToList();
        AllDrops = [.. drops.Distinct().OrderByDescending(d => d)];
    }

    private static List<KeyValuePair<Loading.LoaderTask<string, DlClientListResult>, int>>
        _GetClientListSources()
    {
        return Config.Download.VersionListSource switch
        {
            0 =>
            [
                new KeyValuePair<Loading.LoaderTask<string, DlClientListResult>, int>(
                    dlClientListBmclapiLoader,
                    30),
                new KeyValuePair<Loading.LoaderTask<string, DlClientListResult>, int>(
                    dlClientListMojangLoader,
                    30 + 60)
            ],
            1 =>
            [
                new KeyValuePair<Loading.LoaderTask<string, DlClientListResult>, int>(
                    dlClientListMojangLoader,
                    5),
                new KeyValuePair<Loading.LoaderTask<string, DlClientListResult>, int>(
                    dlClientListBmclapiLoader,
                    5 + 30)
            ],
            _ =>
            [
                new KeyValuePair<Loading.LoaderTask<string, DlClientListResult>, int>(
                    dlClientListMojangLoader,
                    60),
                new KeyValuePair<Loading.LoaderTask<string, DlClientListResult>, int>(
                    dlClientListBmclapiLoader,
                    60 + 60)
            ]
        };
    }

    private static void DlClientListMojangMain(
        Loading.LoaderTask<string, DlClientListResult> loader)
    {
        var startTime = TimeUtils.GetTimeTick();
        var json = (JsonObject)Requester.FetchJson(
            "https://launchermeta.mojang.com/mc/game/version_manifest.json");
        try
        {
            var versions = json["versions"] as JsonArray ?? [];
            _ValidateVersionList(versions, "Mojang", json);
            _MergeUnlistedVersions(versions, "官方源");

            _UpdateMojangSourcePreference(startTime);

            loader.output = new DlClientListResult
            {
                IsOfficial = true,
                SourceName = Lang.Text("Download.Source.MojangOfficial"),
                Value = json
            };
        }
        catch (Exception ex)
        {
            throw new Exception(
                Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "Mojang", ""),
                ex);
        }
    }

    private static void _ValidateVersionList(JsonArray versions, string sourceName, JsonObject json)
    {
        if (versions.Count < 200)
            throw new Exception(
                Lang.Text(
                    "Minecraft.Download.Error.VersionListOperationFailed",
                    sourceName,
                    json));
    }

    private static void _MergeUnlistedVersions(JsonArray versions, string sourceDescription)
    {
        var cacheFilePath = $@"{LauncherPaths.Temp}Cache\uvmc-download.json";
        if (!File.Exists(cacheFilePath))
            try
            {
                var unlistedJson = (JsonObject)Requester.FetchJson(
                    "https://alist.8mi.tech/d/mirror/unlisted-versions-of-minecraft/Auto/version_manifest.json");
                File.WriteAllText(cacheFilePath, unlistedJson.ToString());
            }
            catch (Exception ex)
            {
                LauncherCommon.Log($"[Download] 未列出的版本{sourceDescription}下载失败: {ex.Message}");
            }

        try
        {
            var cachedJson =
                (JsonObject)LauncherCommon.GetJson(LauncherCommon.ReadFile(cacheFilePath));
            versions.Merge(cachedJson["versions"]);
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(ex, "[Download] UVMC 列表加载失败，忽略列表内容");
        }
    }

    private static void _UpdateMojangSourcePreference(long startTime)
    {
        if (DownloadSources.PreferMojang)
            return;

        var deltaTime = TimeUtils.GetTimeTick() - startTime;
        DownloadSources.PreferMojang = deltaTime < 4000;
        LauncherCommon.Log(
            $"[Download] Mojang 官方源加载耗时：{deltaTime}ms，{(DownloadSources.PreferMojang ? "可优先使用官方源" : "不优先使用官方源")}");
    }

    private static void DlClientListBmclapiMain(
        Loading.LoaderTask<string, DlClientListResult> loader)
    {
        var json = (JsonObject)Requester.FetchJson(
            "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json");
        try
        {
            var versions = json["versions"] as JsonArray ?? [];
            _ValidateVersionList(versions, "BMCLAPI", json);
            _MergeUnlistedVersions(versions, "镜像源");
            _EnsureBmclapiHasInputVersion(loader);

            loader.output = new DlClientListResult
            {
                IsOfficial = false,
                SourceName = "BMCLAPI",
                Value = json
            };
        }
        catch (Exception ex)
        {
            throw new Exception(
                Lang.Text("Minecraft.Download.Error.VersionListOperationFailed", "BMCLAPI", json),
                ex);
        }
    }

    private static void _EnsureBmclapiHasInputVersion(
        Loading.LoaderTask<string, DlClientListResult> loader)
    {
        if (string.IsNullOrEmpty(loader.input))
            return;

        var id = loader.input;
        if (dlClientListLoader.output is var output
            && (output.Value["versions"]?.AsArray() ?? [])
            .OfType<JsonObject>()
            .All(v => v["id"]?.ToString() != id))
            throw new Exception(
                Lang.Text("Minecraft.Download.Error.BmclapiMissingTargetVersion", id));
    }

    /// <summary>
    ///     获取某个版本的 Json 下载地址，若失败则返回 Nothing。必须在工作线程执行。
    /// </summary>
    public static object? DlClientListGet(string id)
    {
        try
        {
            id = _NormalizeClientVersionId(id);
            switch (dlClientListLoader.State)
            {
                case LoadState.Finished:
                {
                    var cachedUrl = _FindClientVersionUrl(id);
                    if (cachedUrl is not null)
                        return cachedUrl;
                    dlClientListLoader.WaitForExit(id, isForceRestart: true);
                    break;
                }
                case LoadState.Loading:
                {
                    dlClientListLoader.WaitForExit(id);
                    break;
                }
                case LoadState.Failed
                    or LoadState.Aborted
                    or LoadState.Waiting:
                {
                    dlClientListLoader.WaitForExit(id, isForceRestart: true);
                    break;
                }
            }

            var versionUrl = _FindClientVersionUrl(id);
            if (versionUrl is not null)
                return versionUrl;

            LauncherCommon.Log(
                $"未发现版本 {id} 的 json 下载地址，版本列表返回为：\r\n{dlClientListLoader.output.Value}",
                LauncherLogLevel.Debug);
            return null;
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(ex, $"获取版本 {id} 的 json 下载地址失败");
            return null;
        }
    }

    private static string _NormalizeClientVersionId(string id)
    {
        id = id.Replace("_", "-");
        if (id != "1.0" && id.EndsWithF(".0"))
            id = id[..^2]; // OptiFine 1.8 的下载会触发此问题，显示版本为 1.8.0
        return id;
    }

    private static string? _FindClientVersionUrl(string id)
    {
        return dlClientListLoader.output is var output
            ? (output.Value["versions"]?.AsArray() ?? [])
            .OfType<JsonObject>()
            .Where(version => version["id"]?.ToString() == id)
            .Select(version => version["url"]?.ToString())
            .FirstOrDefault()
            : null;
    }

    public struct DlClientListResult
    {
        /// <summary>
        ///     数据来源名称，如“Mojang”，“BMCLAPI”。
        /// </summary>
        public string SourceName;

        /// <summary>
        ///     是否为官方的实时数据。
        /// </summary>
        public bool IsOfficial;

        /// <summary>
        ///     获取到的 Json 数据。
        /// </summary>
        public JsonObject Value;
    }
}