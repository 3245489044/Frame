using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Plate.DslMethod.Usharp;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// checkpoint 的节点库承载（三库收敛：ur_checkpoints 并入 ur_nodes，NodeType=<see cref="NodeTypes.Checkpoint"/>）。
/// 实现 <see cref="ICheckpointStore"/>：每个 checkpoint 落为一个节点，NodeKey=<c>checkpoint.{threadId}.{ns}.{checkpointId}</c>、
/// ThreadId=checkpoint.ThreadId、Key=&lt;ns&gt;.&lt;checkpointId&gt;、Status=Step、Payload=checkpoint JSON。
/// 省去独立 ur_checkpoints 表（checkpoint 经节点库承载）。序列化/安全反序列化统一经 <see cref="PersistenceJson"/>（Plate.JsonMethod）。
/// </summary>
public sealed class NodeCheckpointStore : ICheckpointStore
{
    private readonly INodeStore _nodes;

    public NodeCheckpointStore(INodeStore nodes)
    {
        _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    private static string NodeKeyOf(string threadId, string ns, string checkpointId)
        => NodeKeyPrefix.BuildCheckpoint(threadId, ns, checkpointId);

    public async Task<Checkpoint?> GetAsync(string threadId, string? checkpointNs = null, string? checkpointId = null, CancellationToken ct = default)
    {
        var ns = checkpointNs ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(checkpointId))
        {
            var node = await _nodes.GetAsync(NodeKeyOf(threadId, ns, checkpointId), ct).ConfigureAwait(false);
            return node is null ? null : Deserialize(UsharpValueJson.ToPayloadText(node.Payload));
        }
        // 未指定 checkpointId：单次扫描取该 (threadId, ns) 下 Step 最大的 checkpoint（不再二次读取/全集反序列化）。
        Checkpoint? best = null;
        await NodePaging.ForEachAsync(_nodes, NodeTypes.Checkpoint, threadId, (n, _) =>
        {
            var cp = Deserialize(UsharpValueJson.ToPayloadText(n.Payload));
            if (cp is not null && cp.CheckpointNs == ns && (best is null || cp.Step > best.Step)) best = cp;
            return Task.CompletedTask;
        }, ct).ConfigureAwait(false);
        return best;
    }

    public async Task<string> PutAsync(Checkpoint checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var id = PersistenceHelper.ResolveCheckpointId(checkpoint.CheckpointId);
        checkpoint.CheckpointId = id;
        var ns = checkpoint.CheckpointNs ?? string.Empty;
        // 节点构造单一适配器：NodeKey / Key / Status（Step）语义统一经 ZoneNodeAdapter（构造单点，对齐 SingleImplementation）。
        await _nodes.PutAsync(ZoneNodeAdapter.BuildCheckpoint(
            checkpoint.ThreadId, ns, id,
            checkpoint.Step.ToString(CultureInfo.InvariantCulture),
            PersistenceJson.Serialize(checkpoint)), ct).ConfigureAwait(false);
        return id;
    }

    public async Task<IReadOnlyList<string>> ListAsync(string threadId, string? checkpointNs = null, CancellationToken ct = default)
    {
        var ns = checkpointNs ?? string.Empty;
        var nodes = await _nodes.ListAsync(NodeTypes.Checkpoint, threadId, limit: int.MaxValue, ct: ct).ConfigureAwait(false);
        return nodes
            .Select(n => Deserialize(UsharpValueJson.ToPayloadText(n.Payload)))
            .Where(c => c is not null && (c!.CheckpointNs ?? string.Empty) == ns)
            .OrderBy(c => c!.Step)
            .Select(c => c!.CheckpointId)
            .ToList();
    }

    private Checkpoint? Deserialize(string json) => PersistenceJson.TryDeserialize<Checkpoint>(json);
}
