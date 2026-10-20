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
    /// <summary>比率 = cacheRead / 估计前缀 token（夹在 [0,1]）；任一缺失 ⇒ 显式 Unknown。</summary>
    public static CacheHitResult Evaluate(CacheUsageSample sample, int estimatedPrefixTokens)
    {
        if (sample.CacheReadTokens is null)
            return new CacheHitResult(false, null, GeometryDiagnostics.UsageMissing);

        var read = Math.Max(0, sample.CacheReadTokens.Value);
        if (estimatedPrefixTokens <= 0)
            return new CacheHitResult(true, null, GeometryDiagnostics.EstimateMissing);

        var ratio = Math.Clamp((double)read / estimatedPrefixTokens, 0d, 1d);
        return new CacheHitResult(true, ratio, "ok");
    }
}
