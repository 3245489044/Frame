using System.Text.Json;

namespace Operation.StatisticalDriven.Usage;

/// <summary>usage JSON 读取辅助：大小写不敏感、缺键/类型不符回退 0（映射表见设计文档 5.1）。</summary>
internal static class UsageJsonReader
{
    public static long GetLong(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return 0;
        foreach (var p in obj.EnumerateObject())
        {
            if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            return p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetInt64() : 0;
        }
        return 0;
    }

    /// <summary>读取嵌套对象字段（如 OpenAI prompt_tokens_details.cached_tokens）。</summary>
    public static long GetNestedLong(JsonElement obj, string parent, string child)
    {
        if (obj.ValueKind != JsonValueKind.Object) return 0;
        foreach (var p in obj.EnumerateObject())
        {
            if (!string.Equals(p.Name, parent, StringComparison.OrdinalIgnoreCase)) continue;
            return p.Value.ValueKind == JsonValueKind.Object ? GetLong(p.Value, child) : 0;
        }
        return 0;
    }
}