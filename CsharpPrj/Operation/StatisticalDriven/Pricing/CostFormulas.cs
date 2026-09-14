namespace Operation.StatisticalDriven.Pricing;

/// <summary>
/// 纯成本公式（可单测，无 I/O）。对应设计文档第 7 章。
/// </summary>
public static class CostFormulas
{
    /// <summary>单次 LLM 调用的金额与原始输入 token 分解。</summary>
    public static (long RawInputTokens, decimal Cost) LlmCallCost(
        long inputTokens, long cachedTokens, long outputTokens, long? contextLength, ModelPricing p)
    {
        var rawInput = Math.Max(0, inputTokens - cachedTokens);
        var effectiveInput = p.EffectiveInputPrice(contextLength);
        var cost = rawInput * effectiveInput
                   + cachedTokens * p.CachedInputPrice
                   + outputTokens * p.OutputPrice;
        return (rawInput, cost);
    }

    /// <summary>计算资源成本（设计文档 7.3）：CPU + 内存 + IO。</summary>
    public static decimal ComputeCost(
        double cpuSeconds, double memoryGbSeconds, double ioBytes, ResourcePricing p)
        => (decimal)cpuSeconds * p.CpuPerSecond
           + (decimal)memoryGbSeconds * p.MemoryPerGbSecond
           + (decimal)ioBytes * p.IoPerByte;

    /// <summary>输出进入上下文的历史重发成本（设计文档 7.4）。</summary>
    public static decimal LifecycleCost(long reinjectedTokens, decimal effectiveInputPrice)
        => reinjectedTokens * effectiveInputPrice;
}