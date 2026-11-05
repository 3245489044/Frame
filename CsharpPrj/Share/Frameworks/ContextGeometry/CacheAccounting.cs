namespace Share.ContextGeometry;

/// <summary>
/// 单次调用用量样本（全 nullable）：缺字段 = 未观测，**禁按 0 计**。
/// </summary>
public readonly record struct CacheUsageSample(int? PromptTokens, int? CacheReadTokens, int? CacheWriteTokens);

/// <summary>命中核算结果：<see cref="Observed"/>=false ⇒ <see cref="Ratio"/>=null（Unknown ≠ 0）。</summary>
public readonly record struct CacheHitResult(bool Observed, double? Ratio, string Code);

/// <summary>命中核算（纯函数；口径唯一，禁平行实现）。</summary>
public static class CacheHitAccounting
{
    /// <summary>
    /// 比率 = cacheRead / 估计前缀 token（不夹取）；任一缺失 ⇒ 显式 Unknown。
    /// <para>区分三种"未知"（ContextGeometry.md §4.4，原 :24 Clamp 把"估计偏小"伪造成满命中）：
    /// 用量缺失 = 未观测；估计缺失 = 已观测但不可算；比率 &gt; 1 = 估计失真（显式诊断，不压成 1）。</para>
    /// </summary>
    public static CacheHitResult Evaluate(CacheUsageSample sample, int estimatedPrefixTokens)
    {
        if (sample.CacheReadTokens is null)
            return new CacheHitResult(false, null, GeometryDiagnostics.UsageMissing);

        var read = Math.Max(0, sample.CacheReadTokens.Value);
        if (estimatedPrefixTokens <= 0)
            return new CacheHitResult(true, null, GeometryDiagnostics.EstimateMissing);

        var ratio = (double)read / estimatedPrefixTokens;
        var code = ratio > 1d ? GeometryDiagnostics.RatioOverflow : "ok";
        return new CacheHitResult(true, ratio, code);
    }
}
