using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using PCL.Core.UI.Dialogs;
using PCL.Core.UI.Hints;
using PCL.Horizon;

namespace PCL;

/// <summary>
///     Horizon 构建：列出已安装的构建与可下载的 Nightly，安装（校验签名）或删除。
/// </summary>
public partial class PageDownloadHorizonBuild : IRefreshable
{
    private IReadOnlyList<HorizonRelease>? _releases;
    private Exception? _releasesError;
    private bool _loading;

    /// <summary>正在安装的版本与进度（0 到 1）。</summary>
    private readonly Dictionary<string, double> _installing = [];

    public PageDownloadHorizonBuild()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshLists();
            if (_releases is null && !_loading)
                _ = LoadReleasesAsync();
        };
        HorizonEnvironment.InstalledBuildsChanged += () => Common.RunInUi(RefreshLists);
    }

    protected override void RefreshLocalization()
    {
        RefreshLists();
    }

    /// <summary>
    ///     重新获取版本列表。
    /// </summary>
    public void Refresh()
    {
        _ = LoadReleasesAsync();
    }

    private async Task LoadReleasesAsync()
    {
        if (_loading)
            return;
        _loading = true;
        _releasesError = null;
        RefreshLists();
        try
        {
            _releases = await HorizonEnvironment.ReleaseService.ListReleasesAsync();
        }
        catch (Exception ex)
        {
            Common.Log(ex, "[Horizon] 获取版本列表失败");
            _releasesError = ex;
        }
        finally
        {
            _loading = false;
        }

        RefreshLists();
    }

    private void RefreshLists()
    {
        var installed = HorizonEnvironment.InstalledBuilds();
        var latest = _releases?.FirstOrDefault();

        CardInstalled.Title = Lang.Text("Horizon.Build.Installed.Card", Lang.Number(installed.Count, "N0"));
        CardInstalled.Visibility = installed.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PanInstalled.Children.Clear();
        foreach (var build in installed)
            PanInstalled.Children.Add(CreateInstalledItem(build, latest));

        if (_loading)
            ShowStatus(Lang.Text("Horizon.Build.Loading"), MyHint.Themes.Blue);
        else if (_releasesError is not null)
            ShowStatus(Lang.Text("Horizon.Build.Error.List", _releasesError.Message), MyHint.Themes.Red);
        else if (installed.Count == 0)
            ShowStatus(Lang.Text("Horizon.Build.NoneInstalled"), MyHint.Themes.Yellow);
        else
            HintStatus.Visibility = Visibility.Collapsed;

        var releases = _releases ?? [];
        CardReleases.Title = Lang.Text("Horizon.Build.Releases.Card", Lang.Number(releases.Count, "N0"));
        CardReleases.Visibility = releases.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PanReleases.Children.Clear();
        var installedVersions = installed.Select(b => b.Version).ToHashSet();
        foreach (var release in releases)
            PanReleases.Children.Add(CreateReleaseItem(release, installedVersions.Contains(release.Version)));
    }

    private void ShowStatus(string text, MyHint.Themes theme)
    {
        HintStatus.Text = text;
        HintStatus.Theme = theme;
        HintStatus.Visibility = Visibility.Visible;
    }

    private MyListItem CreateInstalledItem(HorizonBuild build, HorizonRelease? latest)
    {
        var users = HorizonEnvironment.Instances.Count(i => i.BuildVersion == build.Version);
        var info = new List<string>();
        if (build.Info is { } buildInfo)
            info.Add(Lang.Text("Horizon.Build.InstalledAt", Lang.Date(buildInfo.InstalledAt.LocalDateTime)));
        if (users > 0)
            info.Add(Lang.Text("Horizon.Build.PinnedBy", Lang.Number(users, "N0")));
        if (!build.IsComplete)
            info.Add(Lang.Text("Horizon.Build.Incomplete"));

        var item = new MyListItem
        {
            Title = build.Version,
            Info = string.Join("  ·  ", info),
            Height = 42d,
            Type = MyListItem.CheckType.None,
            Logo = "pack://application:,,,/images/Blocks/Grass.png"
        };
        if (latest is not null && latest.Version == build.Version)
            item.PlainTags = [Lang.Text("Horizon.Build.Latest")];
        item.Buttons =
        [
            CreateButton("lucide/folder-open", "Common.Action.OpenFolder", () => Common.OpenExplorer(build.Directory)),
            CreateButton("lucide/trash-2", "Common.Action.Delete", () => Delete(build, users))
        ];
        return item;
    }

    private MyListItem CreateReleaseItem(HorizonRelease release, bool installed)
    {
        var info = new List<string> { release.Date, release.Commit };
        if (_installing.TryGetValue(release.Version, out var progress))
            info.Add(Lang.Text("Horizon.Build.Installing", Lang.Number(progress, "P0")));
        else if (installed)
            info.Add(Lang.Text("Horizon.Build.AlreadyInstalled"));

        var item = new MyListItem
        {
            Title = release.Version,
            Info = string.Join("  ·  ", info),
            Height = 42d,
            Type = installed || _installing.ContainsKey(release.Version)
                ? MyListItem.CheckType.None
                : MyListItem.CheckType.Clickable,
            Logo = "pack://application:,,,/images/Blocks/Grass.png",
            Opacity = installed ? 0.6d : 1d
        };
        if (release.IsLatest)
            item.PlainTags = [Lang.Text("Horizon.Build.Latest")];
        if (!installed && !_installing.ContainsKey(release.Version))
        {
            item.Click += (_, _) => _ = InstallAsync(release);
            item.Buttons = [CreateButton("lucide/download", "Horizon.Build.Install", () => _ = InstallAsync(release))];
        }

        return item;
    }

    private static MyIconButton CreateButton(string icon, string toolTipKey, Action action)
    {
        var button = new MyIconButton { LogoScale = 1.1d, SvgIcon = icon };
        button.TrySetLocalizedValue("ToolTip", toolTipKey);
        ToolTipService.SetPlacement(button, PlacementMode.Center);
        ToolTipService.SetVerticalOffset(button, 30d);
        ToolTipService.SetHorizontalOffset(button, 2d);
        button.Click += (_, _) => action();
        return button;
    }

    private async Task InstallAsync(HorizonRelease release)
    {
        if (!_installing.TryAdd(release.Version, 0))
            return;
        RefreshLists();
        var lastShown = 0d;
        var progress = new Progress<double>(p =>
        {
            _installing[release.Version] = p;
            if (p - lastShown < 0.05 && p < 1)
                return;
            lastShown = p;
            RefreshLists();
        });
        try
        {
            await HorizonEnvironment.ReleaseService.InstallAsync(release, HorizonEnvironment.Paths, progress);
            HintService.Show(Hints.Success(Lang.Text("Horizon.Build.Installed", release.Version)));
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.Build.Error.Install", release.Version), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Horizon.Build.Error.Install", release.Version));
        }
        finally
        {
            _installing.Remove(release.Version);
        }

        HorizonEnvironment.NotifyInstalledBuildsChanged();
    }

    private void Delete(HorizonBuild build, int users)
    {
        if (HorizonLaunch.IsLaunching || HorizonGames.Running.Any(g => g.Build.Directory == build.Directory))
        {
            HintService.Show(Hints.Error(Lang.Text("Horizon.Build.Delete.InUse")));
            return;
        }

        var content = Lang.Text("Horizon.Build.Delete.Confirm", build.Version);
        if (users > 0)
            content += "\n\n" + Lang.Text("Horizon.Build.Delete.PinnedWarning", Lang.Number(users, "N0"));
        if (DialogService.Instance.Show(new TextDialogRequest
            {
                Title = Lang.Text("Common.Dialog.Warning"),
                Content = content,
                Buttons = DialogButtons.OkCancel,
                IsWarn = true,
                ForceWait = true
            }).Result?.Button != 1)
            return;
        try
        {
            HorizonReleaseService.DeleteInstalled(HorizonEnvironment.Paths, build.Version);
            HintService.Show(Hints.Success(Lang.Text("Horizon.Build.Deleted", build.Version)));
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.Build.Error.Delete"), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Horizon.Build.Error.Delete"));
        }

        HorizonEnvironment.NotifyInstalledBuildsChanged();
    }

    private void BtnOpenFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var dir = HorizonEnvironment.Paths.VersionsDir;
        Directory.CreateDirectory(dir);
        Common.OpenExplorer(dir + "\\");
    }

    private void BtnRefresh_Click(object sender, MouseButtonEventArgs e)
    {
        Refresh();
    }
}
