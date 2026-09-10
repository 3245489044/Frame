using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.StorageFramework.Core;

/// <summary>
/// 内存 checkpoint 存储占位实现（<see cref="ICheckpointStore"/>）：ur_checkpoints 的进程内承载；
/// 供无原生 checkpoint 的后端（Graph/Local）与测试使用，进程重启即失。
/// 线程安全（ConcurrentDictionary），同 key 幂等覆写。
/// </summary>
public sealed class InMemoryCheckpointStore : ICheckpointStore
{
    private sealed record Entry(Checkpoint Cp, long Seq);

    private readonly ConcurrentDictionary<string, Entry> _store = new();
    private long _seq;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<Checkpoint?> GetAsync(string threadId, string? checkpointNs = null, string? checkpointId = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(checkpointId))
        {
            _store.TryGetValue(PersistenceHelper.CheckpointKey(threadId, checkpointNs, checkpointId), out var exact);
            return Task.FromResult(exact?.Cp);
        }

        // 确定性"最新"选择（对齐 Sqlite 的 ORDER BY [Step] DESC, [Id] DESC，[Id]=写入序）：Step 最大，同 Step 取最后写入。
        var ns = checkpointNs ?? string.Empty;
        Entry? latest = null;
        foreach (var e in _store.Values)
        {
            if (!string.Equals(e.Cp.ThreadId, threadId, StringComparison.Ordinal)) continue;
            if (!string.Equals(e.Cp.CheckpointNs ?? string.Empty, ns, StringComparison.Ordinal)) continue;
            if (latest is null
                || e.Cp.Step > latest.Cp.Step
                || (e.Cp.Step == latest.Cp.Step && e.Seq > latest.Seq))
                latest = e;
        }
        return Task.FromResult(latest?.Cp);
    }

    public Task<string> PutAsync(Checkpoint checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var id = PersistenceHelper.ResolveCheckpointId(checkpoint.CheckpointId);
        checkpoint.CheckpointId = id;
        _store[PersistenceHelper.CheckpointKey(checkpoint.ThreadId, checkpoint.CheckpointNs, id)] = new Entry(checkpoint, Interlocked.Increment(ref _seq));
        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<string>> ListAsync(string threadId, string? checkpointNs = null, CancellationToken ct = default)
    {
        var ns = checkpointNs ?? string.Empty;
        var ids = _store.Values
            .Where(e => string.Equals(e.Cp.ThreadId, threadId, StringComparison.Ordinal)
                    && string.Equals(e.Cp.CheckpointNs ?? string.Empty, ns, StringComparison.Ordinal))
            .OrderBy(e => e.Cp.Step)
            .ThenBy(e => e.Seq)
            .Select(e => e.Cp.CheckpointId)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(ids);
    }
}
