using CallCenter.Cost.Pricing;
using CallCenter.Cost.Usage;

namespace CallCenter.Cost.Cache;

/// <summary>
/// 缓存利用率（分母恒为归一后的 PromptTotal，随结果落库 Basis，禁口径漂移）。
/// 任一字段缺失 ⇒ 对应比率为 null（Unknown），不写 0。
/// </summary>
public sealed record CacheEfficiency(
    double? HitRate,
    double? WriteRate,
    decimal? SavingAmount,
    decimal? EffectiveInputCostRatio,
    string Basis)
{
    public const string NormalizedBasis = "normalized.prompt_total";

    public static CacheEfficiency From(LlmUsage usage, PriceQuote? quote = null)
    {
        var prompt = usage.PromptTotal;
        double? hitRate = prompt is > 0 && usage.CacheRead.HasValue
            ? (double)usage.CacheRead.Value / prompt.Value
            : null;
        double? writeRate = prompt is > 0 && usage.CacheWrite.HasValue
            ? (double)usage.CacheWrite.Value / prompt.Value
            : null;

        decimal? saving = null;
        decimal? effectiveRatio = null;
        if (quote is not null && prompt is > 0 && usage.CacheRead.HasValue && usage.UncachedInput.HasValue)
        {
            var cachedPrice = quote.CachedInputPerToken
                ?? (quote.InputPerToken * (quote.CacheDiscount ?? 1m));
            saving = usage.CacheRead.Value * (quote.InputPerToken - cachedPrice);

            var fullPrice = prompt.Value * quote.InputPerToken;
            if (fullPrice > 0)
            {
                var actual = usage.CacheRead.Value * cachedPrice
                    + usage.UncachedInput.Value * quote.InputPerToken;
                effectiveRatio = actual / fullPrice;
            }
        }

        return new CacheEfficiency(hitRate, writeRate, saving, effectiveRatio, NormalizedBasis);
    }
}
