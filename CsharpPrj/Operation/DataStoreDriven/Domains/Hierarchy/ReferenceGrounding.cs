using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Graph;
using Operation.DataStoreDriven.Relations;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 引用自动落地守卫（唯一守卫入口，R1 实现）：**同名门面**，组合通用图接地（<see cref="GraphGrounding"/>）
/// 与 history / task / session 三个域接地服务。
/// <para>
/// 控制反转：本类型位于 <c>Domains</c> 程序集（核心工程不再引用域契约）；
/// 命名空间与公开方法签名逐一保留 ⇒ 既有调用方源码零改。
/// </para>
/// </summary>
public sealed class ReferenceGrounding : IReferenceMaintenance, IHierarchyReferenceManager, IRelationPortsProvider
{
    private readonly GraphGrounding _core;
    private readonly RelationPortLease _ports;
    private readonly TaskReferenceGrounding _tasks;
    private readonly HistoryReferenceGrounding _history;
    private readonly SessionReferenceGrounding _sessions;
    private readonly HierarchyReferenceManager _hierarchy;

    public ReferenceGrounding(ReferenceGraphCoordinator graph, IDefaultWorkflowIdProvider? workflowIds = null)
    {
        _core = new GraphGrounding(graph, workflowIds);
        _ports = new RelationPortLease();
        _tasks = new TaskReferenceGrounding(_core.Edges, _core.Mirrors, _core.Coordinator, _ports, _core.WorkflowIds);
        _history = new HistoryReferenceGrounding(_core.Edges, _core.Mirrors, _core.Coordinator, _tasks, _ports);
        _sessions = new SessionReferenceGrounding(_core.Edges, _core.Mirrors, _core.Coordinator, _ports);
        _hierarchy = new HierarchyReferenceManager(_history, _tasks, _sessions);
    }

    /// <summary>关系端口租约（唯一装配点 = <c>AgentStorageBundle.RegisterRelationPorts</c>；未 Attach 即抛）。</summary>
    public RelationPortLease Ports => _ports;

    /// <summary>租约提供者显式实现（接口返回基类型；具体类型保留给装配点调用 Attach）。</summary>
    IRelationPorts IRelationPortsProvider.Ports => _ports;

    /// <summary>写透目标图协调器（graph-log.db 唯一 owner）。</summary>
    public ReferenceGraphCoordinator Coordinator => _core.Coordinator;

    /// <summary>兼容读面：暴露为 <see cref="IResourceReferenceStore"/>。</summary>
    public IResourceReferenceStore Graph => _core.Graph;

    /// <summary>通用图接地（供宿主按需取通用面）。</summary>
    public GraphGrounding Core => _core;

    /// <summary>默认工作流 Id 提供者。</summary>
    public IDefaultWorkflowIdProvider WorkflowIds => _core.WorkflowIds;

    public IReferenceEdgeDiagnostics? EdgeDiagnostics
    {
        get => _core.EdgeDiagnostics;
        set => _core.EdgeDiagnostics = value;
    }

    public IReferenceEdgeCatalog EdgeCatalog
    {
        get => _core.EdgeCatalog;
        set => _core.EdgeCatalog = value;
    }

    public IDeclaredReferenceSource? DeclarationSource
    {
        get => _core.DeclarationSource;
        set => _core.DeclarationSource = value;
    }

    public global::Operation.DataStoreDriven.Abstractions.INodeStore? NodeStore
    {
        get => _core.NodeStore;
        set => _core.NodeStore = value;
    }

    // ---- 通用图维护面（委托 GraphGrounding）----

    public IReadOnlyList<NodeRelation> AuthoritativeEdgesFor(string workflowId, NodeRecord node)
        => _core.AuthoritativeEdgesFor(workflowId, node);

    public Task MirrorNodeAsync(string workflowId, NodeRecord node, CancellationToken ct = default)
        => _core.MirrorNodeAsync(workflowId, node, ct);

    public Task EnsureNodeMirrorAsync(string workflowId, string nodeKey, string? symbol = null, CancellationToken ct = default)
        => _core.EnsureNodeMirrorAsync(workflowId, nodeKey, symbol, ct);

    public Task RemoveNodeMirrorAsync(string workflowId, string nodeKey, CancellationToken ct = default)
        => _core.RemoveNodeMirrorAsync(workflowId, nodeKey, ct);

    public Task WriteRelationsAsync(string workflowId, string nodeKey,
        IReadOnlyCollection<NodeRelation> authoritative, CancellationToken ct = default)
        => _core.WriteRelationsAsync(workflowId, nodeKey, authoritative, ct);

    public Task WriteStaticReferencesAsync(string workflowId,
        IReadOnlyCollection<StaticReference> references, CancellationToken ct = default)
        => _core.WriteStaticReferencesAsync(workflowId, references, ct);

    public Task DeclareRelationsAsync(string workflowId,
        IReadOnlyCollection<RelationDeclarationEntry> declarations, CancellationToken ct = default)
        => _core.DeclareRelationsAsync(workflowId, declarations, ct);

    public Task UndeclareRelationsAsync(string workflowId,
        IReadOnlyCollection<string> relationIds, CancellationToken ct = default)
        => _core.UndeclareRelationsAsync(workflowId, relationIds, ct);

    public Task<IReadOnlyList<RelationDeclarationEntry>> ListRelationsAsync(string? workflowId = null, CancellationToken ct = default)
        => _core.ListRelationsAsync(workflowId, ct);

    public Task<int> ReconcileAsync(MaintenanceScope scope, string? workflowId = null, string? nodeKey = null, CancellationToken ct = default)
        => _core.ReconcileAsync(scope, workflowId, nodeKey, ct);

    public Task RebuildAllAsync(CancellationToken ct = default) => _core.RebuildAllAsync(ct);

    public Task<GraphMaintenanceDiff> GetDiffAsync(CancellationToken ct = default) => _core.GetDiffAsync(ct);

    public Task<IReadOnlyList<string>> ListDanglingNodeResourcesAsync(CancellationToken ct = default)
        => _core.ListDanglingNodeResourcesAsync(ct);

    public Task<int> SeedAsync(CancellationToken ct = default) => _core.SeedAsync(ct);

    public Task<GraphResyncResult> ResyncAsync(CancellationToken ct = default) => _core.ResyncAsync(ct);

    public Task<IReadOnlyList<GraphTombstoneDoc>> ListTombstonesAsync(string? kind = null, CancellationToken ct = default)
        => _core.ListTombstonesAsync(kind, ct);

    public Task<int> ClearTombstonesAsync(string? kind = null, CancellationToken ct = default)
        => _core.ClearTombstonesAsync(kind, ct);

    public Task SeedDeclarationsFromAsync(IDeclaredReferenceSource source, CancellationToken ct = default)
        => _core.SeedDeclarationsFromAsync(source, null, ct);

    public Task SeedDeclarationsFromAsync(IDeclaredReferenceSource source,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? nodeKeysByWorkflow,
        CancellationToken ct = default)
        => _core.SeedDeclarationsFromAsync(source, nodeKeysByWorkflow, ct);

    // ---- task 域（委托 TaskReferenceGrounding）----

    public Task EnsureTaskMirrorAsync(string taskKey, CancellationToken ct = default)
        => _tasks.EnsureTaskMirrorAsync(taskKey, ct);

    public Task RemoveTaskMirrorAsync(string taskKey, CancellationToken ct = default)
        => _tasks.RemoveTaskMirrorAsync(taskKey, ct);

    public Task ReconcileTaskAsync(string taskKey, IReadOnlyCollection<string> relatedNodeKeys,
        string workflowId = "", CancellationToken ct = default)
        => _tasks.ReconcileTaskAsync(taskKey, relatedNodeKeys, workflowId, ct);

    public Task EnsureTaskNodeLinkAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _tasks.EnsureTaskNodeLinkAsync(taskKey, workflowId, nodeKey, ct);

    public Task UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _tasks.UnlinkTaskNodeAsync(taskKey, workflowId, nodeKey, ct);

    public Task<IReadOnlyList<string>> GetTaskNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct = default)
        => _tasks.GetTaskNodeKeysAsync(taskKey, workflowId, ct);

    public Task LinkSubTaskAsync(string parentTaskKey, string subTaskKey, CancellationToken ct = default)
        => _tasks.LinkSubTaskAsync(parentTaskKey, subTaskKey, ct);

    public Task UnlinkSubTaskAsync(string parentTaskKey, string subTaskKey, CancellationToken ct = default)
        => _tasks.UnlinkSubTaskAsync(parentTaskKey, subTaskKey, ct);

    public Task<IReadOnlyList<string>> GetSubTaskKeysAsync(string taskKey, CancellationToken ct = default)
        => _tasks.GetSubTaskKeysAsync(taskKey, ct);

    public Task<bool> LinkSubTaskCheckedAsync(string parentTaskKey, string subTaskKey, CancellationToken ct = default)
        => _tasks.LinkSubTaskCheckedAsync(parentTaskKey, subTaskKey, ct);

    public Task<IReadOnlyList<NodeProjectionItem>> ProjectTaskNodesAsync(string taskKey, string workflowId, CancellationToken ct = default)
        => _tasks.ProjectTaskNodesAsync(taskKey, workflowId, ct);

    // ---- history 域（委托 HistoryReferenceGrounding）----

    public Task EnsureHistoryMirrorAsync(string historyId, CancellationToken ct = default)
        => _history.EnsureHistoryMirrorAsync(historyId, ct);

    public Task RemoveHistoryMirrorAsync(string historyId, CancellationToken ct = default)
        => _history.RemoveHistoryMirrorAsync(historyId, ct);

    public Task AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
        => _history.AttachHistoryTaskAsync(historyId, taskKey, ct);

    public Task DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
        => _history.DetachHistoryTaskAsync(historyId, taskKey, ct);

    public Task<IReadOnlyList<string>> GetHistoryTasksAsync(string historyId, CancellationToken ct = default)
        => _history.GetHistoryTasksAsync(historyId, ct);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetHistoryTasksManyAsync(
        IReadOnlyCollection<string> historyIds, CancellationToken ct)
        => _history.GetHistoryTasksManyAsync(historyIds, ct);

    public Task AttachHistoryNodeAsync(string historyId, string workflowId, string nodeKey, CancellationToken ct = default)
        => _history.AttachHistoryNodeAsync(historyId, workflowId, nodeKey, ct);

    public Task<IReadOnlyList<string>> GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
        => _history.GetHistoryNodeKeysAsync(historyId, workflowId, ct);

    public Task<int> MergeHistoriesAsync(string intoHistory, string fromHistory, CancellationToken ct = default)
        => _history.MergeHistoriesAsync(intoHistory, fromHistory, ct);

    public Task<EndpointRekeyPlan> PlanEndpointRekeyAsync(IReadOnlyCollection<string> taskKeys, CancellationToken ct = default)
        => _history.PlanEndpointRekeyAsync(taskKeys, ct);

    public Task<EndpointRekeyResult> ApplyEndpointRekeyAsync(EndpointRekeyPlan plan, CancellationToken ct = default)
        => _history.ApplyEndpointRekeyAsync(plan, ct);


    // ---- session 域（委托 SessionReferenceGrounding）----

    public Task EnsureSessionMirrorAsync(string sessionName, CancellationToken ct = default)
        => _sessions.EnsureSessionMirrorAsync(sessionName, ct);

    public Task RemoveSessionMirrorAsync(string sessionName, CancellationToken ct = default)
        => _sessions.RemoveSessionMirrorAsync(sessionName, ct);

    public Task AddSessionMessageLinkAsync(string sessionName, string workflowId, string nodeKey, CancellationToken ct = default)
        => _sessions.AddSessionMessageLinkAsync(sessionName, workflowId, nodeKey, ct);

    public Task<IReadOnlyList<string>> GetSessionNodeKeysAsync(string sessionName, string workflowId, CancellationToken ct = default)
        => _sessions.GetSessionNodeKeysAsync(sessionName, workflowId, ct);

    public Task<IReadOnlyList<NodeProjectionItem>> ProjectSessionNodesAsync(string sessionName, string workflowId, CancellationToken ct = default)
        => _sessions.ProjectSessionNodesAsync(sessionName, workflowId, ct);

    // ---- IHierarchyReferenceManager（唯一中立端口；组合三域服务）----

    Task IHierarchyReferenceManager.EnsureHistoryAsync(string historyId, CancellationToken ct)
        => _hierarchy.EnsureHistoryAsync(historyId, ct);

    Task<bool> IHierarchyReferenceManager.DeleteHistoryAsync(string historyId, CancellationToken ct)
        => _hierarchy.DeleteHistoryAsync(historyId, ct);

    Task IHierarchyReferenceManager.AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct)
        => _hierarchy.AttachHistoryTaskAsync(historyId, taskKey, ct);

    Task IHierarchyReferenceManager.DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct)
        => _hierarchy.DetachHistoryTaskAsync(historyId, taskKey, ct);

    Task<IReadOnlyList<string>> IHierarchyReferenceManager.GetHistoryTaskKeysAsync(string historyId, CancellationToken ct)
        => _hierarchy.GetHistoryTaskKeysAsync(historyId, ct);

    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> IHierarchyReferenceManager.GetHistoryTaskKeysManyAsync(
        IReadOnlyCollection<string> historyIds, CancellationToken ct)
        => _hierarchy.GetHistoryTaskKeysManyAsync(historyIds, ct);

    Task IHierarchyReferenceManager.LinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct)
        => _hierarchy.LinkTaskNodeAsync(taskKey, workflowId, nodeKey, ct);

    Task IHierarchyReferenceManager.UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct)
        => _hierarchy.UnlinkTaskNodeAsync(taskKey, workflowId, nodeKey, ct);

    Task<IReadOnlyList<string>> IHierarchyReferenceManager.GetTaskNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct)
        => _hierarchy.GetTaskNodeKeysAsync(taskKey, workflowId, ct);

    Task<IReadOnlyList<string>> IHierarchyReferenceManager.GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct)
        => _hierarchy.GetHistoryNodeKeysAsync(historyId, workflowId, ct);

    Task<IReadOnlyList<string>> IHierarchyReferenceManager.GetSessionNodeKeysAsync(string sessionName, string workflowId, CancellationToken ct)
        => _hierarchy.GetSessionNodeKeysAsync(sessionName, workflowId, ct);

    Task<IReadOnlyList<NodeProjectionItem>> IHierarchyReferenceManager.ProjectSessionNodesAsync(string sessionName, string workflowId, CancellationToken ct)
        => _hierarchy.ProjectSessionNodesAsync(sessionName, workflowId, ct);

}
