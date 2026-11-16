using System.Diagnostics.CodeAnalysis;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.IO;
using PCL.Core.Minecraft;
using PCL.Core.Minecraft.Java;
using PCL.Core.Utils;

namespace PCL;

public static partial class JavaRuntime
{
    /// <summary>
    ///     防止多个需要 Java 的部分同时要求下载 Java。
    /// </summary>
    public static readonly object JavaLock = new();

    /// <summary>
    ///     目前所有可用的 Java。
    /// </summary>
    public static JavaManager Javas => JavaService.JavaManager;

    /// <summary>
    ///     根据要求返回最适合的 Java，若找不到则返回 Nothing。
    ///     最小与最大版本在与输入相同时也会通过。
    ///     必须在工作线程调用，且必须包括 SyncLock JavaLock。
    /// </summary>
    public static JavaEntry? JavaSelect(
        string cancelException,
        Version? minVersion = null,
        Version? maxVersion = null,
        bool enforceVersionRange = false,
        IReadOnlySet<int>? compatibleMajors = null)
    {
        LauncherCommon.Log(
            $"[Java] 要求选择合适 Java，"
            + $"要求最低版本 {(minVersion is not null ? minVersion.ToString() : "未指定")}，"
            + $"要求选择的最高版本 {(maxVersion is not null ? maxVersion.ToString() : "未指定")}");

        if (TrySelectGlobalJava(
                minVersion,
                maxVersion,
                enforceVersionRange,
                compatibleMajors,
                out var globalJava))
            return globalJava;

        return SelectAutomaticJava(minVersion, maxVersion, compatibleMajors);
    }

    private static bool TrySelectGlobalJava(
        Version? minVersion,
        Version? maxVersion,
        bool enforceVersionRange,
        IReadOnlySet<int>? compatibleMajors,
        [NotNullWhen(true)] out JavaEntry? selected)
    {
        selected = null;
        var globalJavaPath = Config.Launch.SelectedJava;

        if (string.IsNullOrWhiteSpace(globalJavaPath))
        {
            LauncherCommon.Log("[Java] 无全局 Java 配置，使用自动选择策略");
            return false;
        }

        globalJavaPath = globalJavaPath.Trim();
        var candidate = Javas.AddOrGet(globalJavaPath);

        if (candidate is null || !candidate.IsEnabled)
        {
            LauncherCommon.Log($"[Java] 警告：全局指定的 Java 路径无效或不可用: {globalJavaPath}");
            return false;
        }

        var versionSuitable =
            IsVersionSuitable(candidate.Installation.Version, minVersion, maxVersion)
            && IsAllowedMajor(candidate, compatibleMajors);

        if (enforceVersionRange && !versionSuitable)
        {
            LauncherCommon.Log(
                $"[Java] 全局指定的 Java 版本不满足强制范围要求，忽略并继续自动搜索: {candidate}");
            return false;
        }

        if (!versionSuitable)
            LauncherUi.Interaction?.ShowHint(
                _GetJavaRangeWarning(
                    "Minecraft.Launch.Java.Compatibility.GlobalSelectedOutOfRange",
                    candidate.Installation.Version,
                    minVersion,
                    maxVersion));

        LauncherCommon.Log($"[Java] 返回全局指定的 Java: {candidate}");
        selected = candidate;
        return true;
    }

    private static JavaEntry? SelectAutomaticJava(
        Version? minVersion,
        Version? maxVersion,
        IReadOnlySet<int>? compatibleMajors)
    {
        LauncherCommon.Log("[Java] 开始自动搜索符合版本要求的 Java 运行时");
        Javas.CheckAllAvailability();

        var requiredMin = minVersion ?? new Version(1, 0, 0);
        var requiredMax = maxVersion ?? new Version(999, 999, 999);
        var candidates = FilterByCompatibleMajors(
            Javas
                .SelectSuitableJavaAsync(requiredMin, requiredMax)
                .GetAwaiter()
                .GetResult(),
            compatibleMajors);
        var selected = candidates.FirstOrDefault();

        if (selected is null && candidates.Length == 0)
        {
            LauncherCommon.Log("[Java] 未找到符合版本要求的 Java，触发全盘重新扫描");
            Javas.ScanJavaAsync().GetAwaiter().GetResult();
            candidates = FilterByCompatibleMajors(
                Javas
                    .SelectSuitableJavaAsync(requiredMin, requiredMax)
                    .GetAwaiter()
                    .GetResult(),
                compatibleMajors);
            selected = candidates.FirstOrDefault();
        }

        LauncherCommon.Log(
            selected is not null
                ? $"[Java] 返回自动选择的 Java: {selected}"
                : "[Java] 最终未能确定可用的 Java 运行时");

        return selected;
    }

    private static JavaEntry[] FilterByCompatibleMajors(
        JavaEntry[] entries,
        IReadOnlySet<int>? compatibleMajors)
    {
        return compatibleMajors is null
            ? entries
            :
            [
                .. entries.Where(entry => compatibleMajors
                    .Contains(JavaManager.NormalizeVersion(entry.Installation.Version).Major))
            ];
    }

    private static bool IsAllowedMajor(
        JavaEntry entry,
        IReadOnlySet<int>? compatibleMajors)
    {
        return compatibleMajors is null
            || compatibleMajors.Contains(
                JavaManager.NormalizeVersion(entry.Installation.Version).Major);
    }

    private static bool IsVersionSuitable(
        Version version,
        Version? minVersion,
        Version? maxVersion)
    {
        return (minVersion is null || version >= minVersion)
            && (maxVersion is null || version <= maxVersion);
    }

    private static string _GetJavaRangeWarning(
        string key,
        Version selectedVersion,
        Version? minVersion,
        Version? maxVersion)
    {
        return Lang.Text(
            key,
            selectedVersion,
            minVersion?.ToString() ?? Lang.Text("Minecraft.Launch.Java.Compatibility.NoMinimum"),
            maxVersion?.ToString() ?? Lang.Text("Minecraft.Launch.Java.Compatibility.NoMaximum"));
    }

}
