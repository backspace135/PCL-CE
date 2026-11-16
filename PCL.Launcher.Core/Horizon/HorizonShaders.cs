namespace PCL.Horizon;

/// <summary>
/// 实例 <c>shaderpacks/</c> 中的一个光影：Horizon 的 <c>.hshader</c> 或含 <c>horizon-shaders.toml</c> 的文件夹，
/// 或 <c>shaders/</c> 格式的 <c>.zip</c>、文件夹。
/// </summary>
public sealed record HorizonShaderPack(string Name, string Path, bool IsFolder, bool IsHorizonPack, bool Selected,
    long Size, DateTime Modified);

/// <summary>
/// 管理实例的光影。选中的光影写在 <c>horizon-graphics.txt</c> 的 <c>pickedShaderPack:&lt;文件或文件夹名&gt;</c>，
/// 为空表示不使用光影。只在游戏未运行时修改。
/// </summary>
public static class HorizonShaders
{
    private const string OptionKey = "pickedShaderPack:";

    public static IReadOnlyList<HorizonShaderPack> List(HorizonInstance instance)
    {
        var dir = instance.ShaderPacksDir;
        if (!Directory.Exists(dir)) return [];
        var picked = GetSelected(instance);
        var packs = new List<HorizonShaderPack>();
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (extension is not (".hshader" or ".zip")) continue;
            var info = new FileInfo(file);
            packs.Add(new HorizonShaderPack(info.Name, file, false, extension == ".hshader", info.Name == picked,
                info.Length, info.LastWriteTime));
        }
        foreach (var folder in Directory.EnumerateDirectories(dir))
        {
            var isHorizon = File.Exists(System.IO.Path.Combine(folder, "horizon-shaders.toml"));
            if (!isHorizon && !Directory.Exists(System.IO.Path.Combine(folder, "shaders"))) continue;
            var info = new DirectoryInfo(folder);
            packs.Add(new HorizonShaderPack(info.Name, folder, true, isHorizon, info.Name == picked, -1,
                info.LastWriteTime));
        }
        return packs
            .OrderByDescending(p => p.Selected)
            .ThenByDescending(p => p.IsHorizonPack)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>当前选中的光影名称，没有时为 <see langword="null"/>。</summary>
    public static string? GetSelected(HorizonInstance instance)
    {
        if (!File.Exists(instance.GraphicsOptionsPath)) return null;
        foreach (var line in File.ReadAllLines(instance.GraphicsOptionsPath))
            if (line.StartsWith(OptionKey, StringComparison.Ordinal))
                return line[OptionKey.Length..].Trim() is { Length: > 0 } name ? name : null;
        return null;
    }

    /// <summary>选中光影；<paramref name="name"/> 为 <see langword="null"/> 时不使用光影。</summary>
    public static void Select(HorizonInstance instance, string? name)
    {
        var lines = File.Exists(instance.GraphicsOptionsPath)
            ? File.ReadAllLines(instance.GraphicsOptionsPath).ToList()
            : [];
        var value = OptionKey + (name ?? "");
        var index = lines.FindIndex(l => l.StartsWith(OptionKey, StringComparison.Ordinal));
        if (index >= 0) lines[index] = value;
        else lines.Add(value);
        Directory.CreateDirectory(instance.Directory);
        File.WriteAllLines(instance.GraphicsOptionsPath, lines);
    }

    /// <summary>删除光影；删除的是选中的光影时同时取消选中。</summary>
    public static void Delete(HorizonInstance instance, HorizonShaderPack pack)
    {
        if (pack.IsFolder) Directory.Delete(pack.Path, true);
        else File.Delete(pack.Path);
        if (pack.Selected) Select(instance, null);
    }
}
