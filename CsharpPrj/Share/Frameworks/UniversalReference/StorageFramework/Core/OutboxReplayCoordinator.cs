using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.StorageFramework.Core;

/// <summary>重放计划：当前条目能否重放；不可重放时必须给出可见化原因（禁静默）。</summary>
public readonly record struct OutboxReplayPlan(bool CanReplay, Func<CancellationToken, Task>? Replay, string? Reason);

/// <summary>一次 Outbox 重放批次的结果（可见化计数）。</summary>
public readonly record struct OutboxReplaySummary(int Applied, int Rescheduled, int Abandoned, int Faults);

/// <summary>
/// Outbox 重放存储端口（Share A-1 单源）：只暴露重放循环所需的最小能力，
/// 由各存储实现适配（LiteDB/SQL/文件），重放顺序与失败隔离不在各实现内重复。
/// </summary>
public interface IOutboxReplayStore<out TEntry>
{
    /// <summary>取待重放条目（按实现定义的排序/上限）。</summary>
    Task<IReadOnlyList<TEntry>> ListPendingAsync(int maxItems, CancellationToken cancellationToken);

    string GetKey(TEntry entry);

    string GetUnitName(TEntry entry);

    int GetAttempts(TEntry entry);

    Task MarkAppliedAsync(string key, CancellationToken cancellationToken);

    Task MarkAbandonedAsync(string key, string reason, CancellationToken cancellationToken);

    /// <summary>重放失败：累加尝试次数并保留 Pending（下一次批次继续）。</summary>
    Task RescheduleAsync(TEntry entry, string error, CancellationToken cancellationToken);
}

/// <summary>
/// Outbox 重放协调器（Share A-1 单源）：统一「取待放 → 尝试上限 → 无工厂/空单元可见化放弃 →
/// 逐条重放 → 成功标记 / 失败重排」，并保证单条存储操作抛错不中断整批。
/// <para>
/// 职责边界：本类不解释业务 payload、不持有存储；条目形态由 <see cref="IOutboxReplayStore{TEntry}"/> 适配。
/// </para>
/// </summary>
public sealed class OutboxReplayCoordinator<TEntry>
{
    private readonly int _maxAttempts;

    /// <param name="maxAttempts">单条目累计尝试上限（≤0 表示不设上限）；达到上限 → Abandoned（可见化）。</param>
    public OutboxReplayCoordinator(int maxAttempts = 5)
    {
        _maxAttempts = maxAttempts;
    }

    public async Task<OutboxReplaySummary> DrainAsync(
        IOutboxReplayStore<TEntry> store,
        Func<TEntry, OutboxReplayPlan> resolve,
        int maxItems = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(resolve);

        var pending = await store.ListPendingAsync(maxItems, cancellationToken).ConfigureAwait(false);

        int applied = 0, rescheduled = 0, abandoned = 0, faults = 0;

        foreach (var entry in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = store.GetKey(entry);

            if (_maxAttempts > 0 && store.GetAttempts(entry) >= _maxAttempts)
            {
                if (await TryMarkAbandonedAsync(store, key, "attempts-exceeded", cancellationToken).ConfigureAwait(false))
                    abandoned++;
                else
                    faults++;
                continue;
            }

            var plan = resolve(entry);
            if (!plan.CanReplay || plan.Replay is null)
            {
                if (await TryMarkAbandonedAsync(store, key, plan.Reason ?? "no-replay-plan", cancellationToken).ConfigureAwait(false))
                    abandoned++;
                else
                    faults++;
                continue;
            }

            try
            {
                await plan.Replay(cancellationToken).ConfigureAwait(false);
                await store.MarkAppliedAsync(key, cancellationToken).ConfigureAwait(false);
                applied++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单条重放失败：记录并重排，继续处理同批其余条目（不中断整批）。
                try
                {
                    await store.RescheduleAsync(entry, $"replay:{ex.GetType().Name}: {ex.Message}", cancellationToken).ConfigureAwait(false);
                    rescheduled++;
                }
                catch (Exception rescheduleEx)
                {
                    faults++;
                    _ = rescheduleEx;
                }
            }
        }

        return new OutboxReplaySummary(applied, rescheduled, abandoned, faults);
    }

    private static async Task<bool> TryMarkAbandonedAsync(
        IOutboxReplayStore<TEntry> store,
        string key,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.MarkAbandonedAsync(key, reason, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>一致性巡检快照（Pending 前后 + Abandoned + 本次重放成功数）。</summary>
public readonly record struct ConsistencyPulseSnapshot(
    int PendingBefore,
    int PendingAfter,
    int Abandoned,
    int Replayed);

/// <summary>一致性巡检端口：只提供「计数 + 重放」两能力，供启动/定时/失败后三种入口共用。</summary>
public interface IConsistencyPulsePort
{
    Task<(int Pending, int Abandoned)> CountAsync(CancellationToken cancellationToken);

    Task<int> ReplayAsync(int maxItems, CancellationToken cancellationToken);
}

/// <summary>
/// 一致性巡检（Share A-2 单源）：把「计数 → 重放 → 计数」的重复采样序列收敛为一份实现；
/// 三入口差异（全量对账等）仍留在各调用方，本类只负责 Outbox 巡检脉冲。
/// </summary>
public static class StorageConsistencyPulse
{
    public static async Task<ConsistencyPulseSnapshot> RunAsync(
        IConsistencyPulsePort port,
        int maxItems = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(port);

        var (pendingBefore, abandonedBefore) = await port.CountAsync(cancellationToken).ConfigureAwait(false);
        var replayed = await port.ReplayAsync(maxItems, cancellationToken).ConfigureAwait(false);
        var (pendingAfter, abandonedAfter) = await port.CountAsync(cancellationToken).ConfigureAwait(false);

        _ = abandonedBefore; // 报告以「巡检后」Abandoned 为准，before.Abondoned 不参与结论
        return new ConsistencyPulseSnapshot(pendingBefore, pendingAfter, abandonedAfter, replayed);
    }
}
