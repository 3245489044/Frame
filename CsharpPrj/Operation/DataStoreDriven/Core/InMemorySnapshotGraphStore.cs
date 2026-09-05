using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 内存快照图存储（<see cref="ISnapshotGraphStore"/>）：ur_snapshots + ur_nodes + ur_references 的进程内承载；
/// 非 Sqlite 后端 / 测试使用，进程重启即失。线程安全（lock）。
/// </summary>
public sealed class InMemorySnapshotGraphStore : ISnapshotGraphStore
{
    private sealed record SnapshotEntry(long Id, string ThreadId, string? SessionId, string Json);

    private readonly InMemoryNodeStore _nodes;
    private readonly List<SnapshotEntry> _snapshots = new();
    private readonly List<NodeReference> _refs = new();
    private readonly object _lock = new();
    private long _refSeq;
    private long _snapshotSeq;

    public InMemorySnapshotGraphStore(InMemoryNodeStore? nodes = null)
    {
        _nodes = nodes ?? new InMemoryNodeStore();
    }

    /// <summary>底层节点存储（供外部复用同一节点集合）。</summary>
    public InMemoryNodeStore Nodes => _nodes;

    public Task InitializeAsync(CancellationToken ct = default) => _nodes.InitializeAsync(ct);

    public Task<long> SaveSnapshotAsync(string threadId, string? sessionId, string snapshotJson, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var idx = _snapshots.FindIndex(s => s.ThreadId == threadId && s.SessionId == sessionId);
            if (idx >= 0)
            {
                // 幂等覆写：同 (threadId, sessionId) 更新载荷、返回原 id（不重分配，避免"覆写即换 id"的游标漂移）。
                _snapshots[idx] = _snapshots[idx] with { Json = snapshotJson };
                return Task.FromResult(_snapshots[idx].Id);
            }
            var id = IdGenerator.Next(ref _snapshotSeq);
            _snapshots.Add(new SnapshotEntry(id, threadId, sessionId, snapshotJson));
            return Task.FromResult(id);
        }
    }

    public Task<string?> LoadSnapshotAsync(string threadId, string? sessionId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var snap = _snapshots.FirstOrDefault(s => s.ThreadId == threadId && s.SessionId == sessionId);
            return Task.FromResult<string?>(snap?.Json);
        }
    }

    public Task AttachNodeAsync(string threadId, string? sessionId, string nodeKey, string? refType = null,
        double strength = 1.0, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var key = PersistenceHelper.SnapshotKey(threadId, sessionId);
            var rt = string.IsNullOrWhiteSpace(refType) ? "belongs" : refType;
            _refs.RemoveAll(r => r.SourceId == key && r.TargetId == nodeKey && r.RefType == rt);
            _refs.Add(new NodeReference
            {
                Id = IdGenerator.Next(ref _refSeq),
                SourceType = SnapshotGraphStoreTypes.Snapshot,
                SourceId = key,
                TargetType = SnapshotGraphStoreTypes.Node,
                TargetId = nodeKey,
                RefType = rt,
                Strength = strength,
            });
        }
        return Task.CompletedTask;
    }

    public async Task<SnapshotGraph> GetGraphAsync(string threadId, string? sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string key;
        string? snapJson;
        List<string> nodeKeys;
        lock (_lock)
        {
            key = PersistenceHelper.SnapshotKey(threadId, sessionId);
            snapJson = _snapshots.FirstOrDefault(s => s.ThreadId == threadId && s.SessionId == sessionId)?.Json;
            nodeKeys = GetAttachedNodeKeys(key);
        }
        var nodes = (await _nodes.ListAsync(null, threadId, null, int.MaxValue, ct).ConfigureAwait(false))
            .Where(n => nodeKeys.Contains(n.NodeKey))
            .OrderBy(n => n.Id)
            .ToList();
        return new SnapshotGraph { SnapshotKey = key, SnapshotJson = snapJson, Nodes = nodes };
    }

    public async Task<IReadOnlyList<string>> DeleteGraphAsync(string threadId, string? sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        List<string> nodeKeys;
        lock (_lock)
        {
            var key = PersistenceHelper.SnapshotKey(threadId, sessionId);
            nodeKeys = GetAttachedNodeKeys(key);
            _refs.RemoveAll(r => r.SourceId == key);
            _snapshots.RemoveAll(s => s.ThreadId == threadId && s.SessionId == sessionId);
        }
        // 锁外逐节点级联删除（避免持图锁同步等待节点库锁）。删除前做共享引用检查：当前快照的引用已移除，
        // 若该节点仍被其他快照（或其他引用）指向，则属共享节点，跳过删除。
        foreach (var k in nodeKeys)
        {
            bool shared;
            lock (_lock) { shared = _refs.Any(r => r.TargetId == k); }
            if (shared) continue;
            await _nodes.DeleteAsync(k, ct).ConfigureAwait(false);
        }
        return nodeKeys;
    }

    /// <summary>快照根下挂载的节点键（去重；GetGraph/DeleteGraph 共用，消除重复 LINQ）。</summary>
    private List<string> GetAttachedNodeKeys(string key) => _refs
        .Where(r => r.SourceId == key && r.TargetType == SnapshotGraphStoreTypes.Node)
        .Select(r => r.TargetId)
        .Distinct()
        .ToList();
}
