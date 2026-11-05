namespace Share.ContextGeometry;

/// <summary>缓存探针四态（互斥）：Unknown（未观测） / Indeterminate（已观测但不可算） / Hit / Miss。</summary>
public enum CacheProbeState
{
    /// <summary>provider 未给缓存用量（禁按 0 计）。</summary>
    Unknown,

    /// <summary>观测到命中（比率 &gt; 0）。</summary>
    Hit,

    /// <summary>观测到但比率为 0（真实未命中）。</summary>
    Miss,

    /// <summary>快照缺失（无法归因；观测值仍保留，但不足以判命中）。</summary>
    Mismatch,

    /// <summary>已观测但无法计算比率（估计前缀 token 缺失/为 0）——不是 Miss（ContextGeometry.md §4.4）。</summary>
    Indeterminate,
}

/// <summary>探针裁决（纯结构）：观测性 + 是否命中 + 比率 + 诊断码。</summary>
public readonly record struct CacheProbeVerdict(bool Observed, bool Matched, double? Ratio, string Code)
{
    /// <summary>四态判定（互斥；Unknown 优先，Mismatch 次之，不可算再于 Miss 之前区分）。</summary>
    public CacheProbeState State
    {
        get
        {
            if (!Observed) return CacheProbeState.Unknown;
            if (string.Equals(Code, GeometryDiagnostics.SnapshotMissing, StringComparison.Ordinal))
                return CacheProbeState.Mismatch;
            // 估计缺失 ⇒ 比率不可算：判 Miss 会把"不知道"伪造成"未命中"（ContextGeometry.md §4.4，原 :30）。
            if (Ratio is null) return CacheProbeState.Indeterminate;
            return Ratio > 0 ? CacheProbeState.Hit : CacheProbeState.Miss;
        }
    }
}

/// <summary>
/// 缓存探针判据（纯函数）：口径唯一 = <see cref="CacheHitAccounting"/>；本类只补「快照归因」维度。
/// <para>禁：快照缺失时伪造命中；禁：未观测时按 0 计。</para>
/// </summary>
public static class CacheProbePolicy
{
    /// <summary>评估一次调用的缓存命中（<paramref name="estimate"/> ≤ 0 ⇒ 比率不可算，仍算已观测）。</summary>
    public static CacheProbeVerdict Evaluate(
        GeometrySnapshot? snapshot, CacheUsageSample sample, int estimate)
    {
        var hit = CacheHitAccounting.Evaluate(sample, estimate);
        if (!hit.Observed) return new CacheProbeVerdict(false, false, null, hit.Code);

        if (snapshot is null)
            return new CacheProbeVerdict(true, false, hit.Ratio, GeometryDiagnostics.SnapshotMissing);

        return new CacheProbeVerdict(true, hit.Ratio is > 0, hit.Ratio, hit.Code);
    }
}
