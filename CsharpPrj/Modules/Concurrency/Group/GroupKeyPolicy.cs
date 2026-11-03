using System;

namespace Modules.Concurrency;

/// <summary>默认组键策略：{scope}:{planKey}:g{attempt}。
/// <para>同输入同 scope 的重复分发必须得到不同组键（带序号），否则第二次分发会被判「组已存在」。</para></summary>
public sealed class PlanScopedGroupKeyPolicy : IGroupKeyPolicy
{
    public string For(string scopeKey, SplitPlan plan, int attempt = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        ArgumentNullException.ThrowIfNull(plan);
        return $"{scopeKey}:{plan.PlanKey}:g{Math.Max(0, attempt)}";
    }
}
