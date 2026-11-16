using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PCL.Core.App.Localization;
using PCL.Core.IO.Net;
using PCL.Core.UI;
using PCL.Core.UI.Dialogs;
using PCL.Core.UI.Hints;
using PCL.Horizon;

namespace PCL;

/// <summary>
///     Horizon 内容注册表：搜索模组、光影、资源包与数据包，把与选中实例的构建兼容的最新版本安装到实例中。
///     安装前按注册表校验来源、大小、SHA-256、签名与撤下列表。
/// </summary>
public partial class PageDownloadHorizonContent : IRefreshable
{
    /// <summary>列表最多显示的项目数。</summary>
    private const int MaxShown = 100;

    private static readonly string?[] KindFilters =
    [
        null, HorizonRegistry.KindMod, HorizonRegistry.KindShader, HorizonRegistry.KindResourcePack,
        HorizonRegistry.KindDatapack
    ];

    private HorizonRegistry? _registry;
    private Exception? _error;
    private bool _loading;
    private HorizonInstance? _instance;
    private HorizonLaunchApi? _client;
    private readonly HashSet<string> _installing = [];

    public PageDownloadHorizonContent()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = LoadAsync(false);
    }

    protected override void RefreshLocalization()
    {
        Render();
    }

    /// <summary>
    ///     重新获取注册表索引。
    /// </summary>
    public void Refresh()
    {
        _ = LoadAsync(true);
    }

    private async Task LoadAsync(bool forceRefresh)
    {
        if (_loading)
            return;
        _loading = true;
        _error = null;
        Render();
        try
        {
            _instance = HorizonEnvironment.SelectedInstance;
            _client = null;
            if (_instance is not null && HorizonEnvironment.ResolveBuild(_instance) is { } build)
            {
                try
                {
                    _client = (await HorizonEnvironment.GetBuildApiAsync(build)).Client;
                }
                catch (Exception ex)
                {
                    Common.Log(ex, $"[Horizon] 读取构建 {build.Version} 的 Launch API 失败");
                }
            }

            _registry = await HorizonEnvironment.GetRegistryAsync(forceRefresh);
        }
        catch (Exception ex)
        {
            Common.Log(ex, "[Horizon] 获取内容注册表失败");
            _error = ex;
        }
        finally
        {
            _loading = false;
        }

        Render();
    }

    private void Render()
    {
        RenderTarget();

        if (_loading)
        {
            ShowStatus(Lang.Text("Horizon.Content.Loading"), MyHint.Themes.Blue);
            CardList.Visibility = Visibility.Collapsed;
            return;
        }

        if (_error is not null || _registry is null)
        {
            ShowStatus(Lang.Text("Horizon.Content.Error.Load", _error?.Message ?? ""), MyHint.Themes.Red);
            CardList.Visibility = Visibility.Collapsed;
            return;
        }

        var results = Filter(_registry).ToList();
        PanList.Children.Clear();
        if (results.Count == 0)
        {
            ShowStatus(Lang.Text("Horizon.Content.NoResults"), MyHint.Themes.Blue);
            CardList.Visibility = Visibility.Collapsed;
            return;
        }

        HintStatus.Visibility = Visibility.Collapsed;
        CardList.Visibility = Visibility.Visible;
        CardList.Title = results.Count > MaxShown
            ? Lang.Text("Horizon.Content.Card.Truncated", Lang.Number(MaxShown, "N0"), Lang.Number(results.Count, "N0"))
            : Lang.Text("Horizon.Content.Card", Lang.Number(results.Count, "N0"));
        foreach (var project in results.Take(MaxShown))
            PanList.Children.Add(CreateItem(project));
    }

    private void RenderTarget()
    {
        if (_instance is null)
        {
            HintTarget.Text = Lang.Text("Horizon.Content.Target.NoInstance");
            HintTarget.Theme = MyHint.Themes.Yellow;
        }
        else if (_client is null)
        {
            HintTarget.Text = Lang.Text("Horizon.Content.Target.NoBuild", _instance.Name);
            HintTarget.Theme = MyHint.Themes.Yellow;
        }
        else
        {
            HintTarget.Text = Lang.Text("Horizon.Content.Target", _instance.Name, _client.Horizon ?? "?",
                _client.Minecraft ?? "?");
            HintTarget.Theme = MyHint.Themes.Blue;
        }
    }

    private void ShowStatus(string text, MyHint.Themes theme)
    {
        HintStatus.Text = text;
        HintStatus.Theme = theme;
        HintStatus.Visibility = Visibility.Visible;
    }

    private IEnumerable<HorizonRegistryProject> Filter(HorizonRegistry registry)
    {
        var kind = ComboKind.SelectedIndex is >= 0 and var i && i < KindFilters.Length ? KindFilters[i] : null;
        var words = (PanSearchBox.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var compatibleOnly = CheckCompatibleOnly.Checked == true && _client is not null;
        return registry.Projects
            .Where(p => HorizonRegistry.IsInstallableKind(p.Kind) && p.Versions.Count > 0)
            .Where(p => kind is null || p.Kind == kind ||
                        (kind == HorizonRegistry.KindShader && p.Kind == HorizonRegistry.KindShaderPack))
            .Where(p => words.All(w => Matches(p, w)))
            .Where(p => !compatibleOnly || FindInstallable(registry, p) is not null)
            .OrderByDescending(p => p.Downloads)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private static bool Matches(HorizonRegistryProject project, string word) =>
        project.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase) ||
        project.Id.Contains(word, StringComparison.OrdinalIgnoreCase) ||
        project.Summary.Contains(word, StringComparison.CurrentCultureIgnoreCase) ||
        project.Authors.Any(a => a.Contains(word, StringComparison.CurrentCultureIgnoreCase)) ||
        project.Tags.Any(t => t.Contains(word, StringComparison.CurrentCultureIgnoreCase)) ||
        project.Categories.Any(c => c.Contains(word, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>
    ///     与实例构建兼容、未被撤下的最新版本；没有构建或没有兼容版本时为 <see langword="null" />。
    /// </summary>
    private HorizonRegistryVersion? FindInstallable(HorizonRegistry registry, HorizonRegistryProject project)
    {
        if (_client is null)
            return null;
        return project.Versions.FirstOrDefault(v =>
            HorizonRegistry.IsCompatible(project, v, _client) &&
            registry.RevokedReason(project.Id, v.File.Sha256) is null);
    }

    private static string KindName(string kind) => kind switch
    {
        HorizonRegistry.KindMod => Lang.Text("Horizon.Content.Kind.Mod"),
        HorizonRegistry.KindShader or HorizonRegistry.KindShaderPack => Lang.Text("Horizon.Content.Kind.Shader"),
        HorizonRegistry.KindResourcePack => Lang.Text("Horizon.Content.Kind.ResourcePack"),
        HorizonRegistry.KindDatapack => Lang.Text("Horizon.Content.Kind.Datapack"),
        _ => kind
    };

    private static string KindLogo(string kind) => kind switch
    {
        HorizonRegistry.KindMod => "pack://application:,,,/images/Blocks/CommandBlock.png",
        HorizonRegistry.KindShader or HorizonRegistry.KindShaderPack =>
            "pack://application:,,,/images/Blocks/RedstoneLampOn.png",
        HorizonRegistry.KindResourcePack => "pack://application:,,,/images/Blocks/GrassPath.png",
        _ => "pack://application:,,,/images/Blocks/RedstoneBlock.png"
    };

    private MyListItem CreateItem(HorizonRegistryProject project)
    {
        var installable = FindInstallable(_registry!, project);
        var installing = _installing.Contains(project.Id);
        var info = new List<string>();
        if (!string.IsNullOrWhiteSpace(project.Summary))
            info.Add(project.Summary);
        if (project.Authors.Count > 0)
            info.Add(string.Join(", ", project.Authors));
        if (installing)
            info.Add(Lang.Text("Horizon.Content.Installing"));
        else if (_client is not null && installable is null)
            info.Add(Lang.Text("Horizon.Content.Incompatible"));

        var item = new MyListItem
        {
            Title = project.Name,
            Info = string.Join("  ·  ", info),
            Height = 42d,
            Type = installable is not null && !installing
                ? MyListItem.CheckType.Clickable
                : MyListItem.CheckType.None,
            Logo = Uri.TryCreate(project.Icon, UriKind.Absolute, out var icon) && icon.Scheme == Uri.UriSchemeHttps
                ? icon.AbsoluteUri
                : KindLogo(project.Kind),
            Opacity = installable is null && _client is not null ? 0.6d : 1d
        };
        var tags = new List<string> { KindName(project.Kind) };
        if (installable is not null)
            tags.Add(installable.Version);
        if (project.Downloads > 0)
            tags.Add(Lang.Text("Horizon.Content.Downloads", Lang.Number(project.Downloads, "N0")));
        item.PlainTags = tags;

        var buttons = new List<MyIconButton>();
        if (installable is not null && !installing)
        {
            item.Click += (_, _) => _ = InstallAsync(project, installable);
            buttons.Add(CreateButton("lucide/download", "Horizon.Content.Install",
                () => _ = InstallAsync(project, installable)));
        }

        if (Uri.TryCreate(project.Source, UriKind.Absolute, out var source) && source.Scheme == Uri.UriSchemeHttps)
            buttons.Add(CreateButton("lucide/external-link", "Horizon.Content.OpenSource",
                () => Common.OpenWebsite(source.AbsoluteUri)));
        item.Buttons = buttons;
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

    private async Task InstallAsync(HorizonRegistryProject project, HorizonRegistryVersion version)
    {
        var instance = _instance;
        var registry = _registry;
        if (instance is null || registry is null || _installing.Contains(project.Id))
            return;

        string? world = null;
        if (project.Kind == HorizonRegistry.KindDatapack)
        {
            world = SelectWorld(instance);
            if (world is null)
                return;
        }

        _installing.Add(project.Id);
        Render();
        try
        {
            var folder = HorizonRegistry.TargetFolder(instance, project.Kind, world);
            await registry.InstallAsync(NetworkService.GetClient(), project, version, folder);
            if (project.Kind == HorizonRegistry.KindMod)
                HorizonMods.SetEnabled(instance, project.Id, true);
            Common.Log($"[Horizon] 已安装 {project.Id} {version.Version} 到 {folder}");
            HintService.Show(Hints.Success(Lang.Text("Horizon.Content.Installed", project.Name, version.Version,
                instance.Name)));
            if (HorizonGames.HasRunningGame && project.Kind is HorizonRegistry.KindMod)
                HintService.Show(Hints.Info(Lang.Text("Horizon.Mod.AppliesNextStart")));
            Launcher.frmInstanceMod?.Refresh();
            Launcher.frmInstanceShader?.Refresh();
            Launcher.frmInstanceResourcePack?.Refresh();
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.Content.Error.Install", project.Name), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Horizon.Content.Error.Install", project.Name));
        }
        finally
        {
            _installing.Remove(project.Id);
        }

        Render();
    }

    /// <summary>
    ///     让用户选择安装数据包的存档，返回存档文件夹名；没有存档或取消时为 <see langword="null" />。
    /// </summary>
    private static string? SelectWorld(HorizonInstance instance)
    {
        var worlds = Directory.Exists(instance.SavesDir)
            ? Directory.EnumerateDirectories(instance.SavesDir)
                .Where(d => File.Exists(Path.Combine(d, "level.dat")))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .ToList()
            : [];
        if (worlds.Count == 0)
        {
            HintService.Show(Hints.Error(Lang.Text("Horizon.Content.Datapack.NoWorld", instance.Name)));
            return null;
        }

        var result = DialogService.Instance.Show(new SelectDialogRequest
        {
            Title = Lang.Text("Horizon.Content.Datapack.SelectWorld"),
            Options = [.. worlds.Select(w => new DialogOption { Text = w })],
            Buttons = DialogButtons.OkCancel,
            ForceWait = true
        }).Result;
        return result is { IsConfirmed: true, SelectedIndex: { } index } && index >= 0 && index < worlds.Count
            ? worlds[index]
            : null;
    }

    private void PanSearchBox_TextChanged(object sender, EventArgs e)
    {
        if (IsLoaded)
            Render();
    }

    private void ComboKind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            Render();
    }

    private void CheckCompatibleOnly_Change(object sender, bool user)
    {
        if (IsLoaded)
            Render();
    }
}
