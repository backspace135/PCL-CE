using System.Windows;
using PCL.Core.App.Localization;

namespace PCL;

public static partial class Launcher
{
    private const int MainTimerSleepMs = 49;
    private const int LongTimerInterval = 150;
    public static FormMain? frmMain;
    public static SplashScreen? frmStart;
    public static PageLaunchLeft? frmLaunchLeft;
    public static PageLaunchRight? frmLaunchRight;
    public static PageSelectLeft? frmSelectLeft;
    public static PageSelectRight? frmSelectRight;
    public static PageSpeedLeft? frmSpeedLeft;
    public static PageSpeedRight? frmSpeedRight;
    public static PageDownloadLeft? frmDownloadLeft;
    public static PageDownloadHorizonBuild? frmDownloadHorizonBuild;
    public static PageDownloadHorizonContent? frmDownloadHorizonContent;
    public static PageDownloadDataPack? frmDownloadDataPack;
    public static PageDownloadResourcePack? frmDownloadResourcePack;
    public static PageDownloadCompFavorites? frmDownloadCompFavorites;
    public static PageDownloadCompDetail? frmDownloadCompDetail;
    public static PageSetupLeft? frmSetupLeft;
    public static PageSetupLaunch? frmSetupLaunch;
    public static PageSetupUI? frmSetupUi;
    public static PageSetupGameManage? frmSetupGameManage;
    public static PageSetupUpdate? frmSetupUpdate;
    public static PageSetupJava? frmSetupJava;
    public static PageSetupAbout? frmSetupAbout;
    public static PageSetupLog? frmSetupLog;
    public static PageSetupLauncherLanguage? frmSetupLauncherLanguage;
    public static PageSetupLauncherMisc? frmSetupLauncherMisc;
    public static PageLoginMs? frmLoginMs;
    public static PageLoginProfile? frmLoginProfile;
    public static PageLoginProfileSkin? frmLoginProfileSkin;
    public static PageInstanceLeft? frmInstanceLeft;
    public static PageInstanceOverall? frmInstanceOverall;
    public static PageInstanceScreenshot? frmInstanceScreenshot;
    public static PageInstanceSaves? frmInstanceSaves;
    public static PageInstanceMod? frmInstanceMod;
    public static PageInstanceResourcePack? frmInstanceResourcePack;
    public static PageInstanceShader? frmInstanceShader;
    public static PageInstanceSetup? frmInstanceSetup;
    public static PageInstanceServer? frmInstanceServer;
    public static PageInstanceSavesLeft? frmInstanceSavesLeft;
    public static PageInstanceSavesInfo? frmInstanceSavesInfo;
    public static PageInstanceSavesDatapack? frmInstanceSavesDatapack;

    public static MySlider? dragControl;

    private static int _timer150Count;

    /// <summary>
    ///     主时钟回调，每约 50ms 执行一次。
    /// </summary>
    private static void TimerMain()
    {
        try
        {
            frmMain!.DragTick();
            Loading.LoaderTaskbarProgressRefresh();
        }
        catch (Exception ex)
        {
            Common.Log(
                ex,
                "短程主时钟执行异常",
                Common.LogLevel.Critical,
                userSummary: Lang.Text("Main.Error.OperationFailed"));
        }

        if (++_timer150Count < LongTimerInterval)
            return;

        _timer150Count = 0;
        TimerMainLong();
    }

    /// <summary>
    ///     长程主时钟回调，每约 7.5 秒执行一次。
    /// </summary>
    private static void TimerMainLong()
    {
        try
        {
            Common.RunInUi(() =>
            {
                var main = frmMain!;
                if (main.Hidden)
                    return;

                if (main.Top < -9000d)
                    main.Top = 100d;

                if (main.Left < -9000d)
                    main.Left = 100d;
            });
        }
        catch (Exception ex)
        {
            Common.Log(
                ex,
                "长程主时钟执行异常",
                Common.LogLevel.Critical,
                userSummary: Lang.Text("Main.Error.OperationFailed"));
        }
    }

    /// <summary>
    ///     启动主时钟。
    /// </summary>
    public static void TimerMainStart()
    {
        Common.RunInNewThread(RunMainTimerLoop, "Timer Main");
    }

    /// <summary>
    ///     主时钟线程。
    /// </summary>
    private static void RunMainTimerLoop()
    {
        try
        {
            while (true)
            {
                Common.RunInUiWait(TimerMain);
                Thread.Sleep(MainTimerSleepMs);
            }
        }
        catch (Exception ex)
        {
            Common.Log(
                ex,
                "程序主时钟出错",
                Common.LogLevel.Feedback,
                userSummary: Lang.Text("Main.Error.OperationFailed"));
        }
    }

}
