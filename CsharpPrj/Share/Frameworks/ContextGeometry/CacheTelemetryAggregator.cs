namespace Share.ContextGeometry;

/// <summary>遥测聚合结果（只读可见化；不参与放行/终止裁决）。</summary>
public sealed record CacheTelemetry(
    int Samples,
    int Observed,
    int Unknown,
    long CacheReadTokens,
    long PromptTokens,
    double? HitRatio,
    int PromptUnknown = 0)
{
    /// <summary>空样本。</summary>
    public static readonly CacheTelemetry Empty = new(0, 0, 0, 0, 0, null);
}

/// <summary>
/// 缓存遥测聚合（纯函数）：Unknown 不进分母；口径复用 <see cref="CacheHitAccounting"/> / <see cref="CacheEconomics"/>。
/// </summary>
public static class CacheTelemetryAggregator
{
    /// <summary>聚合样本（未知样本计入 <see cref="CacheTelemetry.Unknown"/>，不计入比率）。</summary>
    public static CacheTelemetry Aggregate(IEnumerable<CacheUsageSample>? samples)
    {
        var list = (samples ?? Enumerable.Empty<CacheUsageSample>()).ToArray();
        if (list.Length == 0) return CacheTelemetry.Empty;

        var observed = 0;
        var unknown = 0;
        var promptUnknown = 0;
        long read = 0;
        long prompt = 0;
        foreach (var sample in list)
        {
            if (sample.CacheReadTokens is null)
            {
                unknown++;
                continue;
            }

            observed++;
            read += Math.Max(0, sample.CacheReadTokens.Value);

            // 用量缺失 ≠ 0（ContextGeometry.md §4.4，原 :41 ?? 0 把"未观测的 prompt"折成 0 计入总量）：
            // prompt 缺失计入 PromptUnknown 并不进总量，避免把未知伪装成零消耗。
            if (sample.PromptTokens is int promptTokens)
                prompt += Math.Max(0, promptTokens);
            else
                promptUnknown++;
        }

        var ratio = CacheEconomics.HitRatio(list);
        return new CacheTelemetry(list.Length, observed, unknown, read, prompt, ratio, promptUnknown);
    }

    /// <summary>按维度聚合（K7：K 组 / 角色 / 租户；维度 = 声明键，禁从内容猜）。</summary>
    public static IReadOnlyList<CacheTelemetryDimension> AggregateByDimension(
        IEnumerable<CacheDimensionSample>? samples)
    {
        var groups = new Dictionary<string, List<CacheUsageSample>>(StringComparer.Ordinal);
        foreach (var item in samples ?? Enumerable.Empty<CacheDimensionSample>())
        {
            var key = string.IsNullOrWhiteSpace(item.Dimension) ? "unknown" : item.Dimension!;
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<CacheUsageSample>();
                groups[key] = list;
            }
            list.Add(item.Sample);
        }

        var result = new List<CacheTelemetryDimension>(groups.Count);
        foreach (var key in groups.Keys.OrderBy(k => k, StringComparer.Ordinal))
            result.Add(new CacheTelemetryDimension(key, Aggregate(groups[key])));
        return result;
    }

    /// <summary>
    /// 按四键组合聚合（R7：租户 / K 组 / 角色 / 委托批次）：未声明维不进标签、不进分母。
    /// </summary>
    public static IReadOnlyList<CacheTelemetryDimension> AggregateByKeys(
        IEnumerable<CacheKeyedSample>? samples)
    {
        var groups = new Dictionary<string, List<CacheUsageSample>>(StringComparer.Ordinal);
        foreach (var item in samples ?? Enumerable.Empty<CacheKeyedSample>())
        {
            var key = item.Label();
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<CacheUsageSample>();
                groups[key] = list;
            }
            list.Add(item.Sample);
        }

        var result = new List<CacheTelemetryDimension>(groups.Count);
        foreach (var key in groups.Keys.OrderBy(k => k, StringComparer.Ordinal))
            result.Add(new CacheTelemetryDimension(key, Aggregate(groups[key])));
        return result;
    }
}

/// <summary>四键样本（R7；键全部来自声明，禁从内容推断）。</summary>
public readonly record struct CacheKeyedSample(
    CacheUsageSample Sample,
    string? Tenant = null,
    string? KGroup = null,
    string? Role = null,
    string? DelegationBatch = null)
{
    /// <summary>组合标签（缺失维不出现；全空 ⇒ "unknown"）。</summary>
    public string Label()
    {
        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(Tenant)) parts.Add("tenant=" + Tenant);
        if (!string.IsNullOrWhiteSpace(KGroup)) parts.Add("kGroup=" + KGroup);
        if (!string.IsNullOrWhiteSpace(Role)) parts.Add("role=" + Role);
        if (!string.IsNullOrWhiteSpace(DelegationBatch)) parts.Add("batch=" + DelegationBatch);
        return parts.Count == 0 ? "unknown" : string.Join("|", parts);
    }
}

/// <summary>带维度的样本（K7；维度键由宿主声明，不从内容推断）。</summary>
public readonly record struct CacheDimensionSample(CacheUsageSample Sample, string Dimension);

/// <summary>单维度聚合视图。</summary>
public sealed record CacheTelemetryDimension(string Dimension, CacheTelemetry Telemetry);
