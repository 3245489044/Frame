namespace CallCenter.Cost.Usage;

/// <summary>供应商 usage 字段映射（声明数据；新增供应商 = 加一行）。</summary>
public sealed record ProviderFieldMap(
    string Provider,
    string InputField,
    string OutputField,
    string? CacheReadField,
    string? CacheWriteField,
    UsageShape Shape);

/// <summary>
/// 表驱动的 usage 归一器：provider → 字段名 + Shape。
/// 未知 provider / 缺字段 ⇒ false + UnknownUsage（调用方可见化，不推断）。
/// </summary>
public sealed class ProviderUsageNormalizer
{
    private readonly Dictionary<string, ProviderFieldMap> _maps;

    public ProviderUsageNormalizer(IEnumerable<ProviderFieldMap> maps)
    {
        _maps = new Dictionary<string, ProviderFieldMap>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in maps)
        {
            _maps[map.Provider] = map;
        }
    }

    /// <summary>内置四家供应商映射（OpenAI / Anthropic / DeepSeek / Gemini）。</summary>
    public static ProviderUsageNormalizer CreateDefault() => new(new[]
    {
        new ProviderFieldMap("OpenAI", "prompt_tokens", "completion_tokens",
            "cached_tokens", null, UsageShape.InclusiveCached),
        new ProviderFieldMap("Anthropic", "input_tokens", "output_tokens",
            "cache_read_input_tokens", "cache_creation_input_tokens", UsageShape.ExclusiveCached),
        new ProviderFieldMap("DeepSeek", "prompt_cache_miss_tokens", "completion_tokens",
            "prompt_cache_hit_tokens", null, UsageShape.ExclusiveCached),
        new ProviderFieldMap("Gemini", "prompt_token_count", "candidates_token_count",
            "cached_content_token_count", null, UsageShape.InclusiveCached),
    });

    /// <summary>是否已声明该供应商。</summary>
    public bool IsSupported(string? provider)
        => !string.IsNullOrWhiteSpace(provider) && _maps.ContainsKey(provider);

    /// <summary>取供应商字段映射（供适配器按声明字段构造原始字典）。</summary>
    public bool TryGetMap(string? provider, out ProviderFieldMap map)
    {
        map = null!;
        return !string.IsNullOrWhiteSpace(provider) && _maps.TryGetValue(provider!, out map!);
    }

    /// <summary>按 provider 归一；缺输入/输出/已声明缓存字段 ⇒ Unknown + false。</summary>
    public bool TryNormalize(
        string? provider,
        IReadOnlyDictionary<string, long?> raw,
        out LlmUsage usage)
    {
        usage = LlmUsage.UnknownUsage;
        if (string.IsNullOrWhiteSpace(provider) || raw is null || !_maps.TryGetValue(provider, out var map))
        {
            return false;
        }

        var input = Read(raw, map.InputField);
        var output = Read(raw, map.OutputField);
        var cacheRead = map.CacheReadField is null ? 0L : Read(raw, map.CacheReadField);
        var cacheWrite = map.CacheWriteField is null ? 0L : Read(raw, map.CacheWriteField);

        if (input is null || output is null || cacheRead is null || cacheWrite is null)
        {
            return false;
        }

        usage = map.Shape switch
        {
            UsageShape.InclusiveCached => new LlmUsage(
                Math.Max(0, input.Value - cacheRead.Value),
                cacheRead,
                cacheWrite,
                output,
                map.Shape,
                UsageSource.ProviderReported),
            UsageShape.ExclusiveCached => new LlmUsage(
                input,
                cacheRead,
                cacheWrite,
                output,
                map.Shape,
                UsageSource.ProviderReported),
            _ => LlmUsage.UnknownUsage,
        };

        return usage.Source != UsageSource.Unknown;
    }

    private static long? Read(IReadOnlyDictionary<string, long?> raw, string field)
        => raw.TryGetValue(field, out var value) ? value : null;
}
