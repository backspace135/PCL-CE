using System.IO;
using System.Windows.Input;
using PCL.Core.App.Localization;
using PCL.Core.UI.Dialogs;
using PCL.Core.UI.Hints;
using PCL.Core.Utils.Validate;

namespace PCL;

/// <summary>
///     实例概览：基本信息、快捷方式与管理操作。
/// </summary>
public partial class PageInstanceOverall : IRefreshable
{
    public PageInstanceOverall()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    protected override void RefreshLocalization()
    {
        Refresh();
    }

    public void Refresh()
    {
        var instance = PageInstanceLeft.Instance;
        ItemHeader.Title = instance.Name;
        ItemHeader.Info = PageSelectRight.Describe(instance);
        var build = instance.ResolveBuild(HorizonEnvironment.InstalledBuilds());
        LabBuild.Text = build is null
            ? Lang.Text("Horizon.Instance.Overall.BuildNotInstalled")
            : Lang.Text("Horizon.Instance.Overall.Build", build.Version);
        LabFolder.Text = Lang.Text("Horizon.Instance.Overall.Folder", instance.Directory);
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Common.OpenExplorer(folder.TrimEnd('\\') + "\\");
    }

    private void BtnFolderInstance_Click(object sender, MouseButtonEventArgs e) =>
        OpenFolder(PageInstanceLeft.Instance.Directory);

    private void BtnFolderSaves_Click(object sender, MouseButtonEventArgs e) =>
        OpenFolder(PageInstanceLeft.Instance.SavesDir);

    private void BtnFolderScreenshots_Click(object sender, MouseButtonEventArgs e) =>
        OpenFolder(PageInstanceLeft.Instance.ScreenshotsDir);

    private void BtnFolderLogs_Click(object sender, MouseButtonEventArgs e) =>
        OpenFolder(PageInstanceLeft.Instance.LogsDir);

    private void BtnLaunch_Click(object sender, MouseButtonEventArgs e)
    {
        if (HorizonLaunch.Start(new HorizonLaunchOptions { Instance = PageInstanceLeft.Instance }))
            Launcher.frmMain.PageChange(new FormMain.PageStackData { page = FormMain.PageType.Launch });
    }

    private void BtnLatestLog_Click(object sender, MouseButtonEventArgs e)
    {
        var log = PageInstanceLeft.Instance.LatestLogPath;
        if (File.Exists(log))
            Common.ShellOnly(log);
        else
            HintService.Show(Hints.Info(Lang.Text("Horizon.Instance.Overall.NoLog")));
    }

    private async void BtnRename_Click(object sender, MouseButtonEventArgs e)
    {
        var instance = PageInstanceLeft.Instance;
        var result = await DialogService.Instance.ShowAsync(new InputDialogRequest
        {
            Title = Lang.Text("Instance.Overall.Rename"),
            DefaultValue = instance.Name,
            ValidateRules = [new NullOrWhiteSpaceValidator()],
            Buttons = DialogButtons.OkCancel
        });
        if (!result.IsConfirmed || string.IsNullOrWhiteSpace(result.InputText))
            return;
        instance.Rename(result.InputText);
        HorizonEnvironment.NotifyInstanceChanged(instance);
        Refresh();
    }

    private void BtnDelete_Click(object sender, MouseButtonEventArgs e)
    {
        if (PageSelectRight.DeleteInstance(PageInstanceLeft.Instance))
            Launcher.frmMain.PageBack();
    }
}
