using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Core.App;
using PCL.Core.ViewModel;

namespace PCL;

/// <summary>实例左侧栏页的视图模型。</summary>
public partial class PageInstanceLeftViewModel : ViewModelBase
{
    [ObservableProperty] private bool _textResourceVisible = true;

    [ObservableProperty] private bool _itemWorldVisible = true;

    [ObservableProperty] private bool _itemScreenshotVisible = true;

    [ObservableProperty] private bool _itemResourcePackVisible = true;

    [ObservableProperty] private bool _itemServerVisible = true;

    [RelayCommand]
    private void RefreshButton(string? tag)
    {
        if (tag is null) return;
        Launcher.frmInstanceLeft?.Refresh((FormMain.PageSubType)Common.Val(tag));
    }

    public void RefreshVisibility()
    {
        var hide = Config.Preference.Hide;
        var forceShow = PageSetupUI.HiddenForceShow;
        ItemWorldVisible = forceShow || !hide.InstanceSave;
        ItemScreenshotVisible = forceShow || !hide.InstanceScreenshot;
        ItemResourcePackVisible = forceShow || !hide.InstanceResourcePack;
        ItemServerVisible = forceShow || !hide.InstanceServer;
        TextResourceVisible = ItemWorldVisible || ItemScreenshotVisible || ItemResourcePackVisible || ItemServerVisible;
    }

    public static string GetUnknownSubPageMessage(FormMain.PageSubType id)
    {
        return "未知的实例设置子页面种类：" + (int)id;
    }

    public static string GetSwitchFailedMessage(FormMain.PageSubType id)
    {
        return "切换分页面失败（ID " + (int)id + "）";
    }
}
