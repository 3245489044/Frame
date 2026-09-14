using System.Text.Json;

namespace Operation.StatisticalDriven.Usage;

/// <summary>供应商 usage normalizer 注册表：按 provider 解析，未命中抛 KeyNotFound（错误在边界抛出）。</summary>
public sealed class UsageNormalizerRegistry
{
    private readonly Dictionary<string, IUsageNormalizer> _byProvider = new(StringComparer.OrdinalIgnoreCase);

    public UsageNormalizerRegistry Register(string provider, IUsageNormalizer normalizer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        _byProvider[provider] = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
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
        if (!_byProvider.TryGetValue(provider, out var normalizer))
            throw new KeyNotFoundException($"未注册 usage normalizer: {provider}");
        return normalizer.Normalize(model, usage);
    }
}