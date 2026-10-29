using System;
using System.Collections.Generic;

namespace UniversalWorkflowFramework.Relations.Mounting;

/// <summary>
/// 触发仲裁（设计单 §7.2）：同一 <c>(MountId, Scope, 窗口)</c> 只允许最高优先触发写边。
/// <para>
/// 优先级：<c>rebuild &gt; command &gt; round.settled &gt; live</c>；显式来源覆盖自动来源，外层批量覆盖内层逐点。
/// </para>
/// <para>
/// **窗口**：仲裁键必须带关联 id（轮次 / 命令 id），否则第一轮的 round.settled 会永久抑制后续轮次的 live。
/// 未提供关联 id 时以 <c>default</c> 作为窗口，仅适用于单次语义动作。
/// </para>
/// </summary>
public sealed class TriggerArbiter
{
    private const int PruneThreshold = 4096;
    private static readonly TimeSpan RetainFor = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _winners = new(StringComparer.Ordinal);

    /// <summary>
    /// 尝试取得写权。返回 false ⇒ 本次触发被更高优先级触发抑制（合法 no-op）。
    /// </summary>
    /// <param name="mountId">挂载语义 id。</param>
    /// <param name="scope">作用域（仲裁窗口来自 <see cref="MountScope.ArbitrationKey"/>）。</param>
    /// <param name="trigger">本次触发事件 id。</param>
    /// <param name="winner">out：当前窗口的胜出触发。</param>
    public bool TryAcquire(string mountId, MountScope scope, string trigger, out string? winner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountId);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);

        var key = mountId + "#" + scope.ArbitrationKey;
        var priority = PriorityOf(trigger);

        lock (_gate)
        {
            PruneIfNeeded();

            if (!_winners.TryGetValue(key, out var current))
            {
                _winners[key] = new Entry(priority, trigger, DateTime.UtcNow);
                winner = trigger;
                return true;
            }

            winner = current.Trigger;
            if (string.Equals(current.Trigger, trigger, StringComparison.Ordinal))
            {
                _winners[key] = current with { AtUtc = DateTime.UtcNow };
                return true;
            }

            if (priority <= current.Priority) return false;

            _winners[key] = new Entry(priority, trigger, DateTime.UtcNow);
            winner = trigger;
            return true;
        }
    }

    /// <summary>显式重置一个窗口（测试 / 重跑 / 对账）。</summary>
    public void Reset(string mountId, MountScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountId);
        ArgumentNullException.ThrowIfNull(scope);
        lock (_gate)
        {
            _winners.Remove(mountId + "#" + scope.ArbitrationKey);
        }
    }

    /// <summary>清空全部窗口（进程级重装配）。</summary>
    public void ResetAll()
    {
        lock (_gate)
        {
            _winners.Clear();
        }
    }

    /// <summary>触发事件 → 优先级（未声明事件按最低处理）。</summary>
    public static int PriorityOf(string trigger) => trigger switch
    {
        MountTriggers.RebuildRequested => 4,
        MountTriggers.CommandExplicit => 3,
        MountTriggers.TaskCreated => 2,
        MountTriggers.RoundSettled => 2,
        MountTriggers.NodePersisted => 1,
        _ => 1,
    };

    private void PruneIfNeeded()
    {
        if (_winners.Count <= PruneThreshold) return;

        var cutoff = DateTime.UtcNow - RetainFor;
        var stale = new List<string>();
        foreach (var pair in _winners)
        {
            if (pair.Value.AtUtc < cutoff) stale.Add(pair.Key);
        }

        foreach (var key in stale) _winners.Remove(key);
        if (_winners.Count <= PruneThreshold) return;

        // 仍然超限：按时间淘汰最旧一半（有界内存；不影响当前窗口语义）。
        var overflow = _winners.Count - PruneThreshold / 2;
        foreach (var key in new List<string>(_winners.Keys).GetRange(0, Math.Min(overflow, _winners.Count)))
            _winners.Remove(key);
    }

    private sealed record Entry(int Priority, string Trigger, DateTime AtUtc);
}
