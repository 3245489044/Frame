using System.Text.Json;

namespace Operation.StatisticalDriven.Usage;

/// <summary>
/// 未知供应商兜底：按最常见字段名（prompt_tokens/input_tokens、completion_tokens/output_tokens、
/// cached_tokens/cache_read_input_tokens）尽力映射；口径按“输入含缓存”处理。
/// </summary>
public sealed class UnknownUsageNormalizer : IUsageNormalizer
{
    public UnifiedUsage Normalize(string model, JsonElement usage)
        => new(
            Provider: "unknown",
            Model: model,
            InputTokens: First(usage, "prompt_tokens", "input_tokens"),
            OutputTokens: First(usage, "completion_tokens", "output_tokens", "output_token_count"),
            CachedTokens: First(usage, "cached_tokens", "cache_read_input_tokens", "cached_content_token_count"),
            CacheWriteTokens: First(usage, "cache_creation_input_tokens", "cache_write_tokens"),
            InputIncludesCached: true);

    private static long First(JsonElement usage, params string[] names)
    {
        foreach (var name in names)
        {
            var value = UsageJsonReader.GetLong(usage, name);
            if (value > 0) return value;
        }
        return 0;
    }
}
