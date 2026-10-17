namespace CallCenter.Cost.Pricing;

/// <summary>
/// 声明式价格表（内存实现）：同一 provider/model 取 EffectiveDate ≤ 查询日的最近一条。
/// 装载由宿主完成（本层不读文件、不依赖配置框架）。
/// </summary>
public sealed class DeclaredPriceTable : IPriceTable
{
    private readonly IReadOnlyList<PriceQuote> _quotes;

    public DeclaredPriceTable(IEnumerable<PriceQuote> quotes)
    {
        _quotes = (quotes ?? throw new ArgumentNullException(nameof(quotes)))
            .OrderByDescending(q => q.EffectiveDate)
            .ToList();
    }

    public static DeclaredPriceTable Empty { get; } = new(Array.Empty<PriceQuote>());

    public bool TryGet(string provider, string model, DateOnly date, out PriceQuote quote)
    {
        quote = null!;
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        var hit = _quotes.FirstOrDefault(q =>
            q.EffectiveDate <= date
            && string.Equals(q.Provider, provider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(q.Model, model, StringComparison.OrdinalIgnoreCase));

        if (hit is null)
        {
            return false;
        }

        quote = hit;
        return true;
    }
}
