namespace Share.ContextGeometry;

/// <summary>遥测聚合结果（只读可见化；不参与放行/终止裁决）。</summary>
public sealed record CacheTelemetry(
    int Samples,
    int Observed,
    int Unknown,
    long CacheReadTokens,
    long PromptTokens,
    double? HitRatio)
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
            prompt += Math.Max(0, sample.PromptTokens ?? 0);
        }

        var ratio = CacheEconomics.HitRatio(list);
        return new CacheTelemetry(list.Length, observed, unknown, read, prompt, ratio);
    }
}
