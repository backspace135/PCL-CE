using PCL.Core.App.Localization;
using PCL.Horizon;
using System.Windows.Controls;

namespace PCL;

public partial class PageInstanceLeft : IRefreshable, IPageView
{
    /// <summary>
    ///     正在查看设置的实例。
    /// </summary>
    public static HorizonInstance Instance = null!;

    private readonly PageInstanceLeftViewModel _vm = new();
    private bool _refreshed;

    public PageInstanceLeft()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += (_, _) => RefreshOnNavigate();
        Unloaded += (_, _) => _refreshed = false;
    }

    public PageInstanceLeftViewModel ViewModel => _vm;

    public FormMain.PageSubType pageID = FormMain.PageSubType.Default;

    public void OnNavigatedTo(object? parameter)
    {
        RefreshOnNavigate();
    }

    public void OnNavigatedFrom()
    {
    }

    private void RefreshOnNavigate()
    {
        if (_refreshed)
            return;
        _refreshed = true;
        _vm.RefreshVisibility();
    }

    public void Refresh()
    {
        Refresh(Launcher.frmMain.PageCurrentSub);
    }

    public void Refresh(FormMain.PageSubType subType)
    {
        switch (subType)
        {
            case FormMain.PageSubType.VersionScreenshot:
                {
                    _ = PageInstanceScreenshot.RefreshAsync();
                    break;
                }
            case FormMain.PageSubType.VersionWorld:
                {
                    PageInstanceSaves.Refresh();
                    break;
                }
            case FormMain.PageSubType.VersionMod:
                {
                    Launcher.frmInstanceMod?.Refresh();
                    break;
                }
            case FormMain.PageSubType.VersionResourcePack:
                {
                    Launcher.frmInstanceResourcePack?.Refresh();
                    break;
                }
            case FormMain.PageSubType.VersionShader:
                {
                    Launcher.frmInstanceShader?.Refresh();
                    break;
                }
            case FormMain.PageSubType.VersionServer:
                {
                    if (Launcher.frmInstanceServer is not null)
                        Launcher.frmInstanceServer.RefreshServers();
                    ItemServer.Checked = true;
                    break;
                }
        }
    }


    private void PageCheck(object sender, Common.RouteEventArgs e)
    {
        if (sender is MyListItem item && item.Tag is not null)
            PageChange((FormMain.PageSubType)Common.Val(item.Tag));
    }

    public object PageGet(FormMain.PageSubType id)
    {
        if ((int)id == -1)
            id = pageID;
        switch (id)
        {
            case FormMain.PageSubType.VersionOverall:
                {
                    Launcher.frmInstanceOverall ??= new PageInstanceOverall();
                    return Launcher.frmInstanceOverall;
                }
            case FormMain.PageSubType.VersionSetup:
                {
                    Launcher.frmInstanceSetup ??= new PageInstanceSetup();
                    return Launcher.frmInstanceSetup;
                }
            case FormMain.PageSubType.VersionWorld:
                {
                    Launcher.frmInstanceSaves ??= new PageInstanceSaves();
                    return Launcher.frmInstanceSaves;
                }
            case FormMain.PageSubType.VersionScreenshot:
                {
                    Launcher.frmInstanceScreenshot ??= new PageInstanceScreenshot();
                    return Launcher.frmInstanceScreenshot;
                }
            case FormMain.PageSubType.VersionMod:
                {
                    Launcher.frmInstanceMod ??= new PageInstanceMod();
                    return Launcher.frmInstanceMod;
                }
            case FormMain.PageSubType.VersionResourcePack:
                {
                    Launcher.frmInstanceResourcePack ??= new PageInstanceResourcePack();
                    return Launcher.frmInstanceResourcePack;
                }
            case FormMain.PageSubType.VersionShader:
                {
                    Launcher.frmInstanceShader ??= new PageInstanceShader();
                    return Launcher.frmInstanceShader;
                }
            case FormMain.PageSubType.VersionServer:
                {
                    Launcher.frmInstanceServer ??= new PageInstanceServer();
                    return Launcher.frmInstanceServer;
                }

            default:
                {
                    throw new Exception(PageInstanceLeftViewModel.GetUnknownSubPageMessage(id));
                }
        }
    }

    public void PageChange(FormMain.PageSubType id)
    {
        if (pageID == id)
            return;
        Animations.AniBeginControl();
        try
        {
            PageChangeRun((MyPageRight)PageGet(id));
            pageID = id;
        }
        catch (Exception ex)
        {
            Common.Log(
                ex,
                PageInstanceLeftViewModel.GetSwitchFailedMessage(id),
                Common.LogLevel.Feedback,
                userSummary: Lang.Text("Instance.Error.OperationFailed"));
        }
        finally
        {
            Animations.AniEndControl();
        }
    }

    private static void PageChangeRun(MyPageRight target)
    {
        Animations.AniStop("FrmMain PageChangeRight");
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
                Launcher.frmMain.pageRight.Opacity = 1d;
                Launcher.frmMain.pageRight.PageOnEnter();
            }, 30, true)
        }, "PageLeft PageChange");
    }
}
