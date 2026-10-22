using System;
using System.Collections.Generic;

namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 外部重试提议（AX-ZR RetryIsExternalProposal）：重试是「一次新的 ExecutionUnit」，
/// 不是「同一次执行内的循环」。仅装配点授予的 RetryGrant &gt; 0 时可被采纳。
/// </summary>
public sealed record RetryPlan(
    string Reason,
    string ChangedDimension,
    string ChangeDigest,
    IReadOnlyDictionary<string, object?> Delta);

/// <summary>失败后的外部裁决：Stop（终止）或 Retry（新执行单元）。</summary>
public abstract record AfterFailure
{
    private AfterFailure() { }

    public sealed record Stop(Failure Failure) : AfterFailure;

    public sealed record Retry(RetryPlan Plan) : AfterFailure;
}

/// <summary>
/// 重试合法性（AX-ZR RetryRequiresChange）：变更摘要必须不同于上次输入摘要、
/// 变更维度须在白名单内、理由非空，且账本允许推进（RetryGrant 默认 0 = 不重试）。
/// </summary>
public static class RetryPolicy
{
    private static readonly HashSet<string> Dimensions = new(StringComparer.Ordinal)
    {
        "input", "prompt", "strategy", "tool",
    };

    public static bool IsValid(RetryPlan plan, AttemptLedger ledger)
        => plan is not null
           && ledger is not null
           && !string.IsNullOrWhiteSpace(plan.Reason)
           && Dimensions.Contains(plan.ChangedDimension)
           && !string.IsNullOrWhiteSpace(plan.ChangeDigest)
           && !string.Equals(plan.ChangeDigest, ledger.LastDigest, StringComparison.Ordinal)
           && ledger.CanAdvance;
}
