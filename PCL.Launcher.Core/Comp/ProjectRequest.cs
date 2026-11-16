using System.Net;
using System.Text;

namespace PCL;

public static partial class Comp
{
    public class CompProjectRequest
    {
        /// <summary>
        ///     筛选 MC 版本。
        /// </summary>
        public string? gameVersion = null;

        /// <summary>
        ///     筛选 Mod 加载器类别。
        /// </summary>
        public CompLoaderType modLoader = CompLoaderType.Any;

        /// <summary>
        ///     搜索的文本内容。
        /// </summary>
        public string searchText = "";

        /// <summary>
        ///     搜索结果排序方式。
        /// </summary>
        public CompSortType sort = CompSortType.Default;

        /// <summary>
        ///     加载后应输出到的结果存储器。
        /// </summary>
        public CompProjectStorage storage;

        /// <summary>
        ///     筛选资源标签。空字符串代表不限制。格式例如 "406/worldgen"，斜杠后为 Modrinth 的分类名，斜杠前的部分不使用。
        /// </summary>
        public string tag = "";

        /// <summary>
        ///     应当尽量达成的结果数量。
        /// </summary>
        public int targetResultCount;

        /// <summary>
        ///     筛选资源种类。
        /// </summary>
        public CompType type;

        /// <summary>
        ///     构造函数。
        /// </summary>
        public CompProjectRequest(
            CompType type,
            CompProjectStorage storage,
            int targetResultCount)
        {
            this.type = type;
            this.storage = storage;
            this.targetResultCount = targetResultCount;
        }

        /// <summary>
        ///     根据加载位置记录，是否还可以继续获取内容。
        /// </summary>
        public bool CanContinue
        {
            get
            {
                if (tag.EndsWithF("/"))
                    storage.modrinthTotal = 0;

                return storage.modrinthTotal == -1
                    || storage.modrinthOffset < storage.modrinthTotal;
            }
        }

        /// <summary>
        ///     获取对应的 Modrinth API 请求链接。若返回 Nothing 则为不进行 Modrinth 请求。
        /// </summary>
        public string? GetModrinthAddress()
        {
            if (tag.EndsWithF("/"))
                storage.modrinthTotal = 0;

            if (storage.modrinthTotal > -1 && storage.modrinthTotal <= storage.modrinthOffset)
                return null;

            var index = sort switch
            {
                CompSortType.Downloads => "downloads",
                CompSortType.Follows => "follows",
                CompSortType.Newest => "newest",
                CompSortType.Updated => "updated",
                _ => "relevance"
            };
            var address = $"https://api.modrinth.com/v2/search?limit={CompPageSize}&index={index}";

            if (!string.IsNullOrEmpty(searchText))
                address += $"&query={WebUtility.UrlEncode(searchText)}";
            if (storage.modrinthOffset > 0)
                address += $"&offset={storage.modrinthOffset}";

            var facets = new List<string>
            {
                $"[\"project_type:{LauncherCommon.GetStringFromEnum(type)?.ToLower() ?? ""}\"]"
            };
            if (!string.IsNullOrEmpty(tag))
                facets.Add($"[\"categories:'{tag.AfterLast("/")}'\"]");
            if (modLoader != CompLoaderType.Any)
                facets.Add(
                    $"[\"categories:'{LauncherCommon.GetStringFromEnum(modLoader)?.ToLower() ?? ""}'\"]");
            if (!string.IsNullOrEmpty(gameVersion))
                facets.Add($"[\"versions:'{gameVersion}'\"]");

            address += $"&facets=[{string.Join(",", facets)}]";
            return address;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not CompProjectRequest request)
                return false;

            return type == request.type
                && targetResultCount == request.targetResultCount
                && (tag ?? "") == (request.tag ?? "")
                && modLoader == request.modLoader
                && (gameVersion ?? "") == (request.gameVersion ?? "")
                && (searchText ?? "") == (request.searchText ?? "")
                && sort == request.sort;
        }

        public static bool operator ==(CompProjectRequest left, CompProjectRequest right)
        {
            return EqualityComparer<CompProjectRequest>.Default.Equals(left, right);
        }

        public static bool operator !=(CompProjectRequest left, CompProjectRequest right)
        {
            return !(left == right);
        }
    }
}