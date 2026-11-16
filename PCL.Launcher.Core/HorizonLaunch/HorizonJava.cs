using PCL.Core.App.Localization;
using PCL.Core.Minecraft.Java;

namespace PCL;

/// <summary>
///     为 <c>horizon-prepare</c> 选择或下载指定主版本的 Java。游戏本身不使用 Java。
/// </summary>
public static class HorizonJava
{
    /// <summary>
    ///     返回主版本为 <paramref name="major" /> 的 Java 的主目录（供 <c>--java-home</c> 使用）。
    ///     没有时征得同意后下载 <paramref name="component" />（为空则按主版本下载），被拒绝时抛出 <c>$$</c>。
    /// </summary>
    public static string EnsureJavaHome(Loading.LoaderTask<int, int> task, int major, string? component)
    {
        lock (JavaRuntime.JavaLock)
        {
            var java = _Select(major);
            if (java is null && !task.IsAborted)
            {
                HorizonLaunch.Log($"没有 Java {major}，需要确认是否自动下载");
                if (!JavaRuntime.JavaDownloadConfirm($"Java {major}"))
                    throw new Exception("$$");
                _Download(task, component ?? major.ToString());
                java = _Select(major);
            }

            if (task.IsAborted)
                throw new ThreadInterruptedException();
            if (java is null)
                throw new Exception(Lang.Text("Horizon.Launch.Error.NoJava", major));

            var home = _HomeOf(java.Installation.JavaFolder);
            HorizonLaunch.Log($"准备资源使用的 Java：{java.Installation}，主目录 {home}");
            return home;
        }
    }

    private static JavaEntry? _Select(int major) =>
        JavaRuntime.JavaSelect(
            "$$",
            new Version(major, 0, 0, 0),
            new Version(major, 999, 999, 999),
            true,
            new HashSet<int> { major });

    private static void _Download(Loading.LoaderTask<int, int> task, string component)
    {
        var loader = JavaRuntime.GetJavaDownloadLoader();
        try
        {
            loader.Start(component, true);
            while (loader.State == LoadState.Loading && !task.IsAborted)
            {
                task.Progress = loader.Progress;
                Thread.Sleep(10);
            }

            if (loader.State == LoadState.Failed)
                throw new Exception(Lang.Text("Horizon.Launch.Error.JavaDownloadFailed", component), loader.Error);
        }
        finally
        {
            loader.Abort();
        }
    }

    /// <summary>
    ///     <see cref="JavaInstallation.JavaFolder" /> 是 <c>java.exe</c> 所在的 <c>bin</c> 目录，<c>--java-home</c> 要的是它的上一级。
    /// </summary>
    private static string _HomeOf(string javaFolder)
    {
        var folder = javaFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Path.GetFileName(folder), "bin", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(folder)!
            : folder;
    }
}
