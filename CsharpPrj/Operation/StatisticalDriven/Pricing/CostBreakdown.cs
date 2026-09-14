namespace Operation.StatisticalDriven.Pricing;

/// <summary>
/// 一次成本投影的结果分解。金额统一 <see cref="decimal"/> 保真；量纲为设计文档示例（token / 秒 / 字节）。
/// </summary>
public sealed record CostBreakdown
{
    // —— 事件计数 ——
    public long LlmCallCount { get; init; }
    public long ComputeSpanCount { get; init; }
    public long ReinjectCount { get; init; }

    // —— LLM Token 分解 ——
    public long InputTokens { get; init; }
    public long CachedTokens { get; init; }
    public long OutputTokens { get; init; }

    /// <summary>普通输入 token（= InputTokens - CachedTokens）。</summary>
    public long RawInputTokens => Math.Max(0, InputTokens - CachedTokens);

    // —— 计算资源分解 ——
    public double CpuSeconds { get; init; }
    public double MemoryGbSeconds { get; init; }
    public double IoBytes { get; init; }

    /// <summary>输出生命周期重复注入 token 总数。</summary>
    public long ReinjectedTokens { get; init; }

    // —— 金额 ——
    public decimal LlmCost { get; init; }
    public decimal ComputeCost { get; init; }
    public decimal LifecycleCost { get; init; }
    public decimal TotalCost => LlmCost + ComputeCost + LifecycleCost;
}