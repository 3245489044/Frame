namespace Share.Frameworks.Billing;

/// <summary>
/// 纯成本公式（可单测，无 I/O）。对应设计文档第 7 章。
/// </summary>
public static class CostFormulas
{
    /// <summary>单次 LLM 调用的金额与原始输入 token 分解。</summary>
    /// <remarks>
    /// 输入口径由 <paramref name="p"/>.InputIncludesCached 决定：
    /// true ⇒ rawInput = input - cached；false ⇒ rawInput = input（cached 另行计价）。
    /// 缓存写入（cache_write）单独按 CacheWritePrice 计价。
    /// </remarks>
    public static (long RawInputTokens, decimal Cost) LlmCallCost(
        long inputTokens,
        long cachedTokens,
        long cacheWriteTokens,
        long outputTokens,
        long? contextLength,
        ModelPricing p)
    {
        ArgumentNullException.ThrowIfNull(p);

        var rawInput = p.InputIncludesCached ? Math.Max(0, inputTokens - cachedTokens) : Math.Max(0, inputTokens);
        var inputCost = p.InputCost(rawInput, contextLength);
        var cost = inputCost
                   + cachedTokens * p.CachedInputPrice
                   + cacheWriteTokens * p.CacheWritePrice
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
