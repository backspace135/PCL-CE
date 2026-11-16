using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.VisualBasic.FileIO;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using PCL.Core.UI.Dialogs;
using PCL.Core.UI.Hints;
using PCL.Horizon;

namespace PCL;

/// <summary>
///     实例选择页右侧：列出 Horizon 实例，点击选中，可打开设置、文件夹或删除。
/// </summary>
public partial class PageSelectRight
{
    public PageSelectRight()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        HorizonEnvironment.InstancesChanged += () => Common.RunInUi(Refresh);
    }

    protected override void RefreshLocalization()
    {
        Refresh();
    }

    /// <summary>
    ///     按当前实例列表重建界面。
    /// </summary>
    public void Refresh()
    {
        var instances = HorizonEnvironment.Instances;
        var selected = HorizonEnvironment.SelectedInstance;
        PanEmpty.Visibility = instances.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanBack.Visibility = instances.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        CardInstances.Title = Lang.Text("Horizon.Select.Card", Lang.Number(instances.Count, "N0"));
        PanInstances.Children.Clear();
        foreach (var instance in instances)
            PanInstances.Children.Add(CreateItem(instance, instance.Id == selected?.Id));
    }

    /// <summary>
    ///     实例的说明：使用的构建与上次游玩时间。
    /// </summary>
    public static string Describe(HorizonInstance instance)
    {
        var build = instance.BuildVersion is null
            ? Lang.Text("Horizon.Instance.Build.Latest")
            : Lang.Text("Horizon.Instance.Build.Pinned", instance.BuildVersion);
        return instance.LastPlayedAt is { } played
            ? $"{build}  ·  {Lang.Text("Horizon.Instance.LastPlayed", Lang.Date(played.LocalDateTime))}"
            : build;
    }

    private static MyListItem CreateItem(HorizonInstance instance, bool isSelected)
    {
        var item = new MyListItem
        {
            Title = instance.Name,
            Info = Describe(instance),
            Height = 42d,
            Tag = instance,
            SnapsToDevicePixels = true,
            Type = MyListItem.CheckType.Clickable,
            Logo = "pack://application:,,,/images/Blocks/Grass.png"
        };
        if (isSelected)
            item.PlainTags = [Lang.Text("Horizon.Select.Selected")];
        item.Click += (_, _) =>
        {
            HorizonEnvironment.SelectInstance(instance);
            Launcher.frmMain.PageBack();
        };
        item.MouseRightButtonUp += (_, _) => OpenSettings(instance);
        item.Buttons =
        [
            CreateButton("lucide/folder-open", "Select.Instance.OpenFolder", () => Common.OpenExplorer(instance.Directory + "\\")),
            CreateButton("lucide/trash-2", "Common.Action.Delete", () => DeleteInstance(instance)),
            CreateButton("lucide/settings", "Select.Instance.Settings", () => OpenSettings(instance))
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

    /// <summary>
    ///     打开实例设置页。
    /// </summary>
    public static void OpenSettings(HorizonInstance instance)
    {
        PageInstanceLeft.Instance = instance;
        Launcher.frmMain.PageChange(FormMain.PageType.InstanceSetup);
    }

    /// <summary>
    ///     确认后删除实例（含存档）：默认移到回收站，按住 Shift 时永久删除。返回是否已删除。
    /// </summary>
    public static bool DeleteInstance(HorizonInstance instance)
    {
        var permanent = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        var confirmed = DialogService.Instance.Show(new TextDialogRequest
        {
            Title = Lang.Text("Select.Instance.Delete.ConfirmTitle"),
            Content = Lang.Text(
                permanent ? "Horizon.Instance.Delete.ConfirmPermanent" : "Horizon.Instance.Delete.Confirm",
                instance.Name),
            Buttons = DialogButtons.OkCancel,
            IsWarn = true,
            ForceWait = true
        }).Result?.Button == 1;
        if (!confirmed)
            return false;
        try
        {
            if (permanent)
            {
                HorizonEnvironment.DeleteInstance(instance);
                HintService.Show(Hints.Success(Lang.Text("Select.Instance.Delete.PermanentSuccess", instance.Name)));
            }
            else
            {
                FileSystem.DeleteDirectory(instance.Directory, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
                HorizonEnvironment.ReloadInstances();
                HintService.Show(Hints.Success(Lang.Text("Select.Instance.Delete.RecycleBinSuccess", instance.Name)));
            }

            return true;
        }
        catch (OperationCanceledException ex)
        {
            Common.Log(ex, $"删除实例 {instance.Name} 被主动取消");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Common.Log(ex, Lang.Text("Select.Instance.Error.Delete", instance.Name), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Select.Instance.Error.Delete", instance.Name));
            return false;
        }
    }

    private void BtnCreate_Click(object sender, MouseButtonEventArgs e)
    {
        _ = PageSelectLeft.CreateInstanceInteractive();
    }
}
