namespace PCL;

public static partial class Comp
{
    public class CompRequest
    {
        /// <summary>
        ///     通过一堆 ID 从 Modrinth 那获取项目信息
        /// </summary>
        /// <param name="ids"></param>
        /// <returns></returns>
        public static async Task<List<CompProject>> GetListByIdsFromModrinthAsync(List<string> ids)
        {
            var res = new List<CompProject>();
            try
            {
                await Task.Run(() =>
                {
                    var rawProjectsData = GameDownload.DlModRequest<JsonArray>(
                        $"https://api.modrinth.com/v2/projects?ids=[\"{ids.Join("\",\"")}\"]");
                    res.AddRange(
                        from rawData in rawProjectsData.OfType<JsonObject>()
                        select new CompProject(rawData));
                });
            }
            catch (Exception ex)
            {
                LauncherCommon.Log(ex, "从 Modrinth 获取数据失败");
            }

            return res;
        }

        public static List<CompProject> GetCompProjectsByIds(List<string> input)
        {
            return GetCompProjectsByIdsAsync(input).GetAwaiter().GetResult();
        }

        public static async Task<List<CompProject>> GetCompProjectsByIdsAsync(List<string> input)
        {
            if (input.Count == 0)
                return [];

            return await GetListByIdsFromModrinthAsync(input);
        }
    }
}