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
///     实例的模组：列出 <c>horizon-mods/</c>，在 <c>horizon-mods.txt</c> 中开启或关闭，删除。
///     对照注册表的撤下列表标出被撤下的模组；原生模组只展示，不开启。
/// </summary>
public partial class PageInstanceMod : IRefreshable
{
    /// <summary>被撤下的模组：文件 SHA-256 → 原因。注册表获取失败时为空。</summary>
    private Dictionary<string, string> _revoked = [];

    public PageInstanceMod()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Refresh();
            _ = CheckRevokedAsync();
        };
    }

    protected override void RefreshLocalization()
    {
        Refresh();
    }

    public void Refresh()
    {
        var instance = PageInstanceLeft.Instance;
        IReadOnlyList<HorizonMod> mods;
        try
        {
            mods = HorizonMods.List(instance);
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.Mod.Error.Load"), Common.LogLevel.Hint,
                userSummary: Lang.Text("Horizon.Mod.Error.Load"));
            mods = [];
        }

        CardList.Title = Lang.Text("Horizon.Mod.Card", Lang.Number(mods.Count, "N0"));
        CardList.Visibility = mods.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HintEmpty.Visibility = mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanList.Children.Clear();
        foreach (var mod in mods)
            PanList.Children.Add(CreateItem(mod));
    }

    /// <summary>
    ///     获取注册表并计算已安装模组的 SHA-256，标出被撤下的模组。
    /// </summary>
    private async Task CheckRevokedAsync()
    {
        try
        {
            var registry = await HorizonEnvironment.GetRegistryAsync();
            var mods = HorizonMods.List(PageInstanceLeft.Instance);
            var revoked = await Task.Run(() =>
            {
                var result = new Dictionary<string, string>();
                foreach (var mod in mods)
                    if (HorizonMods.Sha256(mod) is { } sha && registry.RevokedReasonForFile(sha) is { } reason)
                        result[mod.Path] = reason;
                return result;
            });
            _revoked = revoked;
            if (revoked.Count == 0)
                return;
            Refresh();
            HintService.Show(Hints.Error(Lang.Text("Horizon.Mod.Revoked.Hint", Lang.Number(revoked.Count, "N0"))));
        }
        catch (Exception ex)
        {
            Common.Log(ex, "[Horizon] 检查模组撤下列表失败");
        }
    }

    private MyListItem CreateItem(HorizonMod mod)
    {
        var infoParts = new List<string> { mod.Id };
        if (mod.Version is not null)
            infoParts.Add(mod.Version);
        infoParts.Add(mod.IsFolder
            ? Lang.Text("Horizon.Mod.Folder")
            : Lang.Text("Horizon.Mod.Size", Lang.Number(mod.Size / 1024d / 1024d, "N2")));
        infoParts.Add(Lang.Date(mod.Modified));
        _revoked.TryGetValue(mod.Path, out var revokedReason);

        var item = new MyListItem
        {
            Title = mod.Name,
            Info = revokedReason is null
                ? string.Join("  ·  ", infoParts)
                : Lang.Text("Horizon.Mod.Revoked.Info", revokedReason),
            Height = 42d,
            Type = mod.IsNative ? MyListItem.CheckType.None : MyListItem.CheckType.Clickable,
            Logo = "pack://application:,,,/images/Blocks/CommandBlock.png",
            Opacity = mod.Enabled ? 1d : 0.6d
        };
        var tags = new List<string>();
        if (mod.Enabled)
            tags.Add(Lang.Text("Horizon.Mod.Enabled"));
        if (mod.IsNative)
            tags.Add(Lang.Text("Horizon.Mod.Native"));
        if (revokedReason is not null)
            tags.Add(Lang.Text("Horizon.Mod.Revoked"));
        item.PlainTags = tags;
        if (mod.IsNative)
        {
            item.ToolTip = Lang.Text("Horizon.Mod.Native.ToolTip");
            item.Buttons =
            [
                CreateButton("lucide/folder-open", "Common.Action.OpenFolder", () => Common.OpenExplorer(mod.Path)),
                CreateButton("lucide/trash-2", "Common.Action.Delete", () => Delete(mod))
            ];
            return item;
        }

        item.Click += (_, _) => Toggle(mod);
        item.Buttons =
        [
            CreateButton(mod.Enabled ? "lucide/eye-off" : "lucide/eye",
                mod.Enabled ? "Horizon.Mod.Disable" : "Horizon.Mod.Enable", () => Toggle(mod)),
            CreateButton("lucide/folder-open", "Common.Action.OpenFolder", () => Common.OpenExplorer(mod.Path)),
            CreateButton("lucide/trash-2", "Common.Action.Delete", () => Delete(mod))
        ];
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

    private void Toggle(HorizonMod mod)
    {
        if (!mod.Enabled && _revoked.TryGetValue(mod.Path, out var reason) &&
            DialogService.Instance.Show(new TextDialogRequest
            {
                Title = Lang.Text("Common.Dialog.Warning"),
                Content = Lang.Text("Horizon.Mod.Revoked.EnableConfirm", mod.Name, reason),
                Buttons = DialogButtons.OkCancel,
                IsWarn = true,
                ForceWait = true
            }).Result?.Button != 1)
            return;
        if (HorizonGames.HasRunningGame)
            HintService.Show(Hints.Info(Lang.Text("Horizon.Mod.AppliesNextStart")));
        try
        {
            HorizonMods.SetEnabled(PageInstanceLeft.Instance, mod, !mod.Enabled);
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.Mod.Error.Toggle"), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Horizon.Mod.Error.Toggle"));
        }

        Refresh();
    }

    private void Delete(HorizonMod mod)
    {
        if (DialogService.Instance.Show(new TextDialogRequest
            {
                Title = Lang.Text("Common.Dialog.Warning"),
                Content = Lang.Text("Horizon.Mod.Delete.Confirm", mod.Name),
                Buttons = DialogButtons.OkCancel,
                IsWarn = true,
                ForceWait = true
            }).Result?.Button != 1)
            return;
        try
        {
            HorizonMods.Delete(PageInstanceLeft.Instance, mod);
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.Mod.Error.Delete"), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Horizon.Mod.Error.Delete"));
        }

        Refresh();
    }

    /// <summary>
    ///     把拖入的 <c>.hmod</c> 复制到当前实例。导入的模组默认关闭。
    /// </summary>
    public static void Import(IEnumerable<string> files)
    {
        var dir = PageInstanceLeft.Instance.ModsDir;
        Directory.CreateDirectory(dir);
        foreach (var file in files)
        {
            var dest = Path.Combine(dir, Path.GetFileName(file));
            if (File.Exists(dest))
            {
                HintService.Show(Hints.Error(Lang.Text("Main.FileDrag.SameFileExists", dest)));
                continue;
            }

            File.Copy(file, dest);
            HintService.Show(Hints.Success(Lang.Text("Main.FileDrag.Imported", Path.GetFileName(file))));
        }

        Launcher.frmInstanceMod?.Refresh();
    }

    private void BtnOpenFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var dir = PageInstanceLeft.Instance.ModsDir;
        Directory.CreateDirectory(dir);
        Common.OpenExplorer(dir + "\\");
    }

    private void BtnDownload_Click(object sender, MouseButtonEventArgs e)
    {
        Launcher.frmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadHorizonContent);
    }

    private void BtnRefresh_Click(object sender, MouseButtonEventArgs e)
    {
        Refresh();
        _ = CheckRevokedAsync();
    }
}
