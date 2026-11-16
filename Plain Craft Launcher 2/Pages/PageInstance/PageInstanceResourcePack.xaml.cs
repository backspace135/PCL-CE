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
///     实例的资源包：列出 <c>resourcepacks/</c>，在 <c>options.txt</c> 中启用或停用，删除。
/// </summary>
public partial class PageInstanceResourcePack : IRefreshable
{
    public PageInstanceResourcePack()
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
        IReadOnlyList<HorizonResourcePack> packs;
        try
        {
            packs = HorizonResourcePacks.List(instance);
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.ResourcePack.Error.Load"), Common.LogLevel.Hint,
                userSummary: Lang.Text("Horizon.ResourcePack.Error.Load"));
            packs = [];
        }

        CardList.Title = Lang.Text("Horizon.ResourcePack.Card", Lang.Number(packs.Count, "N0"));
        CardList.Visibility = packs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HintEmpty.Visibility = packs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanList.Children.Clear();
        foreach (var pack in packs)
            PanList.Children.Add(CreateItem(pack));
    }

    private MyListItem CreateItem(HorizonResourcePack pack)
    {
        var info = pack.IsFolder
            ? Lang.Text("Horizon.ResourcePack.Folder")
            : Lang.Text("Horizon.ResourcePack.Size", Lang.Number(pack.Size / 1024d / 1024d, "N2"));
        var item = new MyListItem
        {
            Title = Path.GetFileNameWithoutExtension(pack.Name),
            Info = $"{info}  ·  {Lang.Date(pack.Modified)}",
            Height = 42d,
            Type = MyListItem.CheckType.Clickable,
            Logo = "pack://application:,,,/images/Blocks/GrassPath.png",
            Opacity = pack.Enabled ? 1d : 0.6d
        };
        if (pack.Enabled)
            item.PlainTags = [Lang.Text("Horizon.ResourcePack.Enabled")];
        item.Click += (_, _) => Toggle(pack);
        item.Buttons =
        [
            CreateButton(pack.Enabled ? "lucide/eye-off" : "lucide/eye",
                pack.Enabled ? "Horizon.ResourcePack.Disable" : "Horizon.ResourcePack.Enable", () => Toggle(pack)),
            CreateButton("lucide/folder-open", "Common.Action.OpenFolder", () => Common.OpenExplorer(pack.Path)),
            CreateButton("lucide/trash-2", "Common.Action.Delete", () => Delete(pack))
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

    private void Toggle(HorizonResourcePack pack)
    {
        if (HorizonGames.HasRunningGame)
            HintService.Show(Hints.Info(Lang.Text("Horizon.ResourcePack.AppliesNextStart")));
        try
        {
            HorizonResourcePacks.SetEnabled(PageInstanceLeft.Instance, pack, !pack.Enabled);
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.ResourcePack.Error.Toggle"), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Horizon.ResourcePack.Error.Toggle"));
        }

        Refresh();
    }

    private void Delete(HorizonResourcePack pack)
    {
        if (DialogService.Instance.Show(new TextDialogRequest
            {
                Title = Lang.Text("Common.Dialog.Warning"),
                Content = Lang.Text("Horizon.ResourcePack.Delete.Confirm", pack.Name),
                Buttons = DialogButtons.OkCancel,
                IsWarn = true,
                ForceWait = true
            }).Result?.Button != 1)
            return;
        try
        {
            HorizonResourcePacks.Delete(PageInstanceLeft.Instance, pack);
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.ResourcePack.Error.Delete"), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Horizon.ResourcePack.Error.Delete"));
        }

        Refresh();
    }

    /// <summary>
    ///     把拖入的资源包复制到当前实例。
    /// </summary>
    public static void Import(IEnumerable<string> files)
    {
        var dir = PageInstanceLeft.Instance.ResourcePacksDir;
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

        Launcher.frmInstanceResourcePack?.Refresh();
    }

    private void BtnOpenFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var dir = PageInstanceLeft.Instance.ResourcePacksDir;
        Directory.CreateDirectory(dir);
        Common.OpenExplorer(dir + "\\");
    }

    private void BtnDownload_Click(object sender, MouseButtonEventArgs e)
    {
        Launcher.frmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadResourcePack);
    }

    private void BtnRefresh_Click(object sender, MouseButtonEventArgs e)
    {
        Refresh();
    }
}
