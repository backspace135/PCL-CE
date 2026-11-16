using System.Globalization;
using System.Windows.Controls;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     实例设置：使用的 Horizon 构建与游戏窗口。保存在实例的 <c>horizon-instance.json</c> 中。
/// </summary>
public partial class PageInstanceSetup : IRefreshable
{
    private bool _loading;

    public PageInstanceSetup()
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
        _loading = true;
        try
        {
            ComboBuild.Items.Clear();
            ComboBuild.Items.Add(new MyComboBoxItem { Content = Lang.Text("Horizon.Instance.Build.Latest"), Tag = null });
            var builds = HorizonEnvironment.InstalledBuilds().Select(b => b.Version).ToList();
            if (instance.BuildVersion is { } pinned && !builds.Contains(pinned))
                builds.Add(pinned);
            foreach (var version in builds)
                ComboBuild.Items.Add(new MyComboBoxItem { Content = version, Tag = version });
            ComboBuild.SelectedIndex = instance.BuildVersion is null ? 0 : builds.IndexOf(instance.BuildVersion) + 1;

            TextWidth.Text = instance.Width.ToString(CultureInfo.InvariantCulture);
            TextHeight.Text = instance.Height.ToString(CultureInfo.InvariantCulture);
            CheckFullscreen.Checked = instance.Fullscreen;
        }
        finally
        {
            _loading = false;
        }
    }

    private void ComboBuild_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ComboBuild.SelectedItem is not MyComboBoxItem item)
            return;
        var instance = PageInstanceLeft.Instance;
        instance.BuildVersion = item.Tag as string;
        HorizonEnvironment.NotifyInstanceChanged(instance);
    }

    private void WindowSize_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || !TextWidth.IsValidated || !TextHeight.IsValidated)
            return;
        if (!int.TryParse(TextWidth.Text, out var width) || !int.TryParse(TextHeight.Text, out var height))
            return;
        var instance = PageInstanceLeft.Instance;
        instance.Width = width;
        instance.Height = height;
        HorizonEnvironment.NotifyInstanceChanged(instance);
    }

    private void CheckFullscreen_Change(object sender, bool user)
    {
        if (_loading || !user)
            return;
        var instance = PageInstanceLeft.Instance;
        instance.Fullscreen = CheckFullscreen.Checked == true;
        HorizonEnvironment.NotifyInstanceChanged(instance);
    }
}
