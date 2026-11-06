using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 跨库写单元唯一实现（双库通则，Agent 无关）：
/// <code>
/// unit.Sql()  ──失败──▶ 立即返回失败（不写图、不入 Outbox）
///      │成功
///      ▼
/// unit.Graph() ──失败──▶ Outbox.Put(unit) + StorageConsistencyEvent(graph-pending) + 返回 SqlCommitted=true
///      │成功
///      ▼
///      返回 SqlCommitted=true, GraphApplied=true
/// </code>
/// <para>不引入分布式事务：接受「短暂不一致 + 可重建」（C6）。</para>
/// </summary>
public sealed class CompositeWriteUnit : ICompositeWriteUnit
{
    private readonly IOutboxStore _outbox;
    private readonly ConcurrentDictionary<string, Func<OutboxEntryFactory, StorageWriteUnit?>> _replay =
        new(StringComparer.Ordinal);

    public CompositeWriteUnit(IOutboxStore outbox)
        => _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));

    /// <inheritdoc />
    public async Task<WriteUnitResult> ExecuteAsync(StorageWriteUnit unit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(unit.Sql);

        // ① 权威写：失败即真失败（不入 Outbox，不写图）。
        try
        {
            await unit.Sql(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new WriteUnitResult(unit.Name, SqlCommitted: false, GraphApplied: false,
                OutboxKey: null, new[] { $"sql:{ex.GetType().Name}: {ex.Message}" });
        }

        // ② 派生写：可空（无图侧动作）。
        if (unit.Graph is null)
            return new WriteUnitResult(unit.Name, SqlCommitted: true, GraphApplied: true,
                OutboxKey: null, Array.Empty<string>());

        try
        {
            await unit.Graph(ct).ConfigureAwait(false);
            return new WriteUnitResult(unit.Name, SqlCommitted: true, GraphApplied: true,
                OutboxKey: null, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            // ③ 图失败：不回收 SQL 结果；入 Outbox（幂等键覆盖）+ 可见化。
            var entry = new OutboxEntry(
                unit.IdempotencyKey,
                unit.Name,
                unit.PayloadJson,
                Attempts: 0,
                LastError: $"graph:{ex.GetType().Name}: {ex.Message}",
                State: OutboxStates.Pending,
                FirstFailedAt: DateTimeOffset.UtcNow.ToString("o"));
            try
            {
                await _outbox.UpsertPendingAsync(entry, ct).ConfigureAwait(false);
            }
            catch (Exception outboxEx)
            {
                // Outbox 也不可用 ⇒ 事实必须可见（禁静默），但仍不回收 SQL 结果。
                StorageConsistencyEvents.Report(StorageConsistencyEvents.OutboxAbandoned, new
                {
                    unit = unit.Name,
                    key = unit.IdempotencyKey,
                    error = outboxEx.Message,
                });
                return new WriteUnitResult(unit.Name, SqlCommitted: true, GraphApplied: false,
                    OutboxKey: null, new[] { $"graph:{ex.GetType().Name}: {ex.Message}", $"outbox:{outboxEx.GetType().Name}: {outboxEx.Message}" });
            }

            StorageConsistencyEvents.Report(StorageConsistencyEvents.GraphPending, new
            {
                unit = unit.Name,
                key = unit.IdempotencyKey,
                error = ex.Message,
            });
            return new WriteUnitResult(unit.Name, SqlCommitted: true, GraphApplied: false,
                OutboxKey: unit.IdempotencyKey, new[] { $"graph:{ex.GetType().Name}: {ex.Message}" });
        }
    }

    /// <inheritdoc />
    public void RegisterReplay(string unitName, Func<OutboxEntryFactory, StorageWriteUnit?> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitName);
        ArgumentNullException.ThrowIfNull(factory);
        _replay[unitName] = factory;
    }

    /// <inheritdoc />
    public async Task<int> DrainOutboxAsync(int maxItems = 100, CancellationToken ct = default)
    {
        // 重放循环/尝试上限/失败隔离统一走 Share.OutboxReplayCoordinator（A-1 单源）。
        var coordinator = new OutboxReplayCoordinator<OutboxEntry>(MaxReplayAttempts);
        var summary = await coordinator.DrainAsync(
            new OutboxReplayStoreAdapter(_outbox),
            ResolveReplayPlan,
            maxItems,
            ct).ConfigureAwait(false);

        if (summary.Abandoned > 0 || summary.Faults > 0)
        {
            StorageConsistencyEvents.Report(StorageConsistencyEvents.OutboxAbandoned, new
            {
                abandoned = summary.Abandoned,
                faults = summary.Faults,
                rescheduled = summary.Rescheduled,
            });
        }

        if (summary.Applied > 0)
        {
            StorageConsistencyEvents.Report(StorageConsistencyEvents.GraphReplayed, new { applied = summary.Applied });
        }

        return summary.Applied;
    }

    /// <summary>单条目累计尝试上限（达到即 Abandoned，保证不会无限重排；F4.7）。</summary>
    private const int MaxReplayAttempts = 5;

    private OutboxReplayPlan ResolveReplayPlan(OutboxEntry entry)
    {
        if (!_replay.TryGetValue(entry.UnitName, out var factory))
            return new OutboxReplayPlan(false, null, "no-replay-factory");

        var unit = factory(new OutboxEntryFactory(entry.PayloadJson, entry.IdempotencyKey));
        return unit?.Graph is null
            ? new OutboxReplayPlan(false, null, "replay-factory-returned-null")
            : new OutboxReplayPlan(true, unit.Graph, null);
    }

    /// <summary>把工程内 IOutboxStore 适配为 Share 重放端口（存储实现差异收敛在此）。</summary>
    private sealed class OutboxReplayStoreAdapter : IOutboxReplayStore<OutboxEntry>
    {
        private readonly IOutboxStore _store;

        public OutboxReplayStoreAdapter(IOutboxStore store) => _store = store;

        public Task<IReadOnlyList<OutboxEntry>> ListPendingAsync(int maxItems, CancellationToken cancellationToken)
            => _store.ListPendingAsync(maxItems, cancellationToken);

        public string GetKey(OutboxEntry entry) => entry.IdempotencyKey;

        public string GetUnitName(OutboxEntry entry) => entry.UnitName;

        public int GetAttempts(OutboxEntry entry) => entry.Attempts;

        public Task MarkAppliedAsync(string key, CancellationToken cancellationToken)
            => _store.MarkAppliedAsync(key, cancellationToken);

        public Task MarkAbandonedAsync(string key, string reason, CancellationToken cancellationToken)
            => _store.MarkAbandonedAsync(key, reason, cancellationToken);

        public Task RescheduleAsync(OutboxEntry entry, string error, CancellationToken cancellationToken)
            => _store.UpsertPendingAsync(entry with
            {
                Attempts = entry.Attempts + 1,
                LastError = error,
                State = OutboxStates.Pending,
            }, cancellationToken);
    }
}
