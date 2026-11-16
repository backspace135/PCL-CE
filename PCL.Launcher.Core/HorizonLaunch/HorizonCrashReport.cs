using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using PCL.Core.App;
using PCL.Core.Logging;
using PCL.Horizon;

namespace PCL;

/// <summary>
///     游戏出错退出后的报告：读取 Horizon 写出的崩溃报告，并打包成便于反馈的压缩包。
///     Horizon 自己完成崩溃分析，启动器不再按 Java 日志规则猜测原因。
/// </summary>
public static class HorizonCrashReport
{
    private const string CrashReportName = "crash-report.txt";
    private const string LatestLogName = "latest.log";
    private const string StderrName = "stderr.txt";
    private const string LauncherLogName = "launcher.log";
    private const string EnvironmentName = "environment.txt";

    /// <summary>
    ///     崩溃报告的内容，没有或读取失败时为 <see langword="null" />。
    /// </summary>
    public static string? ReadCrashReport(GameExitInfo info)
    {
        var path = info.Result.CrashReportPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LauncherCommon.Log(ex, $"[Horizon] 读取崩溃报告失败：{path}");
            return null;
        }
    }

    /// <summary>
    ///     把崩溃报告、游戏日志、stderr 末尾、启动器日志和环境信息打包到 <paramref name="zipPath" />，
    ///     文本中的访问令牌与 Windows 用户名会被打码。
    /// </summary>
    public static void Export(GameExitInfo info, string zipPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(zipPath));
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        _AddFile(zip, CrashReportName, info.Result.CrashReportPath);
        _AddFile(zip, LatestLogName, info.LatestLogPath);
        _AddText(zip, StderrName, string.Join(Environment.NewLine, info.Result.StderrTail));
        _AddFile(zip, LauncherLogName, LogWrapper.CurrentLogger.CurrentLogFiles.LastOrDefault());
        _AddText(zip, EnvironmentName, _Environment(info));
        LauncherCommon.Log($"[Horizon] 已导出错误报告：{zipPath}");
    }

    private static string _Environment(GameExitInfo info)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"PCL Horizon {Basics.VersionName} ({Basics.VersionCode})");
        builder.AppendLine($"Horizon build: {info.Game.Build.Version}");
        builder.AppendLine($"Launch API: {info.Game.Client.LaunchApiVersion}");
        builder.AppendLine($"Minecraft: {info.Game.Client.Minecraft}");
        builder.AppendLine($"Instance: {info.Game.Instance.Name}");
        builder.AppendLine($"Exit code: {info.Result.ExitCode} ({info.Result.Kind})");
        builder.AppendLine($"Started: {info.Game.StartedAt:O}");
        builder.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        return builder.ToString();
    }

    private static void _AddFile(ZipArchive zip, string name, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            _AddText(zip, name, reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LauncherCommon.Log(ex, $"[Horizon] 无法加入错误报告：{path}");
        }
    }

    private static void _AddText(ZipArchive zip, string name, string text)
    {
        text = LogFilter.FilterUserName(LogFilter.FilterAccessToken(text, '*'), '*');
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
