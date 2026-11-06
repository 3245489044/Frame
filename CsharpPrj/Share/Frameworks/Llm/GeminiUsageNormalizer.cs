using System.Text.Json;

namespace Operation.StatisticalDriven.Usage;

/// <summary>Gemini：prompt_token_count / candidates_token_count / cached_content_token_count。</summary>
/// <remarks>prompt_token_count 含 cached_content_token_count（InputIncludesCached = true）。</remarks>
public sealed class GeminiUsageNormalizer : IUsageNormalizer
{
    public UnifiedUsage Normalize(string model, JsonElement usage)
        => new(
            Provider: "gemini",
            Model: model,
            InputTokens: UsageJsonReader.GetLong(usage, "prompt_token_count"),
            OutputTokens: UsageJsonReader.GetLong(usage, "candidates_token_count"),
            CachedTokens: UsageJsonReader.GetLong(usage, "cached_content_token_count"),
            CacheWriteTokens: 0,
            InputIncludesCached: true);
}
