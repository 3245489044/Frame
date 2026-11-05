using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Share.Signal;

namespace Share.Signal;

/// <summary>
/// 信号观测器（SCF SC-I4/SC-I8）：按作用域维护**本轮终态增量**，是轮循环推进判据的唯一输入。
/// <para>写者 = <see cref="SignalIntake"/>（状态迁移时）；读者 = <c>AgentRunRegionCircuit</c> / 治理层。
/// <see cref="Snapshot"/> 读取即清空增量（<c>InFlight</c> 除外，保留为当前在途数）；投递次数/目标集指纹不进入本计数。</para>
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

    private Counter For(string scope) => _scopes.GetOrAdd(scope, _ => new Counter());

    /// <summary>登记一次状态（幂等：同一 key 反复登记只按最新态计一次）。</summary>
    public void RecordState(string? scope, SignalKey key, SignalState state, bool duplicate = false)
    {
        // 写桶规则：scope 非空白 ⇒ 用 scope；否则回退 key.Scope（与读侧一致）。
        var counter = For(string.IsNullOrWhiteSpace(scope) ? key.Scope : scope!);
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
        var counter = For(string.IsNullOrWhiteSpace(scope) ? key.Scope : scope!);
        lock (counter.Gate) counter.Duplicate++;
    }

    /// <summary>
    /// 取本轮增量快照（<b>读取即清空</b>）：Terminal/Completed/Failed/Cancelled/Dispatched/Duplicate
    /// 在返回后归零，<see cref="SignalDelta.InFlight"/> 为当前在途数、不清零。
    /// <para><paramref name="scope"/> 为 null/空白 ⇒ 全局聚合：返回所有桶求和并统一清空增量。</para>
    /// </summary>
    public SignalDelta Snapshot(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            var terminal = 0;
            var completed = 0;
            var failed = 0;
            var cancelled = 0;
            var inFlight = 0;
            var dispatched = 0;
            var duplicates = 0;
            foreach (var counter in _scopes.Values)
            {
                lock (counter.Gate)
                {
                    terminal += counter.Terminal;
                    completed += counter.Completed;
                    failed += counter.Failed;
                    cancelled += counter.Cancelled;
                    inFlight += counter.InFlight;
                    dispatched += counter.Dispatched;
                    duplicates += counter.Duplicate;
                    ResetIncrements(counter);
                }
            }
            return new SignalDelta(terminal, completed, failed, cancelled, inFlight, dispatched, duplicates);
        }

        if (!_scopes.TryGetValue(scope!, out var scoped)) return SignalDelta.Empty;
        lock (scoped.Gate)
        {
            var delta = new SignalDelta(
                scoped.Terminal,
                scoped.Completed,
                scoped.Failed,
                scoped.Cancelled,
                scoped.InFlight,
                scoped.Dispatched,
                scoped.Duplicate);
            ResetIncrements(scoped);
            return delta;
        }
    }

    /// <summary>清空作用域增量；null/空白 ⇒ 清空全部桶。</summary>
    public void Reset(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            _scopes.Clear();
            return;
        }
        _scopes.TryRemove(scope!, out _);
    }

    private static void ResetIncrements(Counter counter)
    {
        counter.Terminal = 0;
        counter.Completed = 0;
        counter.Failed = 0;
        counter.Cancelled = 0;
        counter.Dispatched = 0;
        counter.Duplicate = 0;
        // InFlight 不清零：保留为当前在途数。
    }

    // 终态/在途判定统一复用 Modules.Frameworks.Signal 的权威实现（SignalStateNames），不再维护私有副本。
    private static bool IsInFlight(SignalState state) => SignalStateNames.IsInFlight(state);

    private static bool IsTerminal(SignalState state) => SignalStateNames.IsTerminal(state);

    private static void DecrementTerminal(Counter counter, SignalState state)
    {
        counter.Terminal = Math.Max(0, counter.Terminal - 1);
        if (state == SignalState.Completed) counter.Completed = Math.Max(0, counter.Completed - 1);
        else if (state == SignalState.Cancelled) counter.Cancelled = Math.Max(0, counter.Cancelled - 1);
    }
}
