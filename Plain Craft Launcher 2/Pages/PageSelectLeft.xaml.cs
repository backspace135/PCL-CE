using System.IO;
using System.Windows.Input;
using PCL.Core.App.Localization;
using PCL.Core.UI.Dialogs;
using PCL.Core.UI.Hints;
using PCL.Core.Utils.Validate;

namespace PCL;

/// <summary>
///     实例选择页左侧：新建实例、打开实例文件夹、刷新。
/// </summary>
public partial class PageSelectLeft : IRefreshable
{
    public PageSelectLeft()
    {
        InitializeComponent();
    }

    void IRefreshable.Refresh()
    {
        HorizonEnvironment.ReloadInstances();
    }

    private void ItemCreate_Click(object sender, MouseButtonEventArgs e)
    {
        _ = CreateInstanceInteractive();
    }

    private void ItemOpenFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var folder = HorizonEnvironment.Paths.InstancesDir;
        Directory.CreateDirectory(folder);
        Common.OpenExplorer(folder.TrimEnd('\\') + "\\");
    }

    private void ItemRefresh_Click(object sender, MouseButtonEventArgs e)
    {
        HorizonEnvironment.ReloadInstances();
        HintService.Show(Hints.Info(Lang.Text("Horizon.Select.Refreshed"), log: false));
    }

    /// <summary>
    ///     询问名称后新建实例并选中它。返回新建的实例，取消时为 <see langword="null" />。
    /// </summary>
    public static async Task<Horizon.HorizonInstance?> CreateInstanceInteractive()
    {
        var result = await DialogService.Instance.ShowAsync(new InputDialogRequest
        {
            Title = Lang.Text("Horizon.Select.Create.Title"),
            Content = Lang.Text("Horizon.Select.Create.Content"),
            DefaultValue = Lang.Text("Horizon.Select.Create.DefaultName"),
            ValidateRules = [new NullOrWhiteSpaceValidator()],
            Buttons = DialogButtons.OkCancel
        });
        if (!result.IsConfirmed || string.IsNullOrWhiteSpace(result.InputText))
            return null;
        try
        {
            var instance = HorizonEnvironment.CreateInstance(result.InputText.Trim());
            HintService.Show(Hints.Success(Lang.Text("Horizon.Select.Create.Success", instance.Name)));
            return instance;
        }
        catch (Exception ex)
        {
            Common.Log(ex, Lang.Text("Horizon.Select.Create.Failed"), Common.LogLevel.Msgbox,
                userSummary: Lang.Text("Horizon.Select.Create.Failed"));
            return null;
        }
    }
}
