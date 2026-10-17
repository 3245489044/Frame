namespace CallCenter.Cost.Costing;

/// <summary>成本可信度。</summary>
public enum CostSource
{
    Measured,
    Estimated,
    Unknown,
}

/// <summary>成本分解项（item 为稳定词：uncached/cached/cache_write/output/cpu/memory/disk_write/disk_footprint/per_call）。</summary>
public sealed record CostBreakdownItem(string Item, decimal Amount, string? Basis = null);

/// <summary>
/// 成本结果。Amount 为 null 表示 Unknown（区别于免费 0）；读侧禁把 Unknown 当 0 求和。
/// </summary>
public sealed record CostResult(
    decimal? Amount,
    string Currency,
    IReadOnlyList<CostBreakdownItem> Breakdown,
    CostSource Source)
{
    public bool IsKnown => Source != CostSource.Unknown && Amount.HasValue;

    /// <summary>仅已知成本可参与求和。</summary>
    public bool IsSummable => IsKnown;

    public static CostResult Unknown(string currency = "USD")
        => new(null, currency, Array.Empty<CostBreakdownItem>(), CostSource.Unknown);

    public static CostResult Known(string currency, IEnumerable<CostBreakdownItem> breakdown, CostSource source = CostSource.Measured)
    {
        var items = breakdown.ToList();
        return new CostResult(items.Sum(i => i.Amount), currency, items, source);
    }
}
