using System.Runtime.InteropServices;

namespace PCL.Horizon;

/// <summary>GitHub release 中的一个文件。</summary>
public sealed record HorizonReleaseAsset(string Name, long Size, string DownloadUrl);

/// <summary>
/// 一个可安装的 Nightly 构建。<see cref="Version"/> 由标题严格解析而来（如 <c>nightly-2026-11-13-9f42064</c>），
/// 签名针对的正是它，安装目录也以它命名。
/// </summary>
public sealed record HorizonRelease(
    string Tag,
    string Title,
    string Version,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<HorizonReleaseAsset> Assets)
{
    /// <summary>是否为当前 <c>nightly</c> 标签所指的最新构建。</summary>
    public bool IsLatest { get; init; }

    /// <summary>构建日期（<c>yyyy-MM-dd</c>）。</summary>
    public string Date => Version.Substring(HorizonReleaseSigning.NightlyTag.Length + 1, 10);

    /// <summary>构建对应的提交（短 SHA）。</summary>
    public string Commit => Version[(HorizonReleaseSigning.NightlyTag.Length + 12)..];

    public HorizonReleaseAsset? FindAsset(string name) => Assets.FirstOrDefault(a => a.Name == name);
}

/// <summary>各平台的发布包文件名。</summary>
public static class HorizonPlatform
{
    public const string WindowsX64 = "horizon-windows-x64.zip";
    public const string WindowsArm64 = "horizon-windows-arm64.zip";

    /// <summary>当前系统架构对应的 Windows 发布包。</summary>
    public static string CurrentWindowsAsset =>
        RuntimeInformation.OSArchitecture == Architecture.Arm64 ? WindowsArm64 : WindowsX64;

    /// <summary>当前系统的可执行文件扩展名。</summary>
    public static string ExecutableSuffix => OperatingSystem.IsWindows() ? ".exe" : "";
}
