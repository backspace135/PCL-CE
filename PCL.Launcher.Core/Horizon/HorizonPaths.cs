namespace PCL.Horizon;

/// <summary>
/// Horizon 数据根目录下的布局：
/// <c>versions/&lt;版本&gt;/</c> 放已安装的 Horizon 构建，<c>prepared/&lt;版本&gt;/</c> 放准备好的资源，
/// <c>instances/&lt;实例&gt;/</c> 放实例，<c>minecraft/</c> 放准备资源时读取的原版文件。
/// </summary>
public sealed class HorizonPaths
{
    public HorizonPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    /// <summary>已安装构建的父目录，每个构建一个子目录。</summary>
    public string VersionsDir => Path.Combine(Root, "versions");

    /// <summary>准备好的资源的父目录，按构建版本区分，同一构建的所有实例共用。</summary>
    public string PreparedDir => Path.Combine(Root, "prepared");

    public string InstancesDir => Path.Combine(Root, "instances");

    /// <summary>
    /// 启动器为准备资源下载的原版文件，布局与官方启动器的游戏目录一致，可直接作为 <c>--minecraft-dir</c>。
    /// </summary>
    public string MinecraftDir => Path.Combine(Root, "minecraft");

    public string BuildDir(string version) => Path.Combine(VersionsDir, CheckVersion(version));

    public string PreparedFor(string version) => Path.Combine(PreparedDir, CheckVersion(version));

    public string MinecraftVersionJson(string minecraft) =>
        Path.Combine(MinecraftDir, "versions", CheckName(minecraft), minecraft + ".json");

    public string MinecraftClientJar(string minecraft) =>
        Path.Combine(MinecraftDir, "versions", CheckName(minecraft), minecraft + ".jar");

    public string MinecraftLibrariesDir => Path.Combine(MinecraftDir, "libraries");

    public string MinecraftAssetsDir => Path.Combine(MinecraftDir, "assets");

    /// <summary>服务端 jar 不属于官方启动器的布局，单独存放。</summary>
    public string MinecraftServerJar(string minecraft) =>
        Path.Combine(MinecraftDir, "server", CheckName(minecraft), "server.jar");

    private static string CheckVersion(string version)
    {
        if (!HorizonReleaseSigning.IsBuild(version))
            throw new ArgumentException($"“{version}”不是 Horizon 构建版本。", nameof(version));
        return version;
    }

    private static string CheckName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            throw new ArgumentException($"“{name}”不能用作目录名。", nameof(name));
        return name;
    }
}
