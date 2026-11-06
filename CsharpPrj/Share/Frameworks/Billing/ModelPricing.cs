namespace Share.Frameworks.Billing;

/// <summary>
/// LLM 模型定价（设计文档 7.2 / 4.x pricing）。单价以十进制保真金额。
/// 兼容阶梯定价：命中 <see cref="TierThresholdTokens"/> 后，仅超出阈值的原始输入部分按
/// <see cref="TierInputMultiplier"/> 溢价计价（阈值内仍按 <see cref="InputPrice"/>）。
/// </summary>
public sealed record ModelPricing
{
    public required string Provider { get; init; }

    public required string Model { get; init; }

    /// <summary>普通输入单价（$/token）。</summary>
    public decimal InputPrice { get; init; } = 0m;

    /// <summary>输出单价（$/token）。</summary>
    public decimal OutputPrice { get; init; } = 0m;

    /// <summary>缓存命中输入单价（$/token）。</summary>
    public decimal CachedInputPrice { get; init; } = 0m;

    /// <summary>缓存写入单价（$/token）。</summary>
    public decimal CacheWritePrice { get; init; } = 0m;

    /// <summary>原始 input 是否含 cached（与供应商语义一致；默认 true = OpenAI/DeepSeek/Gemini）。</summary>
    public bool InputIncludesCached { get; init; } = true;

    /// <summary>阶梯定价阈值（原始输入 token 数，&gt; 阈值启用溢价）；null = 无阶梯。</summary>
    public long? TierThresholdTokens { get; init; }

    /// <summary>阶梯溢价比率（如 2.0 = 超阈值部分输入按 2 倍输入价）。</summary>
    public decimal TierInputMultiplier { get; init; } = 1m;

    /// <summary>
    /// 单次调用的输入金额（分段计价）：阈值内按 <see cref="InputPrice"/>，超出部分按溢价。
    /// 阈值触发以 <paramref name="contextLength"/>（缺省用本段原始输入长度）判定。
    /// </summary>
    public decimal InputCost(long rawInputTokens, long? contextLength)
    {
        if (rawInputTokens <= 0) return 0m;
        if (!TierThresholdTokens.HasValue || TierInputMultiplier <= 1m)
            return rawInputTokens * InputPrice;

        var threshold = TierThresholdTokens.Value;
        var effectiveLength = contextLength ?? rawInputTokens;
        if (effectiveLength <= threshold)
            return rawInputTokens * InputPrice;

        var tieredTokens = Math.Max(0, rawInputTokens - threshold);
        var baseTokens = rawInputTokens - tieredTokens;
        return baseTokens * InputPrice + tieredTokens * InputPrice * TierInputMultiplier;
    }

    /// <summary>
    /// 兼容旧入口：按单次输入上下文长度决定**整体**有效单价（不做分段）。
    /// 新代码请用 <see cref="InputCost"/> 以获得正确的分段计价。
    /// </summary>
    public decimal EffectiveInputPrice(long? contextLength)
        => TierThresholdTokens.HasValue
           && contextLength.HasValue
           && contextLength.Value > TierThresholdTokens.Value
            ? InputPrice * TierInputMultiplier
            : InputPrice;
}
