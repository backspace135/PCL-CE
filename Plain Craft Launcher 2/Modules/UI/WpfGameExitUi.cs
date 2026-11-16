using System.IO;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using PCL.Core.UI.Dialogs;
using PCL.Core.UI.Hints;
using PCL.Horizon;

namespace PCL;

/// <summary>
///     游戏出错退出时的提示：显示 Horizon 的崩溃报告，并可打开或导出。
/// </summary>
public sealed class WpfGameExitUi : IGameExitUi
{
    private const int MaxPreviewLength = 1500;

    public void OnGameExited(GameExitInfo info)
    {
        Common.RunInUi(() => _ = ShowAsync(info));
    }

    private static async Task ShowAsync(GameExitInfo info)
    {
        var result = info.Result;
        if (result.Kind == HorizonExitKind.RefusedCommandLine)
        {
            await DialogService.Instance.ShowAsync(new TextDialogRequest
            {
                Title = Lang.Text("Horizon.Crash.Title.Refused"),
                Content = Lang.Text("Horizon.Crash.Refused", string.Join("\n", result.StderrTail.TakeLast(10))),
                Buttons = DialogButtons.Ok,
                IsWarn = true
            });
            return;
        }

        var report = HorizonCrashReport.ReadCrashReport(info);
        var preview = report is null
            ? string.Join("\n", result.StderrTail.TakeLast(15))
            : report.Length > MaxPreviewLength
                ? report[..MaxPreviewLength] + "\n…"
                : report;
        var content = HorizonGames.Describe(result) + "\n\n"
            + (result.CrashReportPath is null
                ? Lang.Text("Horizon.Crash.NoReport")
                : Lang.Text("Horizon.Crash.ReportPath", result.CrashReportPath))
            + (string.IsNullOrWhiteSpace(preview) ? "" : "\n\n" + preview.Trim());
        var choice = (await DialogService.Instance.ShowAsync(new TextDialogRequest
        {
            Title = Lang.Text(result.Kind == HorizonExitKind.Panic ? "Horizon.Crash.Title.Panic" : "Horizon.Crash.Title.Error"),
            Content = content,
            Buttons = DialogButtons.Create(
                result.CrashReportPath is null ? "Horizon.Crash.OpenLog" : "Horizon.Crash.OpenReport",
                "Horizon.Crash.Export",
                "Common.Action.Close"),
            IsWarn = true
        })).Button;
        switch (choice)
        {
            case 1:
                _Open(result.CrashReportPath ?? info.LatestLogPath);
                break;
            case 2:
                _Export(info);
                break;
        }
    }

    private static void _Open(string path)
    {
        if (File.Exists(path))
            Common.ShellOnly(path);
        else
            HintService.Show(Hints.Error(Lang.Text("Horizon.Crash.FileMissing", path)));
    }

    private static void _Export(GameExitInfo info)
    {
        var target = SystemDialogs.SelectSaveFile(
            Lang.Text("Horizon.Crash.Export"),
            $"Horizon 错误报告 {DateTime.Now:yyyy-MM-dd HHmmss}.zip",
            Lang.Text("Horizon.Crash.Export.Filter") + "|*.zip");
        if (string.IsNullOrWhiteSpace(target))
            return;
        try
        {
            HorizonCrashReport.Export(info, target);
            HintService.Show(Hints.Success(Lang.Text("Horizon.Crash.Export.Success")));
            Common.OpenExplorer(target);
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.Crash.Export.Failed"), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Horizon.Crash.Export.Failed"));
        }
    }
}
