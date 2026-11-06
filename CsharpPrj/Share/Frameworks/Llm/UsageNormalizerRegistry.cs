using System.Collections.Concurrent;
using System.Text.Json;

namespace Operation.StatisticalDriven.Usage;

/// <summary>
/// 供应商 usage normalizer 注册表：按 provider 解析；未注册回落 <see cref="Default"/>（默认 <see cref="UnknownUsageNormalizer"/>）。
/// 并发：注册/解析线程安全（ConcurrentDictionary）。
/// </summary>
public sealed class UsageNormalizerRegistry
{
    private readonly ConcurrentDictionary<string, IUsageNormalizer> _byProvider =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>未注册供应商的兜底归一化器（默认按常见字段名尽力映射）。</summary>
    public IUsageNormalizer Default { get; private set; } = new UnknownUsageNormalizer();

    public UsageNormalizerRegistry Register(string provider, IUsageNormalizer normalizer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(normalizer);
        _byProvider[provider] = normalizer;
        return this;
    }

    /// <summary>替换未知供应商兜底（传 null 恢复默认）。</summary>
    public UsageNormalizerRegistry WithDefault(IUsageNormalizer? normalizer)
    {
        Default = normalizer ?? new UnknownUsageNormalizer();
        return this;
    }

    /// <summary>注册内置四家（openai/anthropic/deepseek/gemini）。</summary>
    public UsageNormalizerRegistry RegisterBuiltIn()
    {
        Register("openai", new OpenAiUsageNormalizer());
        Register("anthropic", new AnthropicUsageNormalizer());
        Register("deepseek", new DeepSeekUsageNormalizer());
        Register("gemini", new GeminiUsageNormalizer());
        return this;
    }

    public UnifiedUsage Normalize(string provider, string model, JsonElement usage)
    {
        var normalizer = string.IsNullOrWhiteSpace(provider)
            ? Default
            : _byProvider.GetValueOrDefault(provider, Default);
        return normalizer.Normalize(model, usage);
    }
}
