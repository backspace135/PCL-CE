using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCL.Horizon;

/// <summary>存档 <c>datapacks/</c> 中的一个数据包：<c>.zip</c> 或含 <c>pack.mcmeta</c> 的文件夹。</summary>
public sealed record HorizonDatapack(string Name, string Path, bool IsFolder, bool Enabled, long Size, DateTime Modified)
{
    /// <summary><c>datapacks.json</c> 中引用它的名称：<c>file/&lt;名称&gt;</c>。</summary>
    public string OptionId => HorizonDatapacks.FilePrefix + Name;
}

/// <summary>
/// 管理存档的数据包。开关写在 <c>saves/&lt;存档&gt;/datapacks.json</c>：<c>{"enabled": […], "disabled": […]}</c>，
/// 不在任一列表中的数据包视为启用。只在游戏未运行时修改。
/// </summary>
public static class HorizonDatapacks
{
    public const string FilePrefix = "file/";
    public const string StateFileName = "datapacks.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

    public static string DatapacksDir(string world) => System.IO.Path.Combine(world, "datapacks");

    /// <summary>列出存档 <paramref name="world"/>（存档文件夹的完整路径）中的数据包。</summary>
    public static IReadOnlyList<HorizonDatapack> List(string world)
    {
        var dir = DatapacksDir(world);
        if (!Directory.Exists(dir)) return [];
        var (_, disabled) = ReadState(world);
        var packs = new List<HorizonDatapack>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.zip"))
        {
            var info = new FileInfo(file);
            packs.Add(new HorizonDatapack(info.Name, file, false, !disabled.Contains(FilePrefix + info.Name),
                info.Length, info.LastWriteTime));
        }
        foreach (var folder in Directory.EnumerateDirectories(dir))
        {
            if (!File.Exists(System.IO.Path.Combine(folder, "pack.mcmeta"))) continue;
            var info = new DirectoryInfo(folder);
            packs.Add(new HorizonDatapack(info.Name, folder, true, !disabled.Contains(FilePrefix + info.Name), -1,
                info.LastWriteTime));
        }
        return packs.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static void SetEnabled(string world, HorizonDatapack pack, bool enabled)
    {
        var (enabledList, disabledList) = ReadState(world);
        enabledList.Remove(pack.OptionId);
        disabledList.Remove(pack.OptionId);
        (enabled ? enabledList : disabledList).Add(pack.OptionId);
        WriteState(world, enabledList, disabledList);
    }

    public static void Delete(string world, HorizonDatapack pack)
    {
        if (pack.IsFolder) Directory.Delete(pack.Path, true);
        else File.Delete(pack.Path);
        var (enabledList, disabledList) = ReadState(world);
        if (enabledList.Remove(pack.OptionId) | disabledList.Remove(pack.OptionId))
            WriteState(world, enabledList, disabledList);
    }

    public static (List<string> Enabled, List<string> Disabled) ReadState(string world)
    {
        var path = System.IO.Path.Combine(world, StateFileName);
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject state)
                return (_Strings(state["enabled"]), _Strings(state["disabled"]));
        }
        catch (JsonException)
        {
            // 损坏的文件按空处理，下次写入时覆盖。
        }
        return ([], []);
    }

    private static void WriteState(string world, List<string> enabled, List<string> disabled)
    {
        var state = new JsonObject
        {
            ["enabled"] = new JsonArray(enabled.Select(id => (JsonNode)id).ToArray()),
            ["disabled"] = new JsonArray(disabled.Select(id => (JsonNode)id).ToArray()),
        };
        File.WriteAllText(System.IO.Path.Combine(world, StateFileName), state.ToJsonString(JsonOptions));
    }

    private static List<string> _Strings(JsonNode? node) =>
        node is JsonArray array ? array.Select(n => n?.GetValue<string>()).OfType<string>().ToList() : [];
}
