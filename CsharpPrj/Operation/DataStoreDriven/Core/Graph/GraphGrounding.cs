using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Graph;
using Plate.DslMethod.Usharp;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 通用图接地实现（U8 通用半，实现 <see cref="IGraphGrounding"/>）：节点镜像 / 权威边 / 声明 / 对账 / 重建 / 墓碑 / seed。
/// <para>零领域词汇：task / history / session 语义由 <c>Domains\Hierarchy</c> 的接地服务组合本类型。</para>
/// </summary>
public sealed class GraphGrounding : IGraphGrounding
{
    private readonly ReferenceGraphCoordinator _graph;
    private readonly GraphEdgeWriter _edges;
    private readonly GraphMirrorWriter _mirrors;

    public GraphGrounding(ReferenceGraphCoordinator graph, IDefaultWorkflowIdProvider? workflowIds = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _edges = new GraphEdgeWriter(graph);
        _mirrors = new GraphMirrorWriter(graph);
        WorkflowIds = workflowIds ?? ConfiguredDefaultWorkflowIdProvider.Default;
    }

    /// <summary>默认工作流 Id 提供者（U9；缺省值保持既有行为）。</summary>
    public IDefaultWorkflowIdProvider WorkflowIds { get; }

    /// <summary>通用写边器（供域接地服务组合）。</summary>
    public GraphEdgeWriter Edges => _edges;

    /// <summary>通用镜像写入器（供域接地服务组合）。</summary>
    public GraphMirrorWriter Mirrors => _mirrors;

    public ReferenceGraphCoordinator Coordinator => _graph;

    public IResourceReferenceStore Graph => _graph;

    public IReferenceEdgeDiagnostics? EdgeDiagnostics
    {
        get => _edges.EdgeDiagnostics;
        set => _edges.EdgeDiagnostics = value;
    }

    public IReferenceEdgeCatalog EdgeCatalog
    {
        get => _edges.EdgeCatalog;
        set => _edges.EdgeCatalog = value;
    }

    public INodeStore? NodeStore
    {
        get => _edges.NodeStore;
        set => _edges.NodeStore = value;
    }

    public IDeclaredReferenceSource? DeclarationSource { get; set; }

    /// <summary>取某节点的权威边（G1/G3 单一取值缝）。</summary>
    public IReadOnlyList<NodeRelation> AuthoritativeEdgesFor(string workflowId, NodeRecord node)
    {
        if (node is null) return Array.Empty<NodeRelation>();
        var result = new List<NodeRelation>();
        if (DeclarationSource is not null)
            result.AddRange(DeclarationSource.GetNodeRelations(workflowId, node.NodeKey));
        var nodeId = NodeRelationFactory.NodeId(workflowId, node.NodeKey);
        foreach (var e in _graph.GetRelationEdges(workflowId))
            if (string.Equals(e.TargetId, nodeId, StringComparison.Ordinal)) result.Add(e);
        return result;
    }

    // ---- 镜像（委托通用写入器）----

    public Task MirrorNodeAsync(string workflowId, NodeRecord node, CancellationToken ct = default)
        => _mirrors.MirrorNodeAsync(workflowId, node, ct);

    public Task EnsureNodeMirrorAsync(string workflowId, string nodeKey, string? symbol = null, CancellationToken ct = default)
        => _mirrors.EnsureNodeMirrorAsync(workflowId, nodeKey, symbol, ct);

    public Task RemoveNodeMirrorAsync(string workflowId, string nodeKey, CancellationToken ct = default)
        => _mirrors.RemoveNodeMirrorAsync(workflowId, nodeKey, ct);

    /// <summary>列悬空 node 端点（仅报告不删除）。</summary>
    public async Task<IReadOnlyList<string>> ListDanglingNodeResourcesAsync(CancellationToken ct = default)
    {
        var resources = await Graph.GetAllResourcesAsync(ct).ConfigureAwait(false);
        var dangling = new List<string>();
        foreach (var resource in resources)
        {
            if (resource is null) continue;
            if (!string.Equals(resource.Identifier.ResourceType, NodeResourceTypes.Node, StringComparison.Ordinal)) continue;
            if (string.IsNullOrWhiteSpace(resource.Identifier.Id)) continue;
            var refs = await Graph.GetReferencesAsync(resource.Identifier, includeIncoming: true, ct).ConfigureAwait(false);
            if (refs.Count == 0) dangling.Add(resource.Identifier.Id);
        }
        return dangling;
    }

    public async Task<GraphMaintenanceDiff> GetDiffAsync(CancellationToken ct = default)
    {
        var reconciled = await ReconcileAsync(MaintenanceScope.All, ct: ct).ConfigureAwait(false);
        var dangling = await ListDanglingNodeResourcesAsync(ct).ConfigureAwait(false);
        return new GraphMaintenanceDiff(dangling, reconciled);
    }

    public async Task WriteRelationsAsync(string workflowId, string nodeKey,
        IReadOnlyCollection<NodeRelation> authoritative, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authoritative);
        var targetNodeId = NodeRelationFactory.NodeId(workflowId, nodeKey);
        var existing = (await _graph.GetRelationDeclarationsAsync(workflowId, ct).ConfigureAwait(false))
            .Where(d => string.Equals(d.Target, targetNodeId, StringComparison.Ordinal))
            .ToList();
        var authByKey = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in authoritative)
            if (e is not null) authByKey.Add(ReferenceGraphCoordinator.RelationKey(e));

        foreach (var d in existing)
        {
            var edge = NodeRelationDeriver.DeriveEdgeFromDeclaration(d.WorkflowId, ReferenceGraphCoordinator.ToDeclaration(d));
            if (edge is null || !authByKey.Contains(ReferenceGraphCoordinator.RelationKey(edge)))
                await _graph.DeleteRelationDeclarationAsync(d.NodeKey, ct).ConfigureAwait(false);
        }

        var ordinal = 0;
        foreach (var e in authoritative)
        {
            if (e is null) continue;
            var entry = BuildEntry(workflowId, e, "w" + ordinal++);
            await _graph.DeclareRelationAsync(entry.NodeKey, entry.Doc, ct).ConfigureAwait(false);
        }
    }

    public async Task WriteStaticReferencesAsync(string workflowId,
        IReadOnlyCollection<StaticReference> references, CancellationToken ct = default)
    {
        if (references is null || references.Count == 0) return;
        foreach (var r in references)
        {
            if (r is null || string.IsNullOrWhiteSpace(r.RefType)
                || string.IsNullOrWhiteSpace(r.SourceId) || string.IsNullOrWhiteSpace(r.TargetId)) continue;
            var built = BuildStaticEntry(workflowId, r);
            await SeedOneAsync(built.Doc, built.NodeKey, ct).ConfigureAwait(false);
        }
    }

    public async Task DeclareRelationsAsync(string workflowId,
        IReadOnlyCollection<RelationDeclarationEntry> declarations, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        foreach (var entry in declarations)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.RelationId)) continue;
            var decl = entry.Declaration ?? new RelationDeclaration();
            var nodeKey = RelationDeclarationKeys.NodeKey(workflowId, entry.RelationId);
            await _graph.DeclareRelationAsync(nodeKey, ToRelationDoc(workflowId, entry.RelationId, nodeKey, decl), ct).ConfigureAwait(false);
        }
    }

    public async Task UndeclareRelationsAsync(string workflowId,
        IReadOnlyCollection<string> relationIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(relationIds);
        foreach (var relationId in relationIds)
        {
            if (string.IsNullOrWhiteSpace(relationId)) continue;
            var nodeKey = RelationDeclarationKeys.NodeKey(workflowId, relationId);
            await _graph.DeleteDeclarationDurablyAsync(nodeKey, "undeclare", ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<RelationDeclarationEntry>> ListRelationsAsync(
        string? workflowId = null, CancellationToken ct = default)
    {
        var docs = await _graph.GetRelationDeclarationsAsync(workflowId, ct).ConfigureAwait(false);
        var result = new List<RelationDeclarationEntry>(docs.Count);
        foreach (var d in docs)
            result.Add(new RelationDeclarationEntry { RelationId = d.RelationId, Declaration = ReferenceGraphCoordinator.ToDeclaration(d) });
        return result;
    }

    // ---- IGraphMaintenanceStore ----

    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        if (DeclarationSource is not { } source) return 0;
        var before = (await _graph.GetRelationDeclarationsAsync(null, ct).ConfigureAwait(false)).Count;
        await SeedDeclarationsFromAsync(source, null, ct).ConfigureAwait(false);
        var after = (await _graph.GetRelationDeclarationsAsync(null, ct).ConfigureAwait(false)).Count;
        return Math.Max(0, after - before);
    }

    public async Task<GraphResyncResult> ResyncAsync(CancellationToken ct = default)
    {
        var cleared = await _graph.ClearTombstonesAsync(null, ct).ConfigureAwait(false);
        var seeded = await SeedAsync(ct).ConfigureAwait(false);
        return new GraphResyncResult(cleared, seeded);
    }

    public Task<IReadOnlyList<GraphTombstoneDoc>> ListTombstonesAsync(string? kind = null, CancellationToken ct = default)
        => _graph.ListTombstonesAsync(kind, ct);

    public Task<int> ClearTombstonesAsync(string? kind = null, CancellationToken ct = default)
        => _graph.ClearTombstonesAsync(kind, ct);

    public Task SeedDeclarationsFromAsync(IDeclaredReferenceSource source, CancellationToken ct = default)
        => SeedDeclarationsFromAsync(source, null, ct);


    public async Task SeedDeclarationsFromAsync(
        IDeclaredReferenceSource source,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? nodeKeysByWorkflow,
        CancellationToken ct = default)
    {
        if (source is null) return;
        foreach (var workflowId in source.WorkflowIds)
        {
            if (string.IsNullOrWhiteSpace(workflowId)) continue;

            foreach (var s in source.GetStaticEdges(workflowId) ?? Array.Empty<StaticReference>())
            {
                if (s is null || string.IsNullOrWhiteSpace(s.SourceId) || string.IsNullOrWhiteSpace(s.TargetId)) continue;
                var built = BuildStaticEntry(workflowId, s);
                await SeedOneAsync(built.Doc, built.NodeKey, ct).ConfigureAwait(false);
            }

            if (nodeKeysByWorkflow is null
                || !nodeKeysByWorkflow.TryGetValue(workflowId, out var nodeKeys)
                || nodeKeys is not { Count: > 0 })
                continue;

            foreach (var nodeKey in nodeKeys)
            {
                if (string.IsNullOrWhiteSpace(nodeKey)) continue;
                var relations = source.GetNodeRelations(workflowId, nodeKey);
                if (relations is null || relations.Count == 0) continue;

                foreach (var r in relations)
                {
                    if (r is null || string.IsNullOrWhiteSpace(r.SourceId) || string.IsNullOrWhiteSpace(r.TargetId)) continue;
                    var built = BuildEntry(workflowId, r, "n");
                    await SeedOneAsync(built.Doc, built.NodeKey, ct).ConfigureAwait(false);
                }
            }
        }
    }

    public async Task RebuildAllAsync(CancellationToken ct = default)
    {
        await _graph.ClearAllAsync(ct).ConfigureAwait(false);
        await RebuildDeclarationsAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ReconcileAsync(MaintenanceScope scope, string? workflowId = null, string? nodeKey = null, CancellationToken ct = default)
    {
        if (scope == MaintenanceScope.Node)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
            ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
            var nodeId = NodeRelationFactory.NodeId(workflowId, nodeKey);
            var edges = _graph.GetRelationEdges(workflowId)
                .Where(e => string.Equals(e.TargetId, nodeId, StringComparison.Ordinal))
                .ToList();
            if (edges.Count == 0) return 0;
            await WriteRelationsAsync(workflowId, nodeKey, edges, ct).ConfigureAwait(false);
            return 1;
        }

        var docs = await _graph.GetRelationDeclarationsAsync(null, ct).ConfigureAwait(false);
        var processed = 0;
        foreach (var d in docs)
        {
            if (await _graph.IsTombstonedAsync(GraphIdentityKeys.EndpointTombstoneKey(d.SourceType, d.Source), ct).ConfigureAwait(false)
                || await _graph.IsTombstonedAsync(GraphIdentityKeys.EndpointTombstoneKey(d.TargetType, d.Target), ct).ConfigureAwait(false))
                continue;
            await _graph.AddDeclaredEdgeAsync(d, ct).ConfigureAwait(false);
            processed++;
        }
        return processed;
    }


    // ---- 内部：seed / 重建 / 声明文档构造 ----

    private async Task SeedOneAsync(RelationDoc doc, string nodeKey, CancellationToken ct)
    {
        if (await _graph.IsTombstonedAsync(GraphIdentityKeys.DeclarationTombstoneKey(doc), ct).ConfigureAwait(false))
            return;

        var effective = await _graph.SeedRelationDeclarationAsync(nodeKey, doc, ct).ConfigureAwait(false);
        if (effective is null) return;

        if (await _graph.IsTombstonedAsync(GraphIdentityKeys.EndpointTombstoneKey(effective.SourceType, effective.Source), ct).ConfigureAwait(false)
            || await _graph.IsTombstonedAsync(GraphIdentityKeys.EndpointTombstoneKey(effective.TargetType, effective.Target), ct).ConfigureAwait(false))
            return;

        var src = new ResourceIdentifier { ResourceType = effective.SourceType, Id = effective.Source, Location = effective.WorkflowId };
        var tgt = new ResourceIdentifier { ResourceType = effective.TargetType, Id = effective.Target, Location = effective.WorkflowId };
        await _graph.EnsureResourceAsync(src, effective.Source, null, ct).ConfigureAwait(false);
        await _graph.EnsureResourceAsync(tgt, effective.Target, null, ct).ConfigureAwait(false);
        await _graph.AddDeclaredEdgeAsync(effective, ct).ConfigureAwait(false);
    }

    private async Task RebuildDeclarationsAsync(CancellationToken ct)
    {
        var docs = await _graph.GetRelationDeclarationsAsync(null, ct).ConfigureAwait(false);
        foreach (var d in docs)
        {
            if (await _graph.IsTombstonedAsync(GraphIdentityKeys.EndpointTombstoneKey(d.SourceType, d.Source), ct).ConfigureAwait(false)
                || await _graph.IsTombstonedAsync(GraphIdentityKeys.EndpointTombstoneKey(d.TargetType, d.Target), ct).ConfigureAwait(false))
                continue;
            var src = new ResourceIdentifier { ResourceType = d.SourceType, Id = d.Source, Location = d.WorkflowId };
            var tgt = new ResourceIdentifier { ResourceType = d.TargetType, Id = d.Target, Location = d.WorkflowId };
            await _graph.EnsureResourceAsync(src, d.Source, null, ct).ConfigureAwait(false);
            await _graph.EnsureResourceAsync(tgt, d.Target, null, ct).ConfigureAwait(false);
            await _graph.AddDeclaredEdgeAsync(d, ct).ConfigureAwait(false);
        }
    }

    private static (string NodeKey, RelationDoc Doc) BuildEntry(string workflowId, NodeRelation r, string idPrefix)
    {
        var refType = NodeRelationRefCodec.Encode(r);
        var seed = new RelationDoc
        {
            WorkflowId = workflowId,
            SourceType = r.SourceResourceType,
            Source = r.SourceId,
            SourcePort = r.SourcePort,
            TargetType = r.TargetResourceType,
            Target = r.TargetId,
            TargetPort = r.TargetPort,
            RefType = refType,
            KeyMappingRef = r.KeyMappingRef,
        };
        var relationId = idPrefix + GraphIdentityKeys.DeclarationFingerprint(seed)[..16];
        var nodeKey = RelationDeclarationKeys.NodeKey(workflowId, relationId);
        return (nodeKey, seed with { RelationId = relationId, NodeKey = nodeKey });
    }

    private static (string NodeKey, RelationDoc Doc) BuildStaticEntry(string workflowId, StaticReference s)
    {
        var seed = new RelationDoc
        {
            WorkflowId = workflowId,
            SourceType = s.SourceResourceType,
            Source = s.SourceId,
            TargetType = s.TargetResourceType,
            Target = s.TargetId,
            RefType = s.RefType,
        };
        var relationId = "s" + GraphIdentityKeys.DeclarationFingerprint(seed)[..16];
        var nodeKey = RelationDeclarationKeys.NodeKey(workflowId, relationId);
        return (nodeKey, seed with { RelationId = relationId, NodeKey = nodeKey });
    }

    private static RelationDoc ToRelationDoc(string workflowId, string relationId, string nodeKey, RelationDeclaration d) => new()
    {
        RelationId = relationId,
        WorkflowId = workflowId,
        NodeKey = nodeKey,
        Source = d.Source?.Id ?? string.Empty,
        SourceType = d.Source?.ResourceType ?? NodeResourceTypes.Node,
        SourcePort = d.Source?.Port,
        Target = d.Target?.Id ?? string.Empty,
        TargetType = d.Target?.ResourceType ?? NodeResourceTypes.Node,
        TargetPort = d.Target?.Port,
        RefType = d.RefType ?? Modules.Node.Relations.ReferenceTypes.NodeToNode,
        KeyMappingRef = d.KeyMappingRef,
    };
}

