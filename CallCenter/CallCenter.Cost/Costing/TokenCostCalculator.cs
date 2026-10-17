using CallCenter.Cost.Pricing;
using CallCenter.Cost.Usage;

namespace CallCenter.Cost.Costing;

/// <summary>token 计价器：基础价 / 缓存价 / 缓存写入价 / 输出价 / 阶梯。</summary>
public sealed class TokenCostCalculator
{
    public CostResult Calculate(LlmUsage usage, PriceQuote quote)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(quote);

        if (!usage.IsKnown || usage.UncachedInput is null || usage.Output is null)
        {
            return CostResult.Unknown(quote.Currency);
        }

        var effective = quote.EffectiveFor(usage.PromptTotal);
        var cachedPrice = effective.CachedInputPerToken
            ?? effective.InputPerToken * (effective.CacheDiscount ?? 1m);
        var writePrice = effective.CacheWritePerToken ?? effective.InputPerToken;

        var items = new List<CostBreakdownItem>
        {
            new("uncached", usage.UncachedInput.Value * effective.InputPerToken),
            new("cached", (usage.CacheRead ?? 0) * cachedPrice,
                effective.CachedInputPerToken is null ? "fallback:input*discount" : null),
            new("cache_write", (usage.CacheWrite ?? 0) * writePrice,
                effective.CacheWritePerToken is null ? "fallback:input" : null),
            new("output", usage.Output.Value * effective.OutputPerToken),
        };

        return CostResult.Known(effective.Currency, items, CostSource.Measured);
    }
}
