using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Graph;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 通用图镜像写入器（U8 通用半）：端点 upsert / 删除 / 节点内容跟随，**零领域词汇**。
/// </summary>
public sealed class GraphMirrorWriter
{
    private readonly ReferenceGraphCoordinator _graph;

    public GraphMirrorWriter(ReferenceGraphCoordinator graph)
        => _graph = graph ?? throw new ArgumentNullException(nameof(graph));

    /// <summary>端点镜像幂等（已存在即跳过；唯一键冲突容错）。</summary>
    public Task EnsureResourceAsync(ResourceIdentifier id, string? symbol, CancellationToken ct)
        => _graph.EnsureResourceAsync(id, symbol, null, ct);

    /// <summary>镜像业务节点内容（upsert 内容跟随；不删引用边）。</summary>
    public Task MirrorNodeAsync(string workflowId, NodeRecord node, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        var id = new ResourceIdentifier
        {
            ResourceType = NodeResourceTypes.Node,
            Id = NodeRelationFactory.NodeId(workflowId, node.NodeKey),
            Location = workflowId,
        };
        var metadata = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["nodeKey"] = node.NodeKey,
            ["nodeType"] = string.IsNullOrWhiteSpace(node.NodeType) ? NodeTypes.Node : node.NodeType,
            ["status"] = node.Status ?? string.Empty,
            ["threadId"] = workflowId,
            ["updatedAt"] = PersistenceHelper.NowIso(),
        };
        return _graph.UpdateResourceAsync(id, metadata, node.NodeKey, ct);
    }

    /// <summary>镜像 node 端点（wf.node）。</summary>
    public Task EnsureNodeMirrorAsync(string workflowId, string nodeKey, string? symbol = null, CancellationToken ct = default)
        => EnsureResourceAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Node, Id = NodeRelationFactory.NodeId(workflowId, nodeKey), Location = workflowId },
            symbol ?? nodeKey, ct);

    /// <summary>移除 node 端点镜像（级联清边）。</summary>
    public Task RemoveNodeMirrorAsync(string workflowId, string nodeKey, CancellationToken ct = default)
        => _graph.DeleteEndpointDurablyAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Node, Id = NodeRelationFactory.NodeId(workflowId, nodeKey), Location = workflowId },
            "node-removed", ct);
}
