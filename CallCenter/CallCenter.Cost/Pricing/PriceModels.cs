namespace CallCenter.Cost.Pricing;

/// <summary>阶梯价：PromptTotal 达到 ThresholdPromptTokens 时覆盖单价。</summary>
public sealed record PriceTier(
    long ThresholdPromptTokens,
    decimal InputPerToken,
    decimal? CachedInputPerToken,
    decimal? CacheWritePerToken,
    decimal OutputPerToken);

/// <summary>
/// 价格声明（provider/model/effective_date/单价/阶梯/compute 单价）。
/// 金额一律 decimal；缺单价由调用方判 Unknown，不在此填 0。
/// </summary>
public sealed record PriceQuote(
    string Provider,
    string Model,
    DateOnly EffectiveDate,
    string Currency,
    decimal InputPerToken,
    decimal? CachedInputPerToken,
    decimal? CacheWritePerToken,
    decimal OutputPerToken,
    IReadOnlyList<PriceTier>? Tiers = null,
    decimal? CpuPerSecond = null,
    decimal? MemoryPerGbSecond = null,
    decimal? DiskPerGbWrite = null,
    decimal? DiskPerGb = null,
    decimal? PerCallPrice = null,
    decimal? CacheDiscount = null)
{
    /// <summary>按 PromptTotal 选择生效阶梯（未命中阶梯 ⇒ 基础价）。</summary>
    public PriceQuote EffectiveFor(long? promptTotal)
    {
        if (Tiers is null || Tiers.Count == 0 || promptTotal is null)
        {
            return this;
        }

        var tier = Tiers
            .Where(t => t.ThresholdPromptTokens <= promptTotal.Value)
            .OrderByDescending(t => t.ThresholdPromptTokens)
            .FirstOrDefault();

        return tier is null
            ? this
            : this with
            {
                InputPerToken = tier.InputPerToken,
                CachedInputPerToken = tier.CachedInputPerToken ?? CachedInputPerToken,
                CacheWritePerToken = tier.CacheWritePerToken ?? CacheWritePerToken,
                OutputPerToken = tier.OutputPerToken,
            };
    }
}

/// <summary>价格表读取端口（缺价 ⇒ false，由计价器转 Unknown）。</summary>
public interface IPriceTable
{
    bool TryGet(string provider, string model, DateOnly date, out PriceQuote quote);
}
