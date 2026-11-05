namespace Share.ContextGeometry;

/// <summary>
/// keepalive 判据（纯函数）：内核不持定时器/线程/时钟，时间由宿主传入。
/// <para>三重有界：间隔下限、TTL 上限、总次数上限（防成本反噬）。</para>
/// </summary>
public static class KeepAlivePolicy
{
    /// <summary>是否应重放冻结前缀（未到期/已过期/超次数 ⇒ false）。</summary>
    public static bool ShouldReplay(
        DateTimeOffset lastUsedUtc,
        DateTimeOffset nowUtc,
        int intervalSeconds,
        int ttlSeconds,
        int replayedCount = 0,
        int maxReplays = int.MaxValue)
    {
        if (intervalSeconds <= 0 || ttlSeconds <= 0) return false;
        if (maxReplays <= 0 || replayedCount >= maxReplays) return false;

        var elapsed = nowUtc - lastUsedUtc;
        if (elapsed < TimeSpan.FromSeconds(intervalSeconds)) return false;
        if (elapsed > TimeSpan.FromSeconds(ttlSeconds)) return false;
        return true;
    }
}

/// <summary>
/// 委托判据（纯函数，声明驱动）：排除清单命中 ⇒ 内联执行（不产生委托）。
/// <para>禁运行时 LLM 判断（零判断路由）。</para>
/// </summary>
public static class DelegationPolicy
{
    /// <summary>工具调用是否应委托（空工具名/排除命中 ⇒ false）。</summary>
    public static bool ShouldDelegate(
        string? toolName,
        int estimatedTokens,
        IReadOnlyCollection<string>? excludeTools)
    {
        _ = estimatedTokens; // 预留成本维度；当前判据只读声明排除清单。
        if (string.IsNullOrWhiteSpace(toolName)) return false;
        if (excludeTools is null || excludeTools.Count == 0) return true;

        foreach (var excluded in excludeTools)
        {
            if (string.IsNullOrWhiteSpace(excluded)) continue;
            if (string.Equals(excluded.Trim(), toolName.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}

/// <summary>经济性对账（纯计算，无 IO）：只读样本，Unknown 不按 0 计入。</summary>
public static class CacheEconomics
{
    /// <summary>命中率 = Σread / Σprompt（仅统计双字段齐备的样本）；无可用样本 ⇒ null。</summary>
    public static double? HitRatio(IEnumerable<CacheUsageSample>? samples)
    {
        long read = 0;
        long prompt = 0;
        var any = false;

        foreach (var sample in samples ?? Enumerable.Empty<CacheUsageSample>())
        {
            if (sample.CacheReadTokens is not int sampleRead || sample.PromptTokens is not int samplePrompt) continue;
            // 负值 = 非法观测（显式码 geom_invalid_observation），不参与比率，禁归零伪造"零消耗"。
            if (sampleRead < 0 || samplePrompt < 0) continue;
            read += sampleRead;
            prompt += samplePrompt;
            any = true;
        }

        if (!any || prompt <= 0) return null;
        // 不夹取：Σread/Σprompt > 1 表示估计失真，必须原样暴露（ContextGeometry.md §4.4，原 :73 Clamp 掩盖异常）。
        return (double)read / prompt;
    }

    /// <summary>缓存读 token 合计（Unknown/非法观测不计入；无样本 ⇒ 0）。</summary>
    public static long TotalCacheRead(IEnumerable<CacheUsageSample>? samples)
    {
        long total = 0;
        foreach (var sample in samples ?? Enumerable.Empty<CacheUsageSample>())
        {
            if (sample.CacheReadTokens is not int read || read < 0) continue;
            total += read;
        }

        return total;
    }
}
