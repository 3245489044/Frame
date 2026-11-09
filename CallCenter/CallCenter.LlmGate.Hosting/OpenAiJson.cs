using System.Text;
using System.Text.Json;

namespace CallCenter.LlmGate.Hosting;

/// <summary>wire JSON 基础读取（全 nullable；缺字段 ⇒ null，不推断、不按 0）。</summary>
internal static class OpenAiJson
{
    public static string? ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static bool? ReadBool(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : null;

    public static long? ReadLong(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.TryGetInt64(out var parsed) ? parsed : null
            : null;

    private static long? ReadNestedLong(JsonElement element, string parent, string child)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(parent, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? ReadLong(nested, child)
            : null;

    public static string ReadContentText(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    sb.Append(text.GetString());
                }
            }

            return sb.ToString();
        }

        return string.Empty;
    }

    /// <summary>usage → 多厂字段词典（供 ProviderUsageNormalizer 表驱动归一）。缺 usage ⇒ null。</summary>
    public static IReadOnlyDictionary<string, long?>? ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var prompt = ReadLong(usage, "prompt_tokens") ?? ReadLong(usage, "input_tokens");
        var completion = ReadLong(usage, "completion_tokens") ?? ReadLong(usage, "output_tokens");
        var cacheRead = ReadNestedLong(usage, "prompt_tokens_details", "cached_tokens")
                        ?? ReadLong(usage, "prompt_cache_hit_tokens")
                        ?? ReadLong(usage, "cache_read_input_tokens");
        var cacheWrite = ReadLong(usage, "cache_creation_input_tokens");

        if (prompt is null && completion is null && cacheRead is null && cacheWrite is null)
        {
            return null;
        }

        return new Dictionary<string, long?>(StringComparer.Ordinal)
        {
            ["prompt_tokens"] = prompt,
            ["completion_tokens"] = completion,
            ["cached_tokens"] = cacheRead,
            ["cache_read_input_tokens"] = cacheRead,
            ["cache_creation_input_tokens"] = cacheWrite,
            ["input_tokens"] = prompt,
            ["output_tokens"] = completion,
            ["prompt_cache_hit_tokens"] = cacheRead,
            ["prompt_cache_miss_tokens"] = prompt.HasValue && cacheRead.HasValue
                ? Math.Max(0, prompt.Value - cacheRead.Value)
                : prompt,
        };
    }
}
