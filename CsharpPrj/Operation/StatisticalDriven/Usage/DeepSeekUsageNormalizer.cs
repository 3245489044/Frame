using System.Text.Json;

namespace Operation.StatisticalDriven.Usage;

/// <summary>DeepSeek：prompt_tokens / completion_tokens / prompt_cache_hit_tokens / prompt_cache_miss_tokens。</summary>
public sealed class DeepSeekUsageNormalizer : IUsageNormalizer
{
    public UnifiedUsage Normalize(string model, JsonElement usage)
        => new(
            Provider: "deepseek",
            Model: model,
            InputTokens: UsageJsonReader.GetLong(usage, "prompt_tokens"),
            OutputTokens: UsageJsonReader.GetLong(usage, "completion_tokens"),
            CachedTokens: UsageJsonReader.GetLong(usage, "prompt_cache_hit_tokens"),
            CacheWriteTokens: UsageJsonReader.GetLong(usage, "prompt_cache_miss_tokens"));
}