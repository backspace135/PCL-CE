using PCL.Core.App;
using PCL.Core.IO.Net;
using PCL.Horizon;

namespace PCL;

/// <summary>
///     启动器中 Horizon 的全局状态：数据目录、已安装构建、实例列表与当前选中的实例。
/// </summary>
public static class HorizonEnvironment
{
    private static readonly Lock InstancesLock = new();
    private static IReadOnlyList<HorizonInstance>? _instances;

    /// <summary>
    ///     数据目录。设置为空时使用启动器所在目录下的 <c>.horizon</c>。
    /// </summary>
    public static HorizonPaths Paths =>
        new(string.IsNullOrWhiteSpace(Config.Horizon.Root)
            ? Path.Combine(LauncherPaths.ExecutableDirectory, ".horizon")
            : Config.Horizon.Root);

    /// <summary>
    ///     版本列表与安装服务，使用启动器的网络设置（代理、DoH）。
    /// </summary>
    public static HorizonReleaseService ReleaseService =>
        new(NetworkService.GetClient());

    /// <summary>
    ///     已安装的构建，最新的在前。
    /// </summary>
    public static IReadOnlyList<HorizonBuild> InstalledBuilds() =>
        HorizonReleaseService.ListInstalled(Paths);

    /// <summary>
    ///     实例列表发生变化（新建、删除、改名、重新扫描）时触发。
    /// </summary>
    public static event Action? InstancesChanged;

    /// <summary>
    ///     选中的实例发生变化时触发。
    /// </summary>
    public static event Action? SelectedInstanceChanged;

    /// <summary>
    ///     所有实例，按最近游玩排序。首次访问时扫描数据目录。
    /// </summary>
    public static IReadOnlyList<HorizonInstance> Instances
    {
        get
        {
            lock (InstancesLock)
            {
                return _instances ??= HorizonInstance.List(Paths);
            }
        }
    }

    /// <summary>
    ///     重新扫描实例目录。
    /// </summary>
    public static void ReloadInstances()
    {
        lock (InstancesLock)
        {
            _instances = HorizonInstance.List(Paths);
        }

        InstancesChanged?.Invoke();
        SelectedInstanceChanged?.Invoke();
    }

    /// <summary>
    ///     当前选中的实例。记录的实例不存在时回退到最近游玩的实例，没有实例时为 <see langword="null" />。
    /// </summary>
    public static HorizonInstance? SelectedInstance
    {
        get
        {
            var instances = Instances;
            var id = States.Game.SelectedInstance;
            return instances.FirstOrDefault(i => i.Id == id) ?? instances.FirstOrDefault();
        }
    }

    /// <summary>
    ///     选中实例并记住。
    /// </summary>
    public static void SelectInstance(HorizonInstance instance)
    {
        States.Game.SelectedInstance = instance.Id;
        LauncherCommon.Log($"[Horizon] 选择实例：{instance.Name}（{instance.Id}）");
        SelectedInstanceChanged?.Invoke();
    }

    /// <summary>
    ///     新建实例并选中它。
    /// </summary>
    public static HorizonInstance CreateInstance(string name, string? buildVersion = null)
    {
        var instance = HorizonInstance.Create(Paths, name, buildVersion);
        LauncherCommon.Log($"[Horizon] 新建实例：{instance.Name}（{instance.Directory}）");
        ReloadInstances();
        SelectInstance(instance);
        return instance;
    }

    /// <summary>
    ///     删除实例目录（含存档），调用方需事先确认。
    /// </summary>
    public static void DeleteInstance(HorizonInstance instance)
    {
        instance.Delete();
        LauncherCommon.Log($"[Horizon] 已删除实例：{instance.Name}（{instance.Directory}）");
        ReloadInstances();
    }

    /// <summary>
    ///     实例设置已修改（改名、窗口大小、固定构建）后调用，以刷新界面。
    /// </summary>
    public static void NotifyInstanceChanged(HorizonInstance instance)
    {
        instance.Save();
        InstancesChanged?.Invoke();
        SelectedInstanceChanged?.Invoke();
    }
}
