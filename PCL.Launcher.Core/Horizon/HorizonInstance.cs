using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCL.Horizon;

/// <summary>
/// 一个 Horizon 实例：<c>instances/&lt;文件夹&gt;/</c>，同时作为 <c>--game-dir</c> 和 <c>--home</c>，
/// 设置保存在其中的 <c>horizon-instance.json</c>。文件夹名创建后不变，改名只改显示名称。
/// </summary>
public sealed class HorizonInstance
{
    public const string ConfigFileName = "horizon-instance.json";

    private HorizonInstance(string directory) => Directory = Path.GetFullPath(directory);

    public string Directory { get; }

    /// <summary>文件夹名，实例的稳定标识。</summary>
    public string Id => Path.GetFileName(Directory);

    public string Name { get; set; } = "";

    /// <summary>固定使用的构建版本；<see langword="null"/> 表示使用已安装的最新构建。</summary>
    public string? BuildVersion { get; set; }

    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool Fullscreen { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }

    public string ConfigPath => Path.Combine(Directory, ConfigFileName);
    public string SavesDir => Path.Combine(Directory, "saves");
    public string ResourcePacksDir => Path.Combine(Directory, "resourcepacks");
    public string ShaderPacksDir => Path.Combine(Directory, "shaderpacks");
    public string ModsDir => Path.Combine(Directory, "horizon-mods");

    /// <summary>模组开关：<c>mod.&lt;id&gt;:true</c>。</summary>
    public string ModsListPath => Path.Combine(Directory, "horizon-mods.txt");

    /// <summary>图形设置，其中 <c>pickedShaderPack:</c> 为选中的光影。</summary>
    public string GraphicsOptionsPath => Path.Combine(Directory, "horizon-graphics.txt");

    public string ScreenshotsDir => Path.Combine(Directory, "screenshots");
    public string LogsDir => Path.Combine(Directory, "logs");
    public string LatestLogPath => Path.Combine(LogsDir, "latest.log");
    public string CrashReportsDir => Path.Combine(Directory, "crash-reports");
    public string OptionsPath => Path.Combine(Directory, "options.txt");
    public string ServersDatPath => Path.Combine(Directory, "servers.dat");

    /// <summary>某个存档的数据包目录。</summary>
    public string DatapacksDir(string world) => Path.Combine(SavesDir, world, "datapacks");

    /// <summary>
    /// 本实例使用的构建：固定版本已安装时返回它，未固定时返回最新的已安装构建；找不到时返回 <see langword="null"/>。
    /// </summary>
    public HorizonBuild? ResolveBuild(IReadOnlyList<HorizonBuild> installed) =>
        BuildVersion is null
            ? installed.FirstOrDefault(b => b.IsComplete)
            : installed.FirstOrDefault(b => b.Version == BuildVersion && b.IsComplete);

    /// <summary>所有含 <c>horizon-instance.json</c> 的实例，按最近游玩、再按创建时间排序。损坏的配置会被跳过。</summary>
    public static IReadOnlyList<HorizonInstance> List(HorizonPaths paths)
    {
        if (!System.IO.Directory.Exists(paths.InstancesDir)) return [];
        var result = new List<HorizonInstance>();
        foreach (var dir in System.IO.Directory.EnumerateDirectories(paths.InstancesDir))
        {
            if (Path.GetFileName(dir).StartsWith('.')) continue;
            var instance = TryLoad(dir);
            if (instance is not null) result.Add(instance);
        }
        return result
            .OrderByDescending(i => i.LastPlayedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(i => i.CreatedAt)
            .ToList();
    }

    /// <summary>读取实例目录；没有或无法解析配置时返回 <see langword="null"/>。</summary>
    public static HorizonInstance? TryLoad(string directory)
    {
        var instance = new HorizonInstance(directory);
        try
        {
            if (JsonNode.Parse(File.ReadAllText(instance.ConfigPath)) is not JsonObject o) return null;
            instance.Name = (string?)o["name"] is { Length: > 0 } name ? name : instance.Id;
            instance.BuildVersion = (string?)o["build"] is { } v && HorizonReleaseSigning.IsBuild(v) ? v : null;
            instance.Width = _Int(o["width"]) is int w and > 0 ? w : 1280;
            instance.Height = _Int(o["height"]) is int h and > 0 ? h : 720;
            instance.Fullscreen = o["fullscreen"] is JsonValue f && f.TryGetValue<bool>(out var fs) && fs;
            instance.CreatedAt = _Date(o["created"]) ?? System.IO.Directory.GetCreationTime(instance.Directory);
            instance.LastPlayedAt = _Date(o["last_played"]);
            return instance;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>新建实例，文件夹名由显示名称得出，重名时追加序号。</summary>
    public static HorizonInstance Create(HorizonPaths paths, string name, string? buildVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (buildVersion is not null && !HorizonReleaseSigning.IsBuild(buildVersion))
            throw new ArgumentException($"“{buildVersion}”不是 Horizon 构建版本。", nameof(buildVersion));
        System.IO.Directory.CreateDirectory(paths.InstancesDir);
        var baseName = _FolderName(name);
        var folder = baseName;
        for (var i = 2; System.IO.Directory.Exists(Path.Combine(paths.InstancesDir, folder)); i++)
            folder = $"{baseName} ({i})";
        var instance = new HorizonInstance(Path.Combine(paths.InstancesDir, folder))
        {
            Name = name.Trim(),
            BuildVersion = buildVersion,
            CreatedAt = DateTimeOffset.Now,
        };
        System.IO.Directory.CreateDirectory(instance.Directory);
        instance.Save();
        return instance;
    }

    /// <summary>修改显示名称并保存。</summary>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Save();
    }

    /// <summary>删除整个实例目录（含存档），调用方需事先确认。</summary>
    public void Delete()
    {
        if (!System.IO.Directory.Exists(Directory)) return;
        var trash = Path.Combine(Path.GetDirectoryName(Directory)!, $".delete-{Guid.NewGuid():N}");
        System.IO.Directory.Move(Directory, trash);
        System.IO.Directory.Delete(trash, true);
    }

    /// <summary>记录一次启动时间并保存。</summary>
    public void MarkPlayed()
    {
        LastPlayedAt = DateTimeOffset.Now;
        Save();
    }

    /// <summary>写入 <c>horizon-instance.json</c>（先写临时文件再替换）。</summary>
    public void Save()
    {
        var json = new JsonObject
        {
            ["name"] = Name,
            ["build"] = BuildVersion,
            ["width"] = Width,
            ["height"] = Height,
            ["fullscreen"] = Fullscreen,
            ["created"] = CreatedAt.ToString("O"),
            ["last_played"] = LastPlayedAt?.ToString("O"),
        };
        System.IO.Directory.CreateDirectory(Directory);
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, ConfigPath, true);
    }

    public override string ToString() => Name;

    private static string _FolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var chars = name.Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray();
        var folder = new string(chars).Trim().TrimEnd('.');
        if (folder.Length > 64) folder = folder[..64].TrimEnd();
        // Windows 保留设备名。
        var stem = folder.Split('.')[0].ToUpperInvariant();
        if (folder.Length == 0 || folder.StartsWith('.') ||
            stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsAsciiDigit(stem[3])))
            folder = "instance-" + (folder.Length == 0 ? Guid.NewGuid().ToString("N")[..8] : folder.TrimStart('.'));
        return folder;
    }

    private static int? _Int(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;

    private static DateTimeOffset? _Date(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && DateTimeOffset.TryParse(s, out var d) ? d : null;
}
