using CallCenter.Cost.Costing;
using CallCenter.Cost.Pricing;
using CallCenter.Cost.Usage;

namespace CallCenter.LlmGate.Costing;

/// <summary>
/// 成本换算单一入口：复用 CallCenter.Cost（归一 + 计价 + 声明价格表）。
/// 缺定价/缺用量 ⇒ <see cref="CostResult.Unknown"/>（Amount=null），禁按 0 计。
/// </summary>
public sealed class GateCosting
{
    private readonly IPriceTable _prices;
    private readonly ProviderUsageNormalizer _normalizer;
    private readonly TokenCostCalculator _calculator = new();

    public GateCosting(IPriceTable? prices = null, ProviderUsageNormalizer? normalizer = null)
    {
        _prices = prices ?? DeclaredPriceTable.Empty;
        _normalizer = normalizer ?? ProviderUsageNormalizer.CreateDefault();
    }

    public bool TryGetQuote(string? provider, string? model, DateOnly day, out PriceQuote quote)
    {
        quote = null!;
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        return _prices.TryGet(provider!, model!, day, out quote!);
    }

    /// <summary>调用前粗估（字符/4 ≈ token），仅用于人工闸阈值；标 Estimated。</summary>
    public decimal? EstimateInputCost(string? provider, string? model, int totalChars)
    {
        if (totalChars <= 0 || !TryGetQuote(provider, model, DateOnly.FromDateTime(DateTime.UtcNow), out var quote))
        {
            return null;
        }

        var tokens = totalChars / 4m;
        return Math.Round(tokens * quote.InputPerToken, 8, MidpointRounding.AwayFromZero);
    }

    /// <summary>按 provider 归一原始 usage（缺字段 ⇒ Unknown + false）。</summary>
    public bool TryNormalizeUsage(string? provider, IReadOnlyDictionary<string, long?> raw, out LlmUsage usage)
        => _normalizer.TryNormalize(provider, raw, out usage);

    /// <summary>计价（缺用量 ⇒ Unknown）。</summary>
    public CostResult Price(string? provider, string? model, LlmUsage usage)
    {
        if (!TryGetQuote(provider, model, DateOnly.FromDateTime(DateTime.UtcNow), out var quote))
        {
            return CostResult.Unknown();
        }

        return _calculator.Calculate(usage, quote);
    }
}
