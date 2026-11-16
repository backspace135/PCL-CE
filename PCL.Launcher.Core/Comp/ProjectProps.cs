using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Network;

namespace PCL;

public static partial class Comp
{
    public partial class CompProject
    {
        /// <summary>
        ///     中文描述。若为 Nothing 则没有。
        /// </summary>
        public Task<string?> ChineseDescription => GetChineseDescriptionAsync();

        private async Task<string?> GetChineseDescriptionAsync()
        {
            const string from = "modrinth";
            string? result = null;

            var descHash = $"{Id}{LauncherCommon.GetStringMd5(Description)}";
            var cacheFilePath = $@"{LauncherPaths.Temp}Cache\CompTranslation.ini";
            var cacheTranslation = LauncherCommon.ReadIni(cacheFilePath, descHash);
            if (!string.IsNullOrWhiteSpace(cacheTranslation))
            {
                result = LauncherCommon.Base64Decode(cacheTranslation);
                return result;
            }

            try
            {
                var jsonObject = (JsonObject)await
                    Requester.FetchJsonAsync($"https://mod.mcimirror.top/translate/{from}/{Id}");
                if (jsonObject.ContainsKey("translated"))
                {
                    result = jsonObject["translated"]?.ToString() ?? string.Empty;
                    LauncherCommon.WriteIni(
                        cacheFilePath,
                        descHash,
                        LauncherCommon.Base64Encode(result));
                }
            }
            catch (HttpRequestException ex)
            {
                if (ex.Message.Contains("404"))
                {
                    LauncherUi.Interaction?.ShowMessageBox(
                        Lang.Text("Download.Comp.Detail.DescriptionNoTranslation"),
                        Lang.Text("Download.Comp.Detail.DescriptionTranslationFailed"),
                        Lang.Text("Download.Comp.Detail.KnownButton"));
                    return null;
                }

                LauncherCommon.Log(
                    ex,
                    "获取中文描述时出现错误",
                    LauncherLogLevel.Hint,
                    userSummary: Lang.Text("Minecraft.Comp.Error.OperationFailed"));
            }
            catch (Exception ex)
            {
                LauncherCommon.Log(
                    ex,
                    "获取中文描述时出现错误",
                    LauncherLogLevel.Hint,
                    userSummary: Lang.Text("Minecraft.Comp.Error.OperationFailed"));
            }

            return result;
        }

        /// <summary>
        ///     将当前实例转为可用于保存缓存的 Json。
        /// </summary>
        public JsonObject ToJson()
        {
            var json = new JsonObject
            {
                ["DataSource"] = "Modrinth",
                ["Type"] = (int)Type,
                ["Slug"] = Slug,
                ["Id"] = Id
            };

            json["RawName"] = RawName;
            json["Description"] = Description;
            json["Website"] = Website;

            if (LastUpdate is not null)
                json["LastUpdate"] = LastUpdate;

            json["DownloadCount"] = DownloadCount;

            if (ModLoaders is not null && ModLoaders.Count != 0)
                json["ModLoaders"] =
                    new JsonArray(ModLoaders.Select(m => (JsonNode)(int)m).ToArray());

            json["Tags"] = new JsonArray(Tags.Select(s => (JsonNode)s).ToArray());

            if (LogoUrl is not null)
                json["LogoUrl"] = LogoUrl;

            if (Drops.Count != 0)
                json["Drops"] = new JsonArray(Drops.Select(i => (JsonNode)i).ToArray());

            json["CacheTime"] = DateTime.Now;

            return json;
        }

        /// <summary>
        ///     按当前界面语言生成卡片下边栏的版本描述文本。
        /// </summary>
        /// <param name="showMcVersionDesc">是否显示 Minecraft 版本范围（也会显示加载器全名）。</param>
        /// <param name="showLoaderDesc">是否显示加载器描述。</param>
        /// <returns>应当显示的版本描述；两者都为 <see langword="false" /> 时返回空字符串。</returns>
        public string BuildVersionDescription(bool showMcVersionDesc, bool showLoaderDesc)
        {
            var gameVersionDescription = _BuildGameVersionDescription();

            var modLoadersForDesc = ModLoaders.ToList();
            if (Config.Download.Comp.IgnoreQuilt) modLoadersForDesc.Remove(CompLoaderType.Quilt);

            var (fullDesc, partDesc) = modLoadersForDesc.Count switch
            {
                0 => ModLoaders.Count == 1
                    ? (
                        Lang.Text("Download.Comp.Type.Only", ModLoaders.Single().ToString()),
                        ModLoaders.Single().ToString())
                    : (
                        Lang.Text("Download.Comp.Type.Unknown"),
                        ""),
                1 => (
                    Lang.Text("Download.Comp.Type.Only", modLoadersForDesc.Single().ToString()),
                    modLoadersForDesc.Single().ToString()),
                _ => _GetMultiLoaderDesc(modLoadersForDesc)
            };

            return (showMcVersionDesc, showLoaderDesc) switch
            {
                (true, true) =>
                    $"{(string.IsNullOrEmpty(partDesc) ? "" : $"{partDesc} ")}{gameVersionDescription}",
                (true, false) => gameVersionDescription,
                (false, true) => fullDesc,
                _ => ""
            };
        }

        /// <summary>
        ///     将当前工程信息实例化为控件。
        /// </summary>
        /// <param name="showQuickDownload">是否在卡片右侧显示快速下载按钮（仅搜索结果页应传 true）。</param>
        /// <exception cref="ArgumentNullException"></exception>
        private string _BuildGameVersionDescription()
        {
            if (Drops is null || Drops.Count == 0)
                return Lang.Text("Download.Comp.Detail.CompItem.SnapshotOnly");

            var segments = new List<string>();
            var isOld = false;

            for (var i = 0; i < Drops.Count; i++)
            {
                var startDrop = Drops[i];
                var endDrop = Drops[i];

                if (startDrop < 100)
                {
                    if (segments.Count != 0 && !isOld) break;
                    isOld = true;
                }

                for (var ii = i + 1; ii < Drops.Count; ii++)
                {
                    if (GameDownload.AllDrops is null
                        || GameDownload.AllDrops.IndexOf(Drops[ii])
                        != GameDownload.AllDrops.IndexOf(endDrop) + 1) break;
                    endDrop = Drops[ii];
                    i = ii;
                }

                var startName = McInstanceInfo.DropToVersion(startDrop);
                var endName = McInstanceInfo.DropToVersion(endDrop);

                if (startDrop == endDrop)
                {
                    segments.Add(startName);
                }
                else if (GameDownload.AllDrops?.Any() == true
                         && startDrop >= GameDownload.AllDrops.First())
                {
                    if (endDrop < 100)
                    {
                        segments.Clear();
                        segments.Add(Lang.Text("Download.Comp.Detail.CompItem.AllVersions"));
                        break;
                    }

                    segments.Add($"{endName}+");
                }
                else if (endDrop < 100)
                {
                    segments.Add($"{startName}-");
                    break;
                }
                else if (GameDownload.AllDrops is null
                         || GameDownload.AllDrops.IndexOf(endDrop)
                         - GameDownload.AllDrops.IndexOf(startDrop)
                         == 1)
                {
                    segments.Add($"{startName}, {endName}");
                }
                else
                {
                    segments.Add($"{startName}~{endName}");
                }
            }

            return string.Join(", ", segments);
        }

        private (string, string) _GetMultiLoaderDesc(List<CompLoaderType> modLoadersForDesc)
        {
            var newestDrop = Drops?.FirstOrDefault() ?? 9999;
            var isAny = ModLoaders.Contains(CompLoaderType.Forge)
                && (newestDrop < 140 || ModLoaders.Contains(CompLoaderType.Fabric))
                && (newestDrop < 200 || ModLoaders.Contains(CompLoaderType.NeoForge))
                && (newestDrop < 140
                    || ModLoaders.Contains(CompLoaderType.Quilt)
                    || Config.Download.Comp.IgnoreQuilt);

            var joined = string.Join(" / ", modLoadersForDesc);
            return isAny
                ? (Lang.Text("Download.Comp.Type.Any"), "")
                : (joined, joined);
        }


        public KeyValuePair<string, string> GetControlTitle(bool hasModLoaderDescription)
        {
            if (!_TryGetControlTitleParts(out var title, out var subtitleList))
                return new KeyValuePair<string, string>(title, "");

            var subtitleResult = _BuildControlSubtitle(subtitleList, hasModLoaderDescription);
            return new KeyValuePair<string, string>(title, subtitleResult);
        }

        private bool _TryGetControlTitleParts(out string title, out List<string> subtitleList)
        {
            subtitleList = [];

            var nameLists = RawName
                .Split(
                    [" | ", " - ", "(", ")", "[", "]", "{", "}"],
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim(' ', '/', '\\', '"'))
                .Where(w => !string.IsNullOrEmpty(w))
                .ToList();

            if (nameLists.Count <= 1)
            {
                title = RawName;
                return false;
            }

            var normalNameList = new List<string>();
            foreach (var name in nameLists)
            {
                var lowerName = name.ToLower();
                if ((name.Equals(name, StringComparison.CurrentCultureIgnoreCase)
                        && name != "FPS"
                        && name != "HUD")
                    || _IsModLoaderMarker(lowerName))
                    subtitleList.Add(name);
                else
                    normalNameList.Add(name);
            }

            if (normalNameList.Count == 0 || subtitleList.Count == 0)
            {
                title = RawName;
                return false;
            }

            title = string.Join(" - ", normalNameList);
            return true;
        }

        private static string _BuildControlSubtitle(
            List<string> subtitleList,
            bool hasModLoaderDescription)
        {
            var finalSubtitles = new List<string>();
            foreach (var rawEx in subtitleList.Distinct())
            {
                var ex = rawEx;
                var lowerEx = ex.ToLower();
                var isModLoader = lowerEx.Contains("forge")
                    || lowerEx.Contains("fabric")
                    || lowerEx.Contains("quilt");

                if (!hasModLoaderDescription && isModLoader) continue;
                if (ex.Length < 16 && lowerEx.Contains("fabric") && lowerEx.Contains("forge"))
                    continue;

                if (isModLoader
                    && !ex.Contains('版')
                    && lowerEx
                        .Replace("forge", "")
                        .Replace("fabric", "")
                        .Replace("quilt", "")
                        .Length
                    <= 3)
                    ex = ex
                            .Replace("Edition", "", StringComparison.OrdinalIgnoreCase)
                            .Replace("edition", "", StringComparison.OrdinalIgnoreCase)
                            .Trim()
                            .Capitalize()
                        + Lang.Text("Download.Comp.Detail.CompItem.EditionSuffix");

                ex = ex
                    .Replace("forge", "Forge")
                    .Replace("neo", "Neo")
                    .Replace("fabric", "Fabric")
                    .Replace("quilt", "Quilt");
                finalSubtitles.Add(ex.Trim());
            }

            return finalSubtitles.Count != 0
                ? $"  |  {string.Join("  |  ", finalSubtitles)}"
                : "";
        }

        private static bool _IsModLoaderMarker(string input)
        {
            return (input.Contains("forge") || input.Contains("fabric") || input.Contains("quilt"))
                && !input
                    .Replace("forge", "")
                    .Replace("fabric", "")
                    .Replace("quilt", "")
                    .RegexCheck("[a-z]+");
        }

        public bool IsLike(CompProject project)
        {
            return (Id ?? "") == (project.Id ?? "");
        }

        public override string ToString()
        {
            return $"{Id} ({Slug}): {RawName}";
        }

        public override bool Equals(object obj)
        {
            var project = obj as CompProject;
            return project is not null && (Id ?? "") == (project.Id ?? "");
        }

        public static bool operator ==(CompProject left, CompProject right)
        {
            return EqualityComparer<CompProject>.Default.Equals(left, right);
        }

        public static bool operator !=(CompProject left, CompProject right)
        {
            return !(left == right);
        }
    }
}