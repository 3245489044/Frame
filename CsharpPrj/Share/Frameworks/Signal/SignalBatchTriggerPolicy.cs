using System;

namespace Share.Signal;

/// <summary>信号批触发输入（计数 / 时间戳 / 依赖 / 显式）。</summary>
public sealed record SignalBatchInput(
    string Scope,
    string CoalesceKey,
    int Count,
    long OldestTimestamp,
    long Now,
    bool DependencySatisfied = false,
    bool ExplicitFlush = false);

/// <summary>触发裁决结果。</summary>
public sealed record SignalTriggerDecision(bool Fire, string Reason)
{
    /// <summary>不触发（缺省）。</summary>
    public static readonly SignalTriggerDecision Hold = new(false, "未达触发条件");

    /// <summary>触发。</summary>
    public static SignalTriggerDecision Fire_(string reason) => new(true, reason);
}

/// <summary>信号攒批策略（配置注入，禁静态读）。</summary>
public sealed record SignalBatchPolicy(int BatchThreshold = 3, long FlushWindowMs = 5_000);

/// <summary>
/// 多级信号触发策略（确定性零 LLM，纯函数）：显式 &gt; 依赖 &gt; 计数 &gt; 时间窗。
/// </summary>
public static class SignalBatchTriggerPolicy
{
    public static SignalTriggerDecision Evaluate(in SignalBatchInput input, in SignalBatchPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(policy);
        if (input.Count <= 0) return SignalTriggerDecision.Hold;

        var threshold = policy.BatchThreshold > 0 ? policy.BatchThreshold : 3;
        var window = policy.FlushWindowMs > 0 ? policy.FlushWindowMs : 5_000;

        if (input.ExplicitFlush) return SignalTriggerDecision.Fire_("显式 flush");
        if (input.DependencySatisfied) return SignalTriggerDecision.Fire_("依赖满足");
        if (input.Count >= threshold) return SignalTriggerDecision.Fire_($"攒够 {input.Count}≥{threshold} 条");

        if (input.OldestTimestamp > 0 && input.Now - input.OldestTimestamp >= window)
            return SignalTriggerDecision.Fire_($"时间窗到期 {input.Now - input.OldestTimestamp}ms");

        return SignalTriggerDecision.Hold;
    }
}
