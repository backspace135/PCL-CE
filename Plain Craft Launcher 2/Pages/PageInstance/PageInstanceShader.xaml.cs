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
///     实例的光影：列出 <c>shaderpacks/</c>，在 <c>horizon-graphics.txt</c> 中选中一个或不使用光影，删除。
/// </summary>
public partial class PageInstanceShader : IRefreshable
{
    public PageInstanceShader()
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
        IReadOnlyList<HorizonShaderPack> packs;
        try
        {
            packs = HorizonShaders.List(instance);
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.Shader.Error.Load"), Common.LogLevel.Hint,
                userSummary: Lang.Text("Horizon.Shader.Error.Load"));
            packs = [];
        }

        CardList.Title = Lang.Text("Horizon.Shader.Card", Lang.Number(packs.Count, "N0"));
        CardList.Visibility = packs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HintEmpty.Visibility = packs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanList.Children.Clear();
        if (packs.Count == 0)
            return;
        PanList.Children.Add(CreateNoneItem(packs.All(p => !p.Selected)));
        foreach (var pack in packs)
            PanList.Children.Add(CreateItem(pack));
    }

    private MyListItem CreateNoneItem(bool selected)
    {
        var item = new MyListItem
        {
            Title = Lang.Text("Horizon.Shader.None"),
            Info = Lang.Text("Horizon.Shader.None.Info"),
            Height = 42d,
            Type = MyListItem.CheckType.Clickable,
            SvgIcon = "lucide/circle-off",
            Opacity = selected ? 1d : 0.6d
        };
        if (selected)
            item.PlainTags = [Lang.Text("Horizon.Shader.Selected")];
        item.Click += (_, _) => Select(null);
        return item;
    }

    private MyListItem CreateItem(HorizonShaderPack pack)
    {
        var info = new List<string>
        {
            Lang.Text(pack.IsHorizonPack ? "Horizon.Shader.Type.Horizon" : "Horizon.Shader.Type.ShaderPack"),
            pack.IsFolder
                ? Lang.Text("Horizon.Shader.Folder")
                : Lang.Text("Horizon.Shader.Size", Lang.Number(pack.Size / 1024d / 1024d, "N2")),
            Lang.Date(pack.Modified)
        };
        var item = new MyListItem
        {
            Title = pack.IsFolder ? pack.Name : Path.GetFileNameWithoutExtension(pack.Name),
            Info = string.Join("  ·  ", info),
            Height = 42d,
            Type = MyListItem.CheckType.Clickable,
            Logo = "pack://application:,,,/images/Blocks/RedstoneLampOn.png",
            Opacity = pack.Selected ? 1d : 0.6d
        };
        if (pack.Selected)
            item.PlainTags = [Lang.Text("Horizon.Shader.Selected")];
        item.Click += (_, _) => Select(pack.Selected ? null : pack.Name);
        item.Buttons =
        [
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

    private void Select(string? name)
    {
        if (HorizonGames.HasRunningGame)
            HintService.Show(Hints.Info(Lang.Text("Horizon.Shader.AppliesNextStart")));
        try
        {
            HorizonShaders.Select(PageInstanceLeft.Instance, name);
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.Shader.Error.Select"), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Horizon.Shader.Error.Select"));
        }

        Refresh();
    }

    private void Delete(HorizonShaderPack pack)
    {
        if (DialogService.Instance.Show(new TextDialogRequest
            {
                Title = Lang.Text("Common.Dialog.Warning"),
                Content = Lang.Text("Horizon.Shader.Delete.Confirm", pack.Name),
                Buttons = DialogButtons.OkCancel,
                IsWarn = true,
                ForceWait = true
            }).Result?.Button != 1)
            return;
        try
        {
            HorizonShaders.Delete(PageInstanceLeft.Instance, pack);
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.Shader.Error.Delete"), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Horizon.Shader.Error.Delete"));
        }

        Refresh();
    }

    /// <summary>
    ///     把拖入的光影复制到当前实例。
    /// </summary>
    public static void Import(IEnumerable<string> files)
    {
        var dir = PageInstanceLeft.Instance.ShaderPacksDir;
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

        Launcher.frmInstanceShader?.Refresh();
    }

    private void BtnOpenFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var dir = PageInstanceLeft.Instance.ShaderPacksDir;
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
    }
}
