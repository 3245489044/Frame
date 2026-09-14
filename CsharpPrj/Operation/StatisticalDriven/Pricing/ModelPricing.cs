namespace Operation.StatisticalDriven.Pricing;

/// <summary>
/// LLM 模型定价（设计文档 7.2 / 4.x pricing）。单价以十进制保真金额。
/// 兼容阶梯定价：当 <see cref="EffectiveInputPrice"/> 命中 <see cref="TierThresholdTokens"/> 时，
/// 原始输入（input-cached）部分按 <see cref="TierInputMultiplier"/> 溢价计价。
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

    /// <summary>阶梯定价阈值（输入 token 数，&gt; 阈值启用溢价）；null = 无阶梯。</summary>
    public long? TierThresholdTokens { get; init; }

    /// <summary>阶梯溢价比率（如 2.0 = 超阈值部分输入按 2 倍输入价）。</summary>
    public decimal TierInputMultiplier { get; init; } = 1m;

    /// <summary>
    /// 按单次输入的上下文长度决定该次调用的有效输入单价。
    /// 只有同时提供 <see cref="TierThresholdTokens"/> 且 <paramref name="contextLength"/> 超过阈值时才溢价。
    /// </summary>
    public decimal EffectiveInputPrice(long? contextLength)
        => TierThresholdTokens.HasValue
           && contextLength.HasValue
           && contextLength.Value > TierThresholdTokens.Value
            ? InputPrice * TierInputMultiplier
            : InputPrice;
}