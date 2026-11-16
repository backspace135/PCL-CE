using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCL.Horizon;

/// <summary>实例 <c>resourcepacks/</c> 中的一个资源包：<c>.zip</c> 或含 <c>pack.mcmeta</c> 的文件夹。</summary>
public sealed record HorizonResourcePack(string Name, string Path, bool IsFolder, bool Enabled, long Size, DateTime Modified)
{
    /// <summary><c>options.txt</c> 中引用它的名称：<c>file/&lt;名称&gt;</c>。</summary>
    public string OptionId => HorizonResourcePacks.FilePrefix + Name;
}

/// <summary>
/// 管理实例的资源包。开关写在 <c>options.txt</c> 的 <c>resourcePacks:[…]</c> 中，顺序为优先级从低到高，
/// 与原版一致；只在游戏未运行时修改，游戏启动时读取。
/// </summary>
public static class HorizonResourcePacks
{
    public const string FilePrefix = "file/";
    private const string OptionKey = "resourcePacks";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>列出资源包，已启用的在前（按优先级从高到低），其余按名称排序。</summary>
    public static IReadOnlyList<HorizonResourcePack> List(HorizonInstance instance)
    {
        var dir = instance.ResourcePacksDir;
        if (!Directory.Exists(dir)) return [];
        var enabled = ReadEnabled(instance);
        var packs = new List<HorizonResourcePack>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.zip"))
        {
            var info = new FileInfo(file);
            packs.Add(new HorizonResourcePack(info.Name, file, false, enabled.Contains(FilePrefix + info.Name),
                info.Length, info.LastWriteTime));
        }
        foreach (var folder in Directory.EnumerateDirectories(dir))
        {
            if (!File.Exists(System.IO.Path.Combine(folder, "pack.mcmeta"))) continue;
            var info = new DirectoryInfo(folder);
            packs.Add(new HorizonResourcePack(info.Name, folder, true, enabled.Contains(FilePrefix + info.Name), -1,
                info.LastWriteTime));
        }
        return packs
            .OrderByDescending(p => p.Enabled ? enabled.IndexOf(p.OptionId) : -1)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>启用或停用资源包。启用的资源包放在最高优先级。</summary>
    public static void SetEnabled(HorizonInstance instance, HorizonResourcePack pack, bool enabled)
    {
        var list = ReadEnabled(instance);
        list.Remove(pack.OptionId);
        if (enabled) list.Add(pack.OptionId);
        if (!list.Contains("vanilla")) list.Insert(0, "vanilla");
        WriteEnabled(instance, list);
    }

    /// <summary>删除资源包文件或文件夹，并从 <c>options.txt</c> 中移除。</summary>
    public static void Delete(HorizonInstance instance, HorizonResourcePack pack)
    {
        if (pack.IsFolder) Directory.Delete(pack.Path, true);
        else File.Delete(pack.Path);
        var list = ReadEnabled(instance);
        if (list.Remove(pack.OptionId)) WriteEnabled(instance, list);
    }

    /// <summary><c>options.txt</c> 中 <c>resourcePacks</c> 的值，按优先级从低到高。</summary>
    public static List<string> ReadEnabled(HorizonInstance instance)
    {
        foreach (var line in _ReadLines(instance.OptionsPath))
        {
            if (!line.StartsWith(OptionKey + ":", StringComparison.Ordinal)) continue;
            try
            {
                return JsonNode.Parse(line[(OptionKey.Length + 1)..]) is JsonArray array
                    ? array.Select(n => n?.GetValue<string>()).OfType<string>().ToList()
                    : [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
        return [];
    }

    private static void WriteEnabled(HorizonInstance instance, List<string> list)
    {
        var value = $"{OptionKey}:{new JsonArray(list.Select(id => (JsonNode)id).ToArray()).ToJsonString(JsonOptions)}";
        var lines = _ReadLines(instance.OptionsPath).ToList();
        var index = lines.FindIndex(l => l.StartsWith(OptionKey + ":", StringComparison.Ordinal));
        if (index >= 0) lines[index] = value;
        else lines.Add(value);
        Directory.CreateDirectory(instance.Directory);
        File.WriteAllLines(instance.OptionsPath, lines);
    }

    private static IEnumerable<string> _ReadLines(string path) =>
        File.Exists(path) ? File.ReadAllLines(path) : [];
}
