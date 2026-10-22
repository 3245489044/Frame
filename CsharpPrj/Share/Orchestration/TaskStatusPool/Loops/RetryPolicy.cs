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

    public static bool IsValid(RetryPlan plan)
        => plan is not null
           && !string.IsNullOrWhiteSpace(plan.Reason)
           && Dimensions.Contains(plan.ChangedDimension)
           && !string.IsNullOrWhiteSpace(plan.ChangeDigest)
           && !string.Equals(plan.ChangeDigest, AttemptLedger.LastDigest, StringComparison.Ordinal)
           && AttemptLedger.CanAdvance;
}

/// <summary>
/// attempt 的唯一写入者（AX-ZR SingleAttemptWriter）：其余任何位置出现 attempt++ / round++ 均为违规。
/// <see cref="RetryGrant"/> 默认 0（框架语义上不重试），仅装配点显式授予且入审计。
/// </summary>
public static class AttemptLedger
{
    private static readonly object Sync = new();
    private static int _grant;
    private static int _used;
    private static string? _lastDigest;

    /// <summary>装配点授予的重试配额（默认 0）。</summary>
    public static int RetryGrant { get { lock (Sync) return _grant; } }

    /// <summary>已消耗的重试次数。</summary>
    public static int Used { get { lock (Sync) return _used; } }

    /// <summary>上次执行的输入摘要（重试必须变更）。</summary>
    public static string? LastDigest { get { lock (Sync) return _lastDigest; } }

    /// <summary>账本是否允许推进（配额未用尽）。</summary>
    public static bool CanAdvance { get { lock (Sync) return _grant > 0 && _used < _grant; } }

    /// <summary>装配点授予配额（一次性、入审计；0 = 不重试）。</summary>
    public static void Grant(int times)
    {
        if (times < 0) throw new ArgumentOutOfRangeException(nameof(times));
        lock (Sync) { _grant = times; _used = 0; }
    }

    /// <summary>仅 Retry 应用点可调：推进一次 attempt 并记录新摘要。</summary>
    public static void Advance(RetryPlan plan, string newDigest)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(newDigest)) throw new ArgumentException("新摘要不可为空。", nameof(newDigest));
        lock (Sync)
        {
            if (_grant <= 0 || _used >= _grant) throw new InvalidOperationException(FailureCodes.UnjustifiedRetry);
            _used++;
            _lastDigest = newDigest;
        }
    }

    /// <summary>重置账本（新 run 起点；不改变装配点授予的配额）。</summary>
    public static void Reset()
    {
        lock (Sync) { _used = 0; _lastDigest = null; }
    }
}
