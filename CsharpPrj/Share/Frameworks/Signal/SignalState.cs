using System;
using System.Collections.Generic;

namespace Share.Signal;

/// <summary>
/// 信号生命周期状态（SCF 10 态）—— 与声明面 <c>Config/nodes/signal.ushar</c> 的
/// <c>[Space] SignalState</c> 逐项同名；迁移合法边唯一权威 = <c>[Matrix] SignalLifecycle</c>。
/// <para>本枚举只承载类型面；禁止在 C# 侧写第二份迁移边（<c>SignalStateMachine</c> 只查声明表）。</para>
/// </summary>
public enum SignalState
{
    /// <summary>已产生（未校验）。</summary>
    Created = 0,

    /// <summary>已接纳（校验通过/去重命中）。</summary>
    Accepted = 1,

    /// <summary>已暂存（多级信号等待触发）。</summary>
    Queued = 2,

    /// <summary>已投递（持有租约，等待回执）。</summary>
    Dispatched = 3,

    /// <summary>接收方已认领。</summary>
    Claimed = 4,

    /// <summary>执行中。</summary>
    Running = 5,

    /// <summary>成功终态。</summary>
    Completed = 6,

    /// <summary>失败（可重试）。</summary>
    Failed = 7,

    /// <summary>外部取消终态。</summary>
    Cancelled = 8,

    /// <summary>TTL 到期终态。</summary>
    Expired = 9,

    /// <summary>重试耗尽终态（不可 claim）。</summary>
    DeadLetter = 10,
}

/// <summary>状态词表/映射（唯一字符串单源：声明面、日志、账本共用）。</summary>
public static class SignalStateNames
{
    private static readonly IReadOnlyDictionary<SignalState, string> Names =
        new Dictionary<SignalState, string>
        {
            [SignalState.Created] = "created",
            [SignalState.Accepted] = "accepted",
            [SignalState.Queued] = "queued",
            [SignalState.Dispatched] = "dispatched",
            [SignalState.Claimed] = "claimed",
            [SignalState.Running] = "running",
            [SignalState.Completed] = "completed",
            [SignalState.Failed] = "failed",
            [SignalState.Cancelled] = "cancelled",
            [SignalState.Expired] = "expired",
            [SignalState.DeadLetter] = "deadletter",
        };

    /// <summary>枚举 → 声明词。</summary>
    public static string Of(SignalState state) => Names.TryGetValue(state, out var v) ? v : "unknown";

    /// <summary>声明词 → 枚举（未知/空白 ⇒ null，不伪造）。</summary>
    public static SignalState? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        foreach (var pair in Names)
            if (string.Equals(pair.Value, value, StringComparison.OrdinalIgnoreCase))
                return pair.Key;
        return null;
    }

    /// <summary>是否终态（Completed/Cancelled/Expired/DeadLetter；Failed 可重试，不是最终态）。</summary>
    public static bool IsTerminal(SignalState state)
        => state is SignalState.Completed or SignalState.Cancelled or SignalState.Expired or SignalState.DeadLetter;

    /// <summary>是否失败（可重试的失败事件；不进入终态计数，但计入失败增量）。</summary>
    public static bool IsFailure(SignalState state) => state == SignalState.Failed;

    /// <summary>是否在途（Dispatched/Claimed/Running）。</summary>
    public static bool IsInFlight(SignalState state)
        => state is SignalState.Dispatched or SignalState.Claimed or SignalState.Running;
}
