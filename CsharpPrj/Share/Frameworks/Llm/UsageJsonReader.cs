using System.Globalization;
using System.Text.Json;

namespace Share.Frameworks.Llm.Usage;

/// <summary>usage JSON 读取辅助：大小写不敏感、缺键/类型不符回退 0（映射表见设计文档 5.1）。</summary>
internal static class UsageJsonReader
{
    /// <summary>读取整数字段：接受 Number 与数字字符串（E-11），其余回退 0。</summary>
    public static long GetLong(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return 0;
        foreach (var p in obj.EnumerateObject())
        {
            if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            return ReadLong(p.Value);
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

    private static long ReadLong(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt64(out var integer)) return integer;
            if (value.TryGetDouble(out var number)) return (long)Math.Round(number);
            return 0;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                return integer;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return (long)Math.Round(number);
        }

        return 0;
    }
}
