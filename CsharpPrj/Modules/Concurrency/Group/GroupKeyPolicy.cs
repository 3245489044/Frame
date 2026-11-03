using System;
using System.Collections.Concurrent;

namespace Modules.Concurrency;

/// <summary>默认组键策略：{scope}:{planKey}:g{attempt}。
/// <para>同一策略实例是**序号唯一所有者**：同输入同 scope 的重复分发由本类递增序号，
/// 得到不同组键（否则第二次分发被「组已存在」误判）。显式 <paramref name="attempt"/> &gt; 0 时以调用方为准。</para>
/// <para>序号账有界（FIFO 淘汰），长驻宿主不随分发次数无限增长。</para></summary>
public sealed class PlanScopedGroupKeyPolicy : IGroupKeyPolicy
{
    private const int MaxTrackedKeys = 8192;
    private readonly ConcurrentDictionary<string, int> _sequence = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();

    public string For(string scopeKey, SplitPlan plan, int attempt = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        ArgumentNullException.ThrowIfNull(plan);

        var sequenceKey = scopeKey + "|" + plan.PlanKey;
        var index = attempt > 0
            ? attempt
            : _sequence.AddOrUpdate(sequenceKey, 0, static (_, current) => current + 1);

        if (attempt <= 0)
        {
            _order.Enqueue(sequenceKey);
            // 有界：超限时淘汰最旧键（重新从 0 计数；键本身含 planKey，漂移面可忽略）。
            while (_order.Count > MaxTrackedKeys && _order.TryDequeue(out var oldest))
            {
                _sequence.TryRemove(oldest, out _);
            }
        }

        return $"{scopeKey}:{plan.PlanKey}:g{Math.Max(0, index)}";
    }
}
