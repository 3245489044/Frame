using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.Graph;
using UniversalWorkflowFramework.Core.Models;

namespace Operation.DataStoreDriven.Core;

/// <summary>session 域接地服务（U8 域半）：会话根镜像 / session_owns 子图 / 时序投影。</summary>
public sealed class SessionReferenceGrounding
{
    private readonly GraphEdgeWriter _edges;
    private readonly GraphMirrorWriter _mirrors;
    private readonly ReferenceGraphCoordinator _graph;

    public SessionReferenceGrounding(GraphEdgeWriter edges, GraphMirrorWriter mirrors, ReferenceGraphCoordinator graph)
    {
        _edges = edges ?? throw new ArgumentNullException(nameof(edges));
        _mirrors = mirrors ?? throw new ArgumentNullException(nameof(mirrors));
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>会话根端点：幂等建 session.{name} 端点镜像。</summary>
    public Task EnsureSessionMirrorAsync(string sessionName, CancellationToken ct = default)
        => _mirrors.EnsureResourceAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Session, Id = SessionNodeId(sessionName) },
            SessionNodeId(sessionName), ct);

    /// <summary>移除会话根端点镜像（级联清边）。</summary>
    public Task RemoveSessionMirrorAsync(string sessionName, CancellationToken ct = default)
        => _graph.DeleteEndpointDurablyAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Session, Id = SessionNodeId(sessionName) }, "session-removed", ct);

    /// <summary>会话归属边（session_owns）：把业务节点挂到会话根（幂等加法）。</summary>
    public async Task AddSessionMessageLinkAsync(string sessionName, string workflowId, string nodeKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        await EnsureSessionMirrorAsync(sessionName, ct).ConfigureAwait(false);
        var sessionId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Session, Id = SessionNodeId(sessionName), Location = workflowId };
        var nodeId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Node, Id = NodeRelationFactory.NodeId(workflowId, nodeKey), Location = workflowId };
        await _edges.AttachEdgeAsync(CrossDbReferenceTypes.SessionOwns, sessionId, nodeId, $"{sessionName}|{nodeKey}", ct).ConfigureAwait(false);
    }

    /// <summary>会话子图节点键集：按 session_owns 出边返回。</summary>
    public async Task<IReadOnlyList<string>> GetSessionNodeKeysAsync(string sessionName, string workflowId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionName)) return Array.Empty<string>();
        var sessionId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Session, Id = SessionNodeId(sessionName), Location = workflowId };
        var targets = await _edges.GetOutgoingTargetIdsAsync(CrossDbReferenceTypes.SessionOwns, sessionId, ct).ConfigureAwait(false);
        var keys = new List<string>();
        foreach (var id in targets)
            if (HierarchyResourceKeys.TryParseNodeKey(id, workflowId, out var nodeKey)) keys.Add(nodeKey);
        return keys.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>会话子图投影（时序序唯一实现）。</summary>
    public async Task<IReadOnlyList<NodeProjectionItem>> ProjectSessionNodesAsync(string sessionName, string workflowId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionName)) return Array.Empty<NodeProjectionItem>();
        var keys = await GetSessionNodeKeysAsync(sessionName, workflowId, ct).ConfigureAwait(false);
        if (keys.Count == 0) return Array.Empty<NodeProjectionItem>();
        return keys
            .OrderBy(k => k, SessionMessageKeys.OrderComparer)
            .Select(k => new NodeProjectionItem(k, workflowId, ParseMsgSeq(k)))
            .ToList();
    }

    private static string SessionNodeId(string sessionName) => SessionMessageKeys.SessionRoot(sessionName);

    private static int ParseMsgSeq(string? nodeKey) => SessionMessageKeys.ParseSeq(nodeKey);
}
