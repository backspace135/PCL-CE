using System.Collections.Concurrent;
using System.Net;
using Dapper;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.Utils;

namespace PCL;

public static partial class Comp
{
    private const int CompPageSize = 40;

    /// <summary>
    ///     已知工程信息的缓存。
    /// </summary>
    public static ConcurrentDictionary<string, CompProject> compProjectCache = new();

    /// <summary>
    ///     用 Uri 解析单个 token 是否为受支持的 Modrinth 资源链接。
    ///     成功时输出分类段与 slug（query、fragment 会被自动丢弃）。
    /// </summary>
    private static bool TryParseResourceLink(
        string token,
        out string category,
        out string slug)
    {
        category = string.Empty;
        slug = string.Empty;

        if (!Uri.TryCreate(token, UriKind.Absolute, out var uri)
            && !Uri.TryCreate($"https://{token}", UriKind.Absolute, out uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var segments = uri.AbsolutePath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);

        if (IsHostOf(uri.Host, "modrinth.com"))
        {
            if (segments.Length < 2) return false;
            category = segments[0];
            slug = segments[1];
            return true;
        }

        return false;
    }

    /// <summary>host 等于 domain 或为其子域，避免 evilmodrinth.com 之类的伪装域名。</summary>
    private static bool IsHostOf(string host, string domain)
    {
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>从文本中找出首个可识别的资源链接 token，找不到返回 null。</summary>
    private static string? FindFirstResourceLinkToken(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? null
            : text
                .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(token => TryParseResourceLink(
                    token,
                    out _,
                    out _));
    }

    /// <summary>
    ///     从单条 Modrinth 资源链接解析出 projectId。无法识别或获取失败时返回 null。
    /// </summary>
    public static string? ResolveLinkToProjectId(string url)
    {
        if (!TryParseResourceLink(
                url,
                out _,
                out var slug))
        {
            var token = FindFirstResourceLinkToken(url);
            if (token is null || !TryParseResourceLink(token, out _, out slug))
                return null;
        }

        var mr = GameDownload.DlModRequest<JsonObject>(
            $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(slug)}");
        return mr["id"]?.ToString();
    }

    /// <summary>
    ///     若输入文本中恰好含 1 条 Modrinth 资源链接，返回该链接；含 0 条或 ≥2 条时返回 null。
    /// </summary>
    public static string? TryExtractSingleResourceLink(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var tokens = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        string? found = null;
        foreach (var token in tokens)
        {
            if (!TryParseResourceLink(token, out _, out _))
                continue;
            if (found is not null)
                return null;
            found = token;
        }

        return found;
    }

    /// <summary>
    ///     根据搜索请求获取一系列的工程列表。需要基于加载器运行。
    /// </summary>
    public static void CompProjectsGet(Loading.LoaderTask<CompProjectRequest, int> task)
    {
        var request = task.input;
        if (request is null) return;
        var storage = request.storage;

        var singleLink = TryExtractSingleResourceLink(request.searchText);
        if (singleLink is not null)
        {
            _AddSingleLinkResult(singleLink, request, storage);
            return;
        }

        if (storage.results.Count >= request.targetResultCount)
        {
            LogWrapper.Info(
                $"[Comp] 已有 {storage.results.Count} 个结果，多于所需的 {request.targetResultCount} 个结果，结束处理");
            return;
        }

        if (!request.CanContinue)
        {
            if (storage.results.Count == 0)
                throw new Exception(Lang.Text("Download.Comp.List.NoMatchingResults"));

            LogWrapper.Info(
                $"[Comp] 已有 {storage.results.Count} 个结果，少于所需的 {request.targetResultCount} 个结果，但无法继续获取，结束处理");
            return;
        }

        if (request.modLoader == CompLoaderType.Quilt
            && McVersionComparer.CompareVersion(request.gameVersion ?? "1.15", "1.14") == -1)
            throw new Exception(Lang.Text("Minecraft.Error.QuiltUnsupported", request.gameVersion));

        var rawFilter = request.searchText.Trim();
        request.searchText = rawFilter;
        _ = rawFilter.ToLower();
        LogWrapper.Info($"[Comp] 工程列表搜索原始文本：{rawFilter}");

        var isChineseSearch = Lang.IsChineseMainland
            && RegexPatterns.HasChineseChar.IsMatch(rawFilter)
            && !string.IsNullOrEmpty(rawFilter);

        request.searchText = _ProcessKeywords(request.searchText, rawFilter)!;
        LogWrapper.Debug($"[Comp] 工程列表搜索最终文本：{request.searchText}");
        task.Progress = 0.1;

        var realResults = _FetchProjectResults(request, storage, task);
        if (realResults is null)
            return;

        if (request.sort != CompSortType.Default)
        {
            if (task.IsAborted) throw new ThreadInterruptedException();
            storage.results.AddRange(realResults);
            return;
        }

        var scores = _ScoreProjectResults(request, realResults, rawFilter, isChineseSearch);

        if (task.IsAborted) throw new ThreadInterruptedException();
        storage.results.AddRange(
            scores
                .OrderByDescending(s => s.Value)
                .Select(s => s.Key));
    }

    private static void _AddSingleLinkResult(
        string singleLink,
        CompProjectRequest request,
        CompProjectStorage storage)
    {
        if (storage.results.Count != 0) return;

        CompProject? project;
        try
        {
            var projectId = ResolveLinkToProjectId(singleLink);
            project = string.IsNullOrEmpty(projectId)
                ? null
                : CompRequest.GetCompProjectsByIds([projectId]).FirstOrDefault();
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(ex, "[Comp] 解析资源链接失败");
            throw new Exception(Lang.Text("Download.Comp.Link.ResolveFailed"));
        }

        if (project is null)
            throw new Exception(Lang.Text("Download.Comp.Link.ResolveFailed"));

        if (request.type != CompType.Any && project.Type != request.type)
            throw new Exception(Lang.Text("Download.Comp.List.NoMatchingResults"));

        storage.results.Add(project);
        storage.modrinthTotal = 0;
    }

    private static string? _ProcessKeywords(string? text, string rawFilter)
    {
        if (text is null) return null;

        text = text.ToLowerInvariant();
        var words = new List<string>();
        foreach (var keyword in text.Split(
                     [' '],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var cleanKeyword = keyword.Trim('[', ']');
            if (string.IsNullOrEmpty(cleanKeyword)) continue;
            if (new[]
                {
                    "forge",
                    "fabric",
                    "for",
                    "mod",
                    "quilt"
                }.Contains(cleanKeyword))
            {
                LogWrapper.Debug($"[Comp] 已跳过搜索关键词：{cleanKeyword}");
                continue;
            }

            words.Add(cleanKeyword);
        }

        text = rawFilter.Length > 0 && words.Count == 0
            ? rawFilter
            : string.Join(" ", words.Distinct());

        var compactFilter = rawFilter.Replace(" ", "");
        if (compactFilter.ContainsF("optiforge", true))
            text = "optiforge";
        if (compactFilter.ContainsF("optifabric", true))
            text = "optifabric";

        return text;
    }

    private static List<CompProject>? _FetchProjectResults(
        CompProjectRequest request,
        CompProjectStorage storage,
        Loading.LoaderTask<CompProjectRequest, int> task)
    {
        var realResults = new List<CompProject>();

        while (true)
        {
            var rawResults = new List<CompProject>();
            Exception? lastError = null;
            var resultsLock = new object();

            // 1.14- 的 Forge 老 Mod 常常没有声明自己支持的加载器，因此查询时不带加载器条件，
            // 之后再按「未声明加载器或声明了 Forge」过滤。
            // 注意必须要求版本可解析：VersionToDrop 对 null/无法识别的版本返回 -1，
            // 若直接比较会把「不选版本 + Forge」误判为老版本，从而丢掉 Forge 筛选条件。
            var gameVersionDrop = McInstanceInfo.VersionToDrop(request.gameVersion, true);
            var isOldForgeRequest = request.modLoader == CompLoaderType.Forge
                && gameVersionDrop is >= 0 and < 140;
            if (isOldForgeRequest) request.modLoader = CompLoaderType.Any;
            var modrinthUrl = request.GetModrinthAddress();
            if (isOldForgeRequest) request.modLoader = CompLoaderType.Forge;

            var tasks = new List<Task>();

            if (modrinthUrl is not null)
                tasks.Add(
                    Task.Run(() =>
                    {
                        lastError = _FetchModrinthPage(
                            modrinthUrl,
                            storage,
                            rawResults,
                            resultsLock);
                    }));

            Task.WaitAll(tasks.ToArray());
            task.Progress += 0.4;
            if (task.IsAborted) return null;

            if (isOldForgeRequest)
                rawResults =
                [
                    .. rawResults.Where(p =>
                        p.ModLoaders.Count == 0 || p.ModLoaders.Contains(CompLoaderType.Forge))
                ];

            if (rawResults.Count == 0)
            {
                if (lastError is not null) throw lastError;
                throw new Exception(Lang.Text("Download.Comp.List.NoResultsSimple"));
            }

            var processedResults = rawResults
                .Where(r => !realResults.Any(r.IsLike) && !storage.results.Any(r.IsLike))
                .ToList();

            realResults.AddRange(processedResults);
            LogWrapper.Info(
                $"[Comp] 去重、筛选后累计新增结果 {processedResults.Count} 个（目前已有结果 {storage.results.Count} 个）");

            if (realResults.Count + storage.results.Count < request.targetResultCount
                && request.CanContinue
                && lastError is null)
            {
                LogWrapper.Info("[Comp] 数量不足，继续加载下一页");
                continue;
            }

            break;
        }

        return realResults;
    }

    private static Exception? _FetchModrinthPage(
        string modrinthUrl,
        CompProjectStorage storage,
        List<CompProject> rawResults,
        object resultsLock)
    {
        try
        {
            LogWrapper.Info($"[Comp] 开始从 Modrinth 获取列表：{modrinthUrl}");
            var json = GameDownload.DlModRequest<JsonObject>(modrinthUrl);
            var projects = (json["hits"]?.AsArray() ?? [])
                .OfType<JsonObject>()
                .Select(j => new CompProject(j))
                .ToList();
            lock (resultsLock)
            {
                rawResults.AddRange(projects);
            }

            storage.modrinthOffset += projects.Count;
            storage.modrinthTotal = json["total_hits"]?.ToObject<int>() ?? 0;
            return null;
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Modrinth 获取失败");
            return ex;
        }
    }

    private static Dictionary<CompProject, double> _ScoreProjectResults(
        CompProjectRequest request,
        List<CompProject> realResults,
        string rawFilter,
        bool isChineseSearch)
    {
        var scores = new Dictionary<CompProject, double>();

        if (string.IsNullOrEmpty(rawFilter))
        {
            foreach (var res in realResults)
                scores.Add(res, res.DownloadCount * _GetDownloadCountMultiplier(request, res));

            return scores;
        }

        var searchEntries = new List<SearchService.SearchEntry<CompProject>>();
        foreach (var res in realResults)
        {
            scores.Add(
                res,
                Math.Log10(
                    Math.Max(res.DownloadCount, 1) * _GetDownloadCountMultiplier(request, res))
                / 9);
            searchEntries.Add(
                new SearchService.SearchEntry<CompProject>
                {
                    item = res,
                    searchSource =
                    [
                        new SearchService.SearchSource(
                            res.RawName
                            .Split(['/'], StringSplitOptions.RemoveEmptyEntries)),
                        new SearchService.SearchSource(res.Description, 0.05)
                    ]
                });
        }

        var searchRes = SearchService.Search(
            searchEntries,
            rawFilter,
            101,
            -1);
        foreach (var item in searchRes)
            if (item.item is not null)
                scores[item.item] +=
                    (item.absoluteRight ? 10 : item.similarity)
                    / (searchRes.First().absoluteRight ? 10 : searchRes.First().similarity);

        return scores;
    }

    private static double _GetDownloadCountMultiplier(
        CompProjectRequest request,
        CompProject project)
    {
        return request.type switch
        {
            CompType.Mod or CompType.ModPack => 7,
            CompType.ResourcePack or CompType.Shader => 5,
            _ => 1
        };
    }

}