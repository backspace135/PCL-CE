using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using PCL.Core.UI.Dialogs;
using PCL.Core.UI.Hints;

namespace PCL;

/// <summary>
///     启动设置：启动后启动器的行为与 Horizon 数据目录。
/// </summary>
public partial class PageSetupLaunch : IPageView
{
    private bool _loading;

    public PageSetupLaunch()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    public void OnNavigatedTo(object? parameter) => Reload();

    public void OnNavigatedFrom()
    {
    }

    protected override void RefreshLocalization()
    {
        Reload();
    }

    public void Reload()
    {
        _loading = true;
        try
        {
            ComboVisibility.SelectedIndex = (int)Config.Launch.LauncherVisibility;
            CheckGpuPreference.Checked = Config.Launch.SetGpuPreference;
            LabRoot.Text = Lang.Text("Horizon.Setup.Data.Current", HorizonEnvironment.Paths.Root);
        }
        finally
        {
            _loading = false;
        }
    }

    public void Reset()
    {
        try
        {
            Config.Launch.Reset();
            Config.Horizon.Reset();
            HorizonEnvironment.ReloadInstances();
            Common.Log("[Setup] 已初始化启动设置");
            HintService.Show(Hints.Success(Lang.Text("Setup.Launch.Initialized"), log: false));
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Setup.Launch.Error.InitFailed"), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Setup.Launch.Error.InitFailed"));
        }

        Reload();
    }

    private async void ComboVisibility_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ComboVisibility.SelectedIndex < 0)
            return;
        var previous = Config.Launch.LauncherVisibility;
        if (ComboVisibility.SelectedIndex == (int)LauncherVisibility.ExitImmediately &&
            previous != LauncherVisibility.ExitImmediately &&
            (await DialogService.Instance.ShowAsync(new TextDialogRequest
            {
                Title = Lang.Text("Setup.Launch.Options.Visibility.CloseImmediately.Warning.Title"),
                Content = Lang.Text("Setup.Launch.Options.Visibility.CloseImmediately.Warning.Message"),
                Buttons = DialogButtons.From(
                    DialogButton.Create(Lang.Text("Setup.Launch.Options.Visibility.CloseImmediately.Warning.Continue")),
                    DialogButton.Create(Lang.Text("Common.Action.Cancel")))
            })).Button != 1)
        {
            Reload();
            return;
        }

        Config.Launch.LauncherVisibility = (LauncherVisibility)ComboVisibility.SelectedIndex;
    }

    private void CheckGpuPreference_Change(object sender, bool user)
    {
        if (!_loading && user)
            Config.Launch.SetGpuPreference = CheckGpuPreference.Checked == true;
    }

    private void BtnOpenRoot_Click(object sender, MouseButtonEventArgs e)
    {
        var root = HorizonEnvironment.Paths.Root;
        Directory.CreateDirectory(root);
        Common.OpenExplorer(root + "\\");
    }

    private void BtnChangeRoot_Click(object sender, MouseButtonEventArgs e)
    {
        var folder = SystemDialogs.SelectFolder(Lang.Text("Horizon.Setup.Data.Select"));
        if (string.IsNullOrWhiteSpace(folder))
            return;
        if (HorizonGames.HasRunningGame || HorizonLaunch.IsLaunching)
        {
            HintService.Show(Hints.Error(Lang.Text("Horizon.Setup.Data.Busy")));
            return;
        }

        Config.Horizon.Root = folder;
        HorizonEnvironment.ReloadInstances();
        Reload();
        HintService.Show(Hints.Success(Lang.Text("Horizon.Setup.Data.Changed", HorizonEnvironment.Paths.Root)));
    }

    private void BtnResetRoot_Click(object sender, MouseButtonEventArgs e)
    {
        if (HorizonGames.HasRunningGame || HorizonLaunch.IsLaunching)
        {
            HintService.Show(Hints.Error(Lang.Text("Horizon.Setup.Data.Busy")));
            return;
        }

        Config.Horizon.RootConfig.Reset();
        HorizonEnvironment.ReloadInstances();
        Reload();
    }
}
