using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 一致性守护唯一实现（三入口、一实现；Agent 无关）：
/// <list type="bullet">
/// <item><see cref="LightCheckAsync"/>：启动一次 —— Outbox 重放 + Pending/Abandoned 计数（不扫全表）。</item>
/// <item><see cref="ReconcileAllAsync"/>：批次后/手动 —— 复用引用守卫全量对账（差异计数可复算）。</item>
/// <item><see cref="ReplayPendingAsync"/>：失败后/定时 —— Outbox 逐条重放（幂等）。</item>
/// </list>
/// </summary>
public sealed class StorageConsistencyGuard : IStorageConsistencyGuard
{
    private readonly ReferenceGrounding _grounding;
    private readonly IOutboxStore _outbox;
    private readonly ICompositeWriteUnit _writeUnit;

    public StorageConsistencyGuard(ReferenceGrounding grounding, IOutboxStore outbox, ICompositeWriteUnit writeUnit)
    {
        _grounding = grounding ?? throw new ArgumentNullException(nameof(grounding));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _writeUnit = writeUnit ?? throw new ArgumentNullException(nameof(writeUnit));
    }

    /// <inheritdoc />
    public async Task<ConsistencyReport> LightCheckAsync(CancellationToken ct = default)
    {
        var before = await _outbox.CountAsync(ct).ConfigureAwait(false);
        var replayed = await _writeUnit.DrainOutboxAsync(100, ct).ConfigureAwait(false);
        var after = await _outbox.CountAsync(ct).ConfigureAwait(false);
        var notes = new List<string> { $"replayed={replayed}" };
        var report = new ConsistencyReport("LightCheck", before.Pending, after.Pending, after.Abandoned, 0, 0, notes);
        StorageConsistencyEvents.Report(StorageConsistencyEvents.ReconcilePass, new
        {
            check = report.Check,
            pending = report.PendingAfter,
            abandoned = report.Abandoned,
            replayed,
        });
        return report;
    }

    /// <inheritdoc />
    public async Task<ConsistencyReport> ReconcileAllAsync(CancellationToken ct = default)
    {
        var before = await _outbox.CountAsync(ct).ConfigureAwait(false);
        var diff = await _grounding.GetDiffAsync(ct).ConfigureAwait(false);
        var after = await _outbox.CountAsync(ct).ConfigureAwait(false);
        var notes = new List<string>(diff.DanglingNodeResources.Count)
        {
            $"dangling={diff.DanglingCount}",
        };
        foreach (var id in diff.DanglingNodeResources) notes.Add($"dangling:{id}");
        var report = new ConsistencyReport("ReconcileAll", before.Pending, after.Pending, after.Abandoned,
            diff.DanglingCount, diff.ReconciledNodes, notes);
        StorageConsistencyEvents.Report(StorageConsistencyEvents.ReconcilePass, new
        {
            check = report.Check,
            dangling = report.Dangling,
            reconciled = report.Reconciled,
            pending = report.PendingAfter,
        });
        return report;
    }

    /// <inheritdoc />
    public async Task<ConsistencyReport> ReplayPendingAsync(CancellationToken ct = default)
    {
        var before = await _outbox.CountAsync(ct).ConfigureAwait(false);
        var replayed = await _writeUnit.DrainOutboxAsync(100, ct).ConfigureAwait(false);
        var after = await _outbox.CountAsync(ct).ConfigureAwait(false);
        var report = new ConsistencyReport("ReplayPending", before.Pending, after.Pending, after.Abandoned, 0, 0,
            new[] { $"replayed={replayed}" });
        StorageConsistencyEvents.Report(StorageConsistencyEvents.ReconcilePass, new
        {
            check = report.Check,
            pending = report.PendingAfter,
            abandoned = report.Abandoned,
            replayed,
        });
        return report;
    }
}
