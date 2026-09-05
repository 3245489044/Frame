using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 内存标准化节点数据存储（<see cref="INodeStore"/>）：ur_nodes 的进程内承载；非 Sqlite 后端与测试使用，进程重启即失。
/// 线程安全（lock），NodeKey 幂等覆写，Id 自增。
/// </summary>
public sealed class InMemoryNodeStore : INodeStore
{
    private readonly List<NodeRecord> _nodes = new();
    private readonly object _lock = new();
    private long _seq;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<long> PutAsync(NodeRecord node, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            var existing = _nodes.FirstOrDefault(n => string.Equals(n.NodeKey, node.NodeKey, StringComparison.Ordinal));
            if (existing is not null)
            {
                // 完整合并（含 Key/Status）：NodeKey 幂等覆写须与新增分支全字段对齐，避免覆写残留旧值（NodeConfig/Checkpoint 等以确定性 NodeKey 覆写的场景）。
                existing.NodeType = node.NodeType;
                existing.ThreadId = node.ThreadId;
                existing.Key = node.Key;
                existing.Status = node.Status;
                existing.PayloadJson = node.PayloadJson ?? "{}";
                existing.RelationsJson = node.RelationsJson;
                existing.UpdatedAt = PersistenceHelper.NowIso();
                return Task.FromResult(existing.Id);
            }
            var id = IdGenerator.Next(ref _seq);
            _nodes.Add(new NodeRecord
            {
                Id = id,
                NodeKey = node.NodeKey,
                NodeType = node.NodeType,
                ThreadId = node.ThreadId,
                Key = node.Key,
                Status = node.Status,
                PayloadJson = node.PayloadJson ?? "{}",
                RelationsJson = node.RelationsJson,
                CreatedAt = PersistenceHelper.NowIso(),
            });
            return Task.FromResult(id);
        }
    }

    public Task<NodeRecord?> GetAsync(string nodeKey, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(_nodes.FirstOrDefault(n => string.Equals(n.NodeKey, nodeKey, StringComparison.Ordinal)));
    }

    public Task<IReadOnlyList<NodeRecord>> ListAsync(string? nodeType = null, string? threadId = null,
        long? afterId = null, int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var list = _nodes
                .Where(n => nodeType is null || string.Equals(n.NodeType, nodeType, StringComparison.Ordinal))
                .Where(n => threadId is null || n.ThreadId == threadId)
                .Where(n => afterId is null || n.Id > afterId)
                .OrderBy(n => n.Id)
                .Take(Math.Max(0, limit))
                .ToList();
            return Task.FromResult<IReadOnlyList<NodeRecord>>(list);
        }
    }

    public Task<bool> DeleteAsync(string nodeKey, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var idx = _nodes.FindIndex(n => string.Equals(n.NodeKey, nodeKey, StringComparison.Ordinal));
            if (idx < 0) return Task.FromResult(false);
            _nodes.RemoveAt(idx);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<string>> DeleteManyAsync(IEnumerable<string> nodeKeys, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var keys = (nodeKeys ?? Enumerable.Empty<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
            var removed = new List<string>(keys.Count);
            foreach (var k in keys)
                if (_nodes.RemoveAll(n => string.Equals(n.NodeKey, k, StringComparison.Ordinal)) > 0) removed.Add(k);
            return Task.FromResult<IReadOnlyList<string>>(removed);
        }
    }
}
