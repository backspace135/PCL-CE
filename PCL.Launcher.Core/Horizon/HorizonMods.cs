using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PCL.Horizon;

/// <summary>实例 <c>horizon-mods/</c> 中的一个内容模组：<c>.hmod</c> 或含 <c>horizon-mod.toml</c> 的文件夹。</summary>
public sealed record HorizonMod(
    string Id, string Name, string? Version, string Path, bool IsFolder, bool Enabled, bool IsNative, long Size,
    DateTime Modified);

/// <summary>
/// 管理实例的模组。开关写在 <c>horizon-mods.txt</c>：<c>mod.&lt;id&gt;:true</c> 为开启，没有这一行的模组是关闭的。
/// 原生模组需要重新构建游戏才能运行，启动器只展示，不开启。只在游戏未运行时修改。
/// </summary>
public static class HorizonMods
{
    public const string ManifestName = "horizon-mod.toml";
    private const string Prefix = "mod.";

    /// <summary>列出模组，已开启的在前。</summary>
    public static IReadOnlyList<HorizonMod> List(HorizonInstance instance)
    {
        var dir = instance.ModsDir;
        if (!Directory.Exists(dir)) return [];
        var options = _ReadOptions(instance);
        var mods = new List<HorizonMod>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.hmod"))
        {
            var info = new FileInfo(file);
            var manifest = _ReadZipManifest(file);
            mods.Add(_Create(manifest, info.Name, file, false, info.Length, info.LastWriteTime, options));
        }
        foreach (var folder in Directory.EnumerateDirectories(dir))
        {
            var manifestPath = System.IO.Path.Combine(folder, ManifestName);
            if (!File.Exists(manifestPath)) continue;
            var info = new DirectoryInfo(folder);
            mods.Add(_Create(File.ReadAllText(manifestPath), info.Name, folder, true, -1, info.LastWriteTime, options));
        }
        return mods
            .OrderByDescending(m => m.Enabled)
            .ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>开启或关闭模组。</summary>
    public static void SetEnabled(HorizonInstance instance, HorizonMod mod, bool enabled) =>
        SetEnabled(instance, mod.Id, enabled);

    /// <summary>开启或关闭 id 为 <paramref name="id"/> 的模组（写入 <c>mod.&lt;id&gt;:true/false</c>）。</summary>
    public static void SetEnabled(HorizonInstance instance, string id, bool enabled)
    {
        var lines = File.Exists(instance.ModsListPath) ? File.ReadAllLines(instance.ModsListPath).ToList() : [];
        var key = Prefix + id + ":";
        var value = key + (enabled ? "true" : "false");
        var index = lines.FindIndex(l => l.StartsWith(key, StringComparison.Ordinal));
        if (index >= 0) lines[index] = value;
        else lines.Add(value);
        Directory.CreateDirectory(instance.Directory);
        File.WriteAllLines(instance.ModsListPath, lines);
    }

    /// <summary>删除模组文件或文件夹，并从 <c>horizon-mods.txt</c> 中移除它的开关。</summary>
    public static void Delete(HorizonInstance instance, HorizonMod mod)
    {
        if (mod.IsFolder) Directory.Delete(mod.Path, true);
        else File.Delete(mod.Path);
        if (!File.Exists(instance.ModsListPath)) return;
        var key = Prefix + mod.Id + ":";
        var lines = File.ReadAllLines(instance.ModsListPath).Where(l => !l.StartsWith(key, StringComparison.Ordinal));
        File.WriteAllLines(instance.ModsListPath, lines);
    }

    /// <summary>模组文件的 SHA-256（文件夹为 <see langword="null"/>），用于对照注册表的撤下列表。</summary>
    public static string? Sha256(HorizonMod mod)
    {
        if (mod.IsFolder) return null;
        using var stream = File.OpenRead(mod.Path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// 从 <c>horizon-mod.toml</c> 读取顶层的字符串键（只认 <c>key = "value"</c>，遇到第一个表头即停止）。
    /// </summary>
    public static string? ReadManifestValue(string manifest, string key)
    {
        foreach (var raw in manifest.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) break;
            var match = Regex.Match(line, "^" + Regex.Escape(key) + "\\s*=\\s*\"([^\"]*)\"");
            if (match.Success) return match.Groups[1].Value;
        }
        return null;
    }

    private static HorizonMod _Create(
        string? manifest, string fileName, string path, bool isFolder, long size, DateTime modified,
        Dictionary<string, bool> options)
    {
        var stem = isFolder ? fileName : System.IO.Path.GetFileNameWithoutExtension(fileName);
        var id = manifest is null ? stem : ReadManifestValue(manifest, "id") ?? stem;
        var name = manifest is null ? stem : ReadManifestValue(manifest, "name") ?? id;
        var version = manifest is null ? null : ReadManifestValue(manifest, "version");
        var isNative = manifest is not null && Regex.IsMatch(manifest, @"^\s*\[native\]", RegexOptions.Multiline);
        var enabled = options.TryGetValue(id, out var on) && on;
        return new HorizonMod(id, name, version, path, isFolder, enabled, isNative, size, modified);
    }

    private static string? _ReadZipManifest(string file)
    {
        try
        {
            using var zip = ZipFile.OpenRead(file);
            var entry = zip.GetEntry(ManifestName);
            if (entry is null) return null;
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return null;
        }
    }

    private static Dictionary<string, bool> _ReadOptions(HorizonInstance instance)
    {
        var options = new Dictionary<string, bool>();
        if (!File.Exists(instance.ModsListPath)) return options;
        foreach (var line in File.ReadAllLines(instance.ModsListPath))
        {
            if (!line.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            var separator = line.LastIndexOf(':');
            if (separator <= Prefix.Length) continue;
            options[line[Prefix.Length..separator]] = line[(separator + 1)..].Trim() == "true";
        }
        return options;
    }
}
