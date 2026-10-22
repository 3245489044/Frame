using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Share.Signal;

namespace Share.Signal;

/// <summary>
/// 信号观测器（SCF SC-I4/SC-I8）：按作用域维护**终态增量**，是轮循环推进判据的唯一输入。
/// <para>写者 = <see cref="SignalIntake"/>（状态迁移时）；读者 = <c>AgentRunRegionCircuit</c> / 治理层。
/// 投递次数/目标集指纹不进入本计数。</para>
/// </summary>
public sealed class SignalObserver
{
    private sealed class Counter
    {
        public readonly object Gate = new();
        public readonly Dictionary<string, SignalState> States = new(StringComparer.Ordinal);
        public int Terminal;
        public int Completed;
        public int Failed;
        public int Cancelled;
        public int InFlight;
        public int Dispatched;
        public int Duplicate;
    }

    private readonly ConcurrentDictionary<string, Counter> _scopes = new(StringComparer.Ordinal);

    private Counter For(string scope) => _scopes.GetOrAdd(scope ?? string.Empty, _ => new Counter());

    /// <summary>登记一次状态（幂等：同一 key 反复登记只按最新态计一次）。</summary>
    public void RecordState(string? scope, SignalKey key, SignalState state, bool duplicate = false)
    {
        var counter = For(scope ?? key.Scope);
        lock (counter.Gate)
        {
            var id = key.Value;
            var hadPrevious = counter.States.TryGetValue(id, out var previous);

            if (hadPrevious && IsInFlight(previous)) counter.InFlight--;
            if (hadPrevious && IsTerminal(previous)) DecrementTerminal(counter, previous);
            if (hadPrevious && previous == SignalState.Failed) counter.Failed = Math.Max(0, counter.Failed - 1);

            if (IsInFlight(state)) counter.InFlight++;
            if (IsTerminal(state))
            {
                counter.Terminal++;
                if (state == SignalState.Completed) counter.Completed++;
                else if (state == SignalState.Cancelled) counter.Cancelled++;
            }
            // Failed 可重试（非终态），但作为失败增量单独计数（推进判据 terminal=failed 的唯一来源）。
            if (state == SignalState.Failed) counter.Failed++;

            if (state == SignalState.Dispatched && (!hadPrevious || previous != SignalState.Dispatched))
                counter.Dispatched++;
            if (duplicate) counter.Duplicate++;

            counter.States[id] = state;
        }
    }

    /// <summary>登记去重命中（不改变状态）。</summary>
    public void RecordDuplicate(string? scope, SignalKey key)
    {
        var counter = For(scope ?? key.Scope);
        lock (counter.Gate) counter.Duplicate++;
    }

    /// <summary>取作用域增量快照。</summary>
    public SignalDelta Snapshot(string? scope)
    {
        if (!_scopes.TryGetValue(scope ?? string.Empty, out var counter)) return SignalDelta.Empty;
        lock (counter.Gate)
        {
            return new SignalDelta(
                counter.Terminal,
                counter.Completed,
                counter.Failed,
                counter.Cancelled,
                counter.InFlight,
                counter.Dispatched,
                counter.Duplicate);
        }
    }

    /// <summary>清空作用域（会话结束/容器关闭）。</summary>
    public void Reset(string? scope) => _scopes.TryRemove(scope ?? string.Empty, out _);

    private static bool IsInFlight(SignalState state)
        => state is SignalState.Dispatched or SignalState.Claimed or SignalState.Running;

    private static bool IsTerminal(SignalState state)
        => state is SignalState.Completed or SignalState.Cancelled or SignalState.Expired or SignalState.DeadLetter;

    private static void DecrementTerminal(Counter counter, SignalState state)
    {
        counter.Terminal = Math.Max(0, counter.Terminal - 1);
        if (state == SignalState.Completed) counter.Completed = Math.Max(0, counter.Completed - 1);
        else if (state == SignalState.Cancelled) counter.Cancelled = Math.Max(0, counter.Cancelled - 1);
    }
}
