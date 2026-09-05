using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.StorageFramework.Core;

/// <summary>
/// 内存快照存储占位实现（<see cref="ISnapshotStore"/>）：ur_snapshots 的进程内承载；
/// 供无原生快照的后端（Graph/Local）与测试使用，进程重启即失。
/// 线程安全（ConcurrentDictionary），同 key 幂等覆写。
/// </summary>
public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private sealed record Entry(string ThreadId, string Json, string CreatedAt);

    private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.Ordinal);

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SaveAsync(string threadId, string? sessionId, string snapshotJson, CancellationToken ct = default)
    {
        _store[PersistenceHelper.SnapshotKey(threadId, sessionId)] = new Entry(threadId, snapshotJson, PersistenceHelper.NowIso());
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string threadId, string? sessionId, CancellationToken ct = default)
    {
        // 与 Sqlite/LiteDB 后端一致：有 sessionId → 精确读取；无 sessionId → 取该 ThreadId 最新快照（按落库时间 CreatedAt 倒序，ISO-8601 字典序即时间序）。
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _store.TryGetValue(PersistenceHelper.SnapshotKey(threadId, sessionId), out var exact);
            return Task.FromResult(exact?.Json);
        }

        Entry? latest = null;
        foreach (var e in _store.Values)
        {
            if (!string.Equals(e.ThreadId, threadId, StringComparison.Ordinal)) continue;
            if (latest is null || string.CompareOrdinal(e.CreatedAt, latest.CreatedAt) > 0) latest = e;
        }
        return Task.FromResult(latest?.Json);
    }
}
