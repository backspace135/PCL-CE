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
///     存档的数据包：列出 <c>datapacks/</c>，在 <c>datapacks.json</c> 中启用或停用，删除。
/// </summary>
public partial class PageInstanceSavesDatapack : IRefreshable
{
    public PageInstanceSavesDatapack()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private static string World => PageInstanceSavesLeft.currentSave;

    protected override void RefreshLocalization()
    {
        Refresh();
    }

    public void Refresh()
    {
        IReadOnlyList<HorizonDatapack> packs;
        try
        {
            packs = HorizonDatapacks.List(World);
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.Datapack.Error.Load"), Common.LogLevel.Hint,
                userSummary: Lang.Text("Horizon.Datapack.Error.Load"));
            packs = [];
        }

        CardList.Title = Lang.Text("Horizon.Datapack.Card", Lang.Number(packs.Count, "N0"));
        CardList.Visibility = packs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HintEmpty.Visibility = packs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanList.Children.Clear();
        foreach (var pack in packs)
            PanList.Children.Add(CreateItem(pack));
    }

    private MyListItem CreateItem(HorizonDatapack pack)
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
            Logo = "pack://application:,,,/images/Blocks/CommandBlock.png",
            Opacity = pack.Enabled ? 1d : 0.6d
        };
        if (!pack.Enabled)
            item.PlainTags = [Lang.Text("Horizon.Datapack.Disabled")];
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

    private void Toggle(HorizonDatapack pack)
    {
        try
        {
            HorizonDatapacks.SetEnabled(World, pack, !pack.Enabled);
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.ResourcePack.Error.Toggle"), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Horizon.ResourcePack.Error.Toggle"));
        }

        Refresh();
    }

    private void Delete(HorizonDatapack pack)
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
            HorizonDatapacks.Delete(World, pack);
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.ResourcePack.Error.Delete"), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Horizon.ResourcePack.Error.Delete"));
        }

        Refresh();
    }

    private void BtnOpenFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var dir = HorizonDatapacks.DatapacksDir(World);
        Directory.CreateDirectory(dir);
        Common.OpenExplorer(dir + "\\");
    }

    private void BtnDownload_Click(object sender, MouseButtonEventArgs e)
    {
        var dir = HorizonDatapacks.DatapacksDir(World);
        Directory.CreateDirectory(dir);
        PageDownloadCompDetail.cachedFolder[Comp.CompType.DataPack] = dir;
        Launcher.frmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadDataPack);
    }

    private void BtnRefresh_Click(object sender, MouseButtonEventArgs e)
    {
        Refresh();
    }
}
