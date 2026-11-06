using System.Text.Json;

namespace Operation.StatisticalDriven.Usage;

/// <summary>Anthropic：input_tokens / output_tokens / cache_read_input_tokens / cache_creation_input_tokens。</summary>
/// <remarks>input_tokens 不含缓存读取/创建部分（InputIncludesCached = false）。</remarks>
public sealed class AnthropicUsageNormalizer : IUsageNormalizer
{
    public UnifiedUsage Normalize(string model, JsonElement usage)
        => new(
            Provider: "anthropic",
            Model: model,
            InputTokens: UsageJsonReader.GetLong(usage, "input_tokens"),
            OutputTokens: UsageJsonReader.GetLong(usage, "output_tokens"),
            CachedTokens: UsageJsonReader.GetLong(usage, "cache_read_input_tokens"),
            CacheWriteTokens: UsageJsonReader.GetLong(usage, "cache_creation_input_tokens"),
            InputIncludesCached: false);
}
