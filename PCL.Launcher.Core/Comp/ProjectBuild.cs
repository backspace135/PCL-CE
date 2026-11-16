using PCL.Core.Utils;

namespace PCL;

public static partial class Comp
{
    public partial class CompProject
    {
        private static readonly Dictionary<string, CompLoaderType> ModrinthLoaderTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["forge"] = CompLoaderType.Forge,
                ["fabric"] = CompLoaderType.Fabric,
                ["quilt"] = CompLoaderType.Quilt,
                ["neoforge"] = CompLoaderType.NeoForge
            };

        private static readonly Dictionary<string, string> ResourcePackHiddenCategoryLangKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["decoration"] = "Download.Comp.Category.Decoration",
                ["mobs"] = "Download.Comp.Category.Mobs",
                ["equipment"] = "Download.Comp.Category.Equipment"
            };

        private static readonly Dictionary<string, string> ModrinthCategoryLangKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["technology"] = "Download.Comp.Category.Technology",
                ["magic"] = "Download.Comp.Category.Magic",
                ["adventure"] = "Download.Comp.Category.Adventure",
                ["utility"] = "Download.Comp.Category.Utility",
                ["optimization"] = "Download.Comp.Category.Optimization",
                ["vanilla-like"] = "Download.Comp.Category.VanillaLike",
                ["realistic"] = "Download.Comp.Category.Realistic",

                ["worldgen"] = "Download.Comp.Category.WorldGen",
                ["food"] = "Download.Comp.Category.FoodCooking",
                ["game-mechanics"] = "Download.Comp.Category.GameMechanics",
                ["transportation"] = "Download.Comp.Category.Transportation",
                ["storage"] = "Download.Comp.Category.Storage",
                ["social"] = "Download.Comp.Category.Server",
                ["library"] = "Download.Comp.Category.Library",

                ["multiplayer"] = "Download.Comp.Category.Multiplayer",
                ["challenging"] = "Download.Comp.Category.Modpack.Hardcore",
                ["combat"] = "Download.Comp.Category.Combat",
                ["quests"] = "Download.Comp.Category.Modpack.Quests",
                ["kitchen-sink"] = "Download.Comp.Category.Modpack.KitchenSink",
                ["lightweight"] = "Download.Comp.Category.Modpack.SmallLight",

                ["simplistic"] = "Download.Comp.Category.Simplistic",
                ["tweaks"] = "Download.Comp.Category.Tweaks",
                ["8x-"] = "Download.Comp.Category.ResourcePack.Resolution8xOrLower",
                ["16x"] = "Download.Comp.Category.ResourcePack.Resolution16x",
                ["32x"] = "Download.Comp.Category.ResourcePack.Resolution32x",
                ["48x"] = "Download.Comp.Category.ResourcePack.Resolution48x",
                ["64x"] = "Download.Comp.Category.ResourcePack.Resolution64x",
                ["128x"] = "Download.Comp.Category.ResourcePack.Resolution128x",
                ["256x"] = "Download.Comp.Category.ResourcePack.Resolution256x",
                ["512x+"] = "Download.Comp.Category.ResourcePack.Resolution512xOrHigher",
                ["audio"] = "Download.Comp.Category.Audio",
                ["fonts"] = "Download.Comp.Category.Fonts",
                ["models"] = "Download.Comp.Category.Models",
                ["gui"] = "Download.Comp.Category.Gui",
                ["locale"] = "Download.Comp.Category.Locale",
                ["core-shaders"] = "Download.Comp.Category.CoreShaders",
                ["modded"] = "Download.Comp.Category.ModSupport",

                ["fantasy"] = "Download.Comp.Category.Fantasy",
                ["semi-realistic"] = "Download.Comp.Category.SemiRealistic",
                ["cartoon"] = "Download.Comp.Category.Cartoon",
                ["colored-lighting"] = "Download.Comp.Category.ColoredLighting",
                ["path-tracing"] = "Download.Comp.Category.PathTracing",
                ["pbr"] = "Download.Comp.Category.Pbr",
                ["reflections"] = "Download.Comp.Category.Reflections",
                ["iris"] = "Download.Comp.Category.Iris",
                ["optifine"] = "Download.Comp.Category.Optifine",
                ["vanilla"] = "Download.Comp.Filter.Loader.VanillaAvailable"
            };

        private static CompProjectBuildResult _BuildFromCompJson(JsonObject data)
        {
            var result = new CompProjectBuildResult
            {
                Type = (CompType)(data["Type"]?.ToObject<int>() ?? 0),
                Slug = (string?)data["Slug"] ?? "",
                Id = (string?)data["Id"] ?? "",
                RawName = (string?)data["RawName"] ?? "",
                Description = (string?)data["Description"] ?? "",
                Website = (string?)data["Website"] ?? "",
                DownloadCount = data["DownloadCount"]?.ToObject<int>() ?? 0,
                Tags =
                [
                    .. (data["Tags"] as JsonArray ?? [])
                    .Select(t => t?.ToString() ?? "")
                ]
            };

            if (data.TryGetPropertyValue("LastUpdate", out var last))
                result.LastUpdate = last?.ToObject<DateTime>();

            if (data.TryGetPropertyValue("ModLoaders", out var loaders))
                result.ModLoaders =
                [
                    .. (loaders as JsonArray ?? [])
                    .Select(t => (CompLoaderType)(t?.ToObject<int>() ?? 0))
                ];

            if (data.TryGetPropertyValue("LogoUrl", out var url))
                result.LogoUrl = (string?)url;

            if (data.TryGetPropertyValue("Drops", out var drops))
                result.Drops =
                [
                    .. (drops as JsonArray ?? [])
                    .Select(t => t?.ToObject<int>() ?? 0)
                ];

            return result;
        }

        private static CompProjectBuildResult _BuildFromModrinth(JsonObject data)
        {
            var projectType = data["project_type"]?.ToString() ?? "";
            var slug = (string?)data["slug"] ?? "";

            var result = new CompProjectBuildResult
            {
                Id = (string?)(data["project_id"] ?? data["id"]) ?? "",
                Slug = slug,
                RawName = (string?)data["title"] ?? "",
                Description = (string?)data["description"] ?? "",
                LastUpdate = data["date_modified"]?.ToObject<DateTime>(),
                DownloadCount = data["downloads"]?.ToObject<int>() ?? 0,
                LogoUrl = (string?)data["icon_url"],
                Website = $"https://modrinth.com/{projectType}/{slug}",

                Type = projectType switch
                {
                    "modpack" => CompType.ModPack,
                    "resourcepack" => CompType.ResourcePack,
                    "shader" => CompType.Shader,
                    _ => CompType.Mod
                }
            };

            if (string.IsNullOrEmpty(result.LogoUrl))
                result.LogoUrl = null;

            result.Drops =
            [
                .. ((data["game_versions"] ?? data["versions"]) as JsonArray ?? [])
                .Select(v => McInstanceInfo.VersionToDrop((string?)v ?? ""))
                .Where(v => v > 0)
                .Distinct()
                .OrderByDescending(v => v)
            ];

            foreach (var category in (data["loaders"] as JsonArray)
                     ?.Select(t => t?.ToString())
                     ?? [])
                if (ModrinthLoaderTypes.TryGetValue(category ?? "", out var loader))
                    result.ModLoaders.Add(loader);

            foreach (var category in (data["categories"] as JsonArray)
                     ?.Select(t => t?.ToString())
                     ?? [])
            {
                if (string.IsNullOrEmpty(category)) continue;
                if (ModrinthLoaderTypes.TryGetValue(category, out var loader))
                {
                    result.ModLoaders.Add(loader);
                }
                else
                {
                    if (category.Equals("datapack", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Type = CompType.DataPack;
                    }
                    else
                    {
                        if (ResourcePackHiddenCategoryLangKeys.TryGetValue(
                                category,
                                out var hiddenLangKey))
                        {
                            if (result.Type != CompType.ResourcePack)
                                _AddTag(result.Tags, hiddenLangKey);
                        }
                        else
                        {
                            if (ModrinthCategoryLangKeys.TryGetValue(category, out var langKey))
                                _AddTag(result.Tags, langKey);
                        }
                    }
                }
            }

            return result;
        }

        private static void _AddTag(List<string> tags, string langKey)
        {
            if (string.IsNullOrWhiteSpace(langKey))
                return;

            if (!tags.Contains(langKey))
                tags.Add(langKey);
        }

        private sealed class CompProjectBuildResult
        {
            public string Description = "";
            public int DownloadCount;
            public List<int> Drops = [];
            public string Id = "";
            public DateTime? LastUpdate;
            public string? LogoUrl;
            public List<CompLoaderType> ModLoaders = [];
            public string RawName = "";
            public string Slug = "";
            public List<string> Tags = [];
            public CompType Type;
            public string Website = "";
        }
    }
}