using PCL.Core.Utils;

namespace PCL;

public static partial class DownloadUi
{
    private static List<string> _SplitMergeArguments(string allArguments)
    {
        var rawArguments = allArguments
            .Split(" ")
            .Where(l => !string.IsNullOrEmpty(l))
            .Select(l => l.Trim())
            .ToList();
        var splitArguments = new List<string>();
        for (int i = 0, loopTo = rawArguments.Count - 1; i <= loopTo; i++)
            if (rawArguments[i].StartsWithF("-"))
                splitArguments.Add(rawArguments[i]);
            else if (splitArguments.Count != 0
                     && splitArguments.Last().StartsWithF("-")
                     && !splitArguments.Last().Contains(' '))
                splitArguments[^1] = $"{splitArguments.Last()} {rawArguments[i]}";
            else
                splitArguments.Add(rawArguments[i]);

        return splitArguments;
    }

    private static void _MergeLoaderJson(
        JsonObject outputJson,
        JsonObject? loaderJson,
        bool isOverridden)
    {
        if (loaderJson is null || isOverridden)
            return;

        loaderJson.Remove("releaseTime");
        loaderJson.Remove("time");
        outputJson.Merge(loaderJson);
    }
}