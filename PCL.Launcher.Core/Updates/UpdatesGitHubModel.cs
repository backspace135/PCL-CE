using PCL.Core.App.Localization;
using PCL.Core.Utils;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

/// <summary>
///     从 GitHub Releases 获取启动器更新。<br />
///     正式版的标签为 <c>v&lt;版本&gt;</c>，Beta 版发布为预发布；每个版本带有
///     <c>PCL-Horizon_&lt;配置&gt;_x64.exe</c>，
///     SHA-256 取自 GitHub 为资源计算的 <c>digest</c>。
/// </summary>
public class UpdatesGitHubModel : IUpdateSource
{
    private const string AssetPrefix = "PCL-Horizon_";
    private const string DigestPrefix = "sha256:";

    private readonly string _apiUrl;

    private JsonArray? _releases;

    public UpdatesGitHubModel(string repository, string name = "GitHub")
    {
        _apiUrl = $"https://api.github.com/repos/{repository}/releases?per_page=30";
        SourceName = name;
    }

    public string SourceName { get; set; }

    public bool IsAvailable()
    {
        return true;
    }

    public bool RefreshCache()
    {
        _releases = (JsonArray)Requester.FetchJson(_apiUrl, RequestParam.WithRetry);
        return true;
    }

    public VersionDataModel GetLatestVersion(UpdateChannel channel)
    {
        var (release, asset) = FindLatest(channel);
        return new VersionDataModel
        {
            VersionName = TagToVersion(release["tag_name"]!.GetValue<string>()),
            VersionCode = 0,
            Sha256 = AssetSha256(asset),
            Source = SourceName,
            Changelog = release["body"]?.GetValue<string>() ?? ""
        };
    }

    public bool IsLatest(
        UpdateChannel channel,
        SemVer currentVersion,
        int currentVersionCode)
    {
        var latest = GetLatestVersion(channel);
        return currentVersion >= SemVer.Parse(latest.VersionName);
    }

    public VersionAnnouncementDataModel GetAnnouncementList()
    {
        return new VersionAnnouncementDataModel { Content = [] };
    }

    public List<Loading.LoaderBase> GetDownloadLoader(
        UpdateChannel channel,
        string output)
    {
        return
        [
            new Loading.LoaderTask<int, List<DownloadFile>>(
                Lang.Text("Update.Task.GetVersionInfo"),
                load =>
                {
                    var (_, asset) = FindLatest(channel);
                    load.output =
                    [
                        new DownloadFile(
                            [asset["browser_download_url"]!.GetValue<string>()],
                            output)
                    ];
                }),
            new LoaderDownload(Lang.Text("Update.Task.DownloadFile"), [])
        ];
    }

    private (JsonObject Release, JsonObject Asset) FindLatest(UpdateChannel channel)
    {
        if (_releases is null)
            RefreshCache();
        const string suffix = "_x64.exe";
        foreach (var release in _releases!.OfType<JsonObject>())
        {
            if (release["draft"]?.GetValue<bool>() == true)
                continue;
            // 正式版通道只看正式版；Beta 通道两者都看，取最新的那个
            if (channel == UpdateChannel.Stable && release["prerelease"]?.GetValue<bool>() == true)
                continue;
            var asset = release["assets"]
                ?.AsArray()
                .OfType<JsonObject>()
                .FirstOrDefault(a =>
                {
                    var name = a["name"]?.GetValue<string>() ?? "";
                    return name.StartsWith(AssetPrefix, StringComparison.Ordinal)
                        && name.EndsWith(suffix, StringComparison.Ordinal);
                });
            if (asset is not null)
                return (release, asset);
        }

        throw new Exception("No release on GitHub has an asset for this channel and architecture.");
    }

    private static string TagToVersion(string tag)
    {
        return tag.StartsWith('v') ? tag[1..] : tag;
    }

    private static string AssetSha256(JsonObject asset)
    {
        var digest = asset["digest"]?.GetValue<string>();
        if (digest is null || !digest.StartsWith(DigestPrefix, StringComparison.Ordinal))
            throw new Exception("The release asset has no SHA-256 digest.");
        return digest[DigestPrefix.Length..].ToLowerInvariant();
    }
}
