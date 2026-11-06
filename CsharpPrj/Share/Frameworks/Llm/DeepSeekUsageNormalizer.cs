using System.Text.Json;
using Share.Frameworks.Billing;

namespace Share.Frameworks.Llm.Usage;

/// <summary>DeepSeek：prompt_tokens / completion_tokens / prompt_cache_hit_tokens / prompt_cache_miss_tokens。</summary>
/// <remarks>
/// prompt_tokens 为命中+未命中之和（InputIncludesCached = true）；
/// prompt_cache_miss_tokens 是“未命中”而非“缓存写入”，CacheWriteTokens 恒为 0（E-3）。
/// </remarks>
public sealed class DeepSeekUsageNormalizer : IUsageNormalizer
{
    public UnifiedUsage Normalize(string model, JsonElement usage)
        => new(
            Provider: "deepseek",
            Model: model,
            InputTokens: UsageJsonReader.GetLong(usage, "prompt_tokens"),
            OutputTokens: UsageJsonReader.GetLong(usage, "completion_tokens"),
            CachedTokens: UsageJsonReader.GetLong(usage, "prompt_cache_hit_tokens"),
            CacheWriteTokens: 0,
            InputIncludesCached: true);
}
