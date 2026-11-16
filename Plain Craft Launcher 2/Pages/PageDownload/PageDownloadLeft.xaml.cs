using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App.Localization;
using PCL.Core.UI.Hints;

namespace PCL;

public partial class PageDownloadLeft : IRefreshable
{
    public void Refresh()
    {
        Refresh(Launcher.frmMain.PageCurrentSub);
    }

    // 强制刷新
    public void RefreshButton_Click(object sender, EventArgs e) // 由边栏按钮匿名调用
    {
        Refresh((FormMain.PageSubType)Common.Val(((MyIconButton)sender).Tag));
    }

    public void Refresh(FormMain.PageSubType subType)
    {
        switch (subType)
        {
            case FormMain.PageSubType.DownloadDataPack:
            {
                Comp.compProjectCache.Clear();
                Comp.compFilesCache.Clear();
                if (Launcher.frmDownloadDataPack is not null)
                {
                    Launcher.frmDownloadDataPack.Content.storage = new Comp.CompProjectStorage();
                    Launcher.frmDownloadDataPack.Content.page = 0;
                    Launcher.frmDownloadDataPack.PageLoaderRestart();
                }

                ItemDataPack.Checked = true;
                break;
            }
            case FormMain.PageSubType.DownloadResourcePack:
            {
                Comp.compProjectCache.Clear();
                Comp.compFilesCache.Clear();
                if (Launcher.frmDownloadResourcePack is not null)
                {
                    Launcher.frmDownloadResourcePack.Content.storage =
                        new Comp.CompProjectStorage();
                    Launcher.frmDownloadResourcePack.Content.page = 0;
                    Launcher.frmDownloadResourcePack.PageLoaderRestart();
                }

                ItemResourcePack.Checked = true;
                break;
            }
            case FormMain.PageSubType.DownloadClient:
            {
                GameDownload.dlClientListLoader.Start(isForceRestart: true);
                ItemClient.Checked = true;
                break;
            }
            case FormMain.PageSubType.DownloadCompFavorites:
            {
                if (Launcher.frmDownloadCompFavorites is not null)
                    Launcher.frmDownloadCompFavorites.PageLoaderRestart();
                ItemFavorites.Checked = true;
                break;
            }
        }

        HintService.Show(Hints.Info(Lang.Text("Download.Left.Hint.Refreshing"), log: false));
    }


    #region 页面切换

    /// <summary>
    ///     当前页面的编号。
    /// </summary>
    public FormMain.PageSubType PageId = FormMain.PageSubType.DownloadClient;

    public PageDownloadLeft()
    {
        InitializeComponent();

        AnimatedControl = PanItem;
    }

    /// <summary>
    ///     勾选事件改变页面。
    /// </summary>
    private void PageCheck(object sender, Common.RouteEventArgs e)
    {
        if (sender is MyListItem { Tag: { } tag })
            PageChange((FormMain.PageSubType)Common.Val(tag));
    }

    public object PageGet(FormMain.PageSubType id)
    {
        if (id == default)
            id = PageId;
        switch (id)
        {
            case FormMain.PageSubType.DownloadDataPack:
            {
                if (Launcher.frmDownloadDataPack is null)
                    Launcher.frmDownloadDataPack = new PageDownloadDataPack();
                return Launcher.frmDownloadDataPack;
            }
            case FormMain.PageSubType.DownloadResourcePack:
            {
                if (Launcher.frmDownloadResourcePack is null)
                    Launcher.frmDownloadResourcePack = new PageDownloadResourcePack();
                return Launcher.frmDownloadResourcePack;
            }
            case FormMain.PageSubType.DownloadCompFavorites:
            {
                if (Launcher.frmDownloadCompFavorites is null)
                    Launcher.frmDownloadCompFavorites = new PageDownloadCompFavorites();
                return Launcher.frmDownloadCompFavorites;
            }
            case FormMain.PageSubType.DownloadClient:
            {
                if (Launcher.frmDownloadClient is null)
                    Launcher.frmDownloadClient = new PageDownloadClient();
                return Launcher.frmDownloadClient;
            }

            default:
            {
                throw new Exception(Lang.Text("Download.Left.Error.UnknownSubPageType", (int)id));
            }
        }
    }

    /// <summary>
    ///     切换现有页面。
    /// </summary>
    public void PageChange(FormMain.PageSubType id)
    {
        if (PageId == id)
            return;
        Animations.AniBeginControl();
        try
        {
            PageChangeRun((MyPageRight)PageGet(id));
            PageId = id;
        }
        catch (Exception ex)
        {
            Common.Log(
                ex,
                "切换分页面失败（ID " + (int)id + "）",
                Common.LogLevel.Feedback,
                userSummary: Lang.Text("Download.Error.OperationFailed"));
        }
        finally
        {
            Animations.AniEndControl();
        }
    }

    private static void PageChangeRun(MyPageRight target)
    {
        Animations.AniStop("FrmMain PageChangeRight"); // 停止主页面的右页面切换动画，防止它与本动画一起触发多次 PageOnEnter
        if (target.Parent is not null)
            target.SetValue(ContentPresenter.ContentProperty, null);
        Launcher.frmMain.pageRight = target;
        ((MyPageRight)Launcher.frmMain.PanMainRight.Child).PageOnExit();
        Animations.AniStart(new[]
        {
            Animations.AaCode(() =>
            {
                ((MyPageRight)Launcher.frmMain.PanMainRight.Child).PageOnForceExit();
                Launcher.frmMain.PanMainRight.Child = Launcher.frmMain.pageRight;
                Launcher.frmMain.pageRight.Opacity = 0d;
            }, 130),
            Animations.AaCode(() =>
            {
                // 延迟触发页面通用动画，以使得在 Loaded 事件中加载的控件得以处理
                Launcher.frmMain.pageRight.Opacity = 1d;
                Launcher.frmMain.pageRight.PageOnEnter();
            }, 30, true)
        }, "PageLeft PageChange");
    }

    #endregion
}
