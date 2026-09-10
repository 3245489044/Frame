
using System.Text.Json.Nodes;
using Plate.JsonMethod.Utilities;

namespace Common.PlaceholderFramework.TemplateExpansion.Converters;

public static class JsonMerger
{
    public static bool Merge(JsonObject target, JsonObject source, HashSet<string>? skipKeys = null)
    {
        return JsonMergeService.DeepMergeSkipKeys(target, source, skipKeys, JsonArrayMergeMode.Deduplicate);
    }

    public static string? GetItemKey(JsonObject obj)
    {
        return JsonMergeService.GetItemKey(obj);
    }
}
