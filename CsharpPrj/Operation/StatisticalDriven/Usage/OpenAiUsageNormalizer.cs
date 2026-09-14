using System.Text.Json;

namespace Operation.StatisticalDriven.Usage;

/// <summary>OpenAI：prompt_tokens / completion_tokens / prompt_tokens_details.cached_tokens。</summary>
public sealed class OpenAiUsageNormalizer : IUsageNormalizer
{
    public UnifiedUsage Normalize(string model, JsonElement usage)
        => new(
            Provider: "openai",
            Model: model,
            InputTokens: UsageJsonReader.GetLong(usage, "prompt_tokens"),
            OutputTokens: UsageJsonReader.GetLong(usage, "completion_tokens"),
            CachedTokens: UsageJsonReader.GetNestedLong(usage, "prompt_tokens_details", "cached_tokens"),
            CacheWriteTokens: 0);
}