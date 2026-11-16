namespace PCL;

/// <summary>依赖 Comp 文件列表的下载加载器，保留在 UI 程序集以避免下载业务依赖 Comp。</summary>
public static class CompDownloadLoaders
{
    /// <summary>Fabric API 列表，官方源。</summary>
    public static readonly Loading.LoaderTask<int, List<Comp.CompFile>> DlFabricApiLoader = new(
        "Fabric API List Loader",
        task => task.output = Comp.CompFilesGet("fabric-api"));

    /// <summary>OptiFabric 列表。OptiFabric 只发布在 CurseForge 上，已不再获取；随模组加载器安装一同删除。</summary>
    public static readonly Loading.LoaderTask<int, List<Comp.CompFile>> DlOptiFabricLoader = new(
        "OptiFabric List Loader",
        task => task.output = []);

    /// <summary>Legacy Fabric API 列表，官方源。</summary>
    public static readonly Loading.LoaderTask<int, List<Comp.CompFile>> DlLegacyFabricApiLoader =
        new(
            "Legacy Fabric API List Loader",
            task => task.output = Comp.CompFilesGet("legacy-fabric-api"));
}