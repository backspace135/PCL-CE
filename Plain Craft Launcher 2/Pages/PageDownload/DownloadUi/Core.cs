using PCL.Core.App.Localization;
using PCL.Core.UI.Hints;

namespace PCL;

public static partial class DownloadUi
{
    /// <summary>
    ///     在加载器状态改变后显示一条提示。
    ///     不会进行任何其他操作。
    /// </summary>
    public static void LoaderStateChangedHintOnly(object loaderObj)
    {
        var loader = (Loading.LoaderBase)loaderObj;
        switch (loader.State)
        {
            case LoadState.Finished:
                HintService.Show(Hints.Success($"{loader.name}{Lang.Text("Common.Status.Success")}"));
                break;
            case LoadState.Failed:
                HintService.Show(Hints.Error($"{loader.name}{Lang.Text("Common.Status.Failure")}{loader.Error.Message}"));
                break;
            case LoadState.Aborted:
                HintService.Show(Hints.Info($"{loader.name}{Lang.Text("Common.Status.Cancelled")}"));
                break;
            case LoadState.Waiting:
            case LoadState.Loading:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
