using System;
using System.Collections.Generic;
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
/// 引用自动落地守卫（唯一守卫入口，R1 实现，对齐公理 DerivedDataAutoMaintenance / SingleImplementation / TH-12，见 Agent/Axioms）：
/// 把「业务实体（node/output/keymapping/task）↔ 图资源镜像 + 节点关系边」的维护固化为存储级职责，替代各写入口手写建边。
/// <para>
/// 组件分工（只做决策/编排，写边原语仍在协调器）：本守卫持有权威推导 + 对账 + 重建（实现 <see cref="IReferenceMaintenance"/>），
///   · 镜像维护：<see cref="EnsureNodeMirrorAsync"/> / <see cref="RemoveNodeMirrorAsync"/>（wf.node）、<see cref="EnsureTaskMirrorAsync"/> / <see cref="RemoveTaskMirrorAsync"/>（task）
///   · 权威边集写入：<see cref="WriteRelationsAsync"/> —— 经 <see cref="ReferenceGraphCoordinator.ApplyRelationEdgesAsync"/>（纯执行者）收敛
///   · 对账：<see cref="ReconcileAsync"/>（单节点 / 全图两级）
/// </para>
/// 写边仍经单一实现 <c>ReferenceGraphCoordinator</c>（幂等），本守卫只负责"何时写/推导出哪些边/对账编排"，
/// 不复制写边逻辑（SingleImplementation / TH-12）；删除级联沿用 <c>RemoveResourceAsync</c>，本守卫不重写。
/// </summary>
public sealed class ReferenceGrounding : IReferenceMaintenance, IHierarchyReferenceManager
{
    private readonly ReferenceGraphCoordinator _graph;

    /// <summary>可选准入诊断出口（W1 写边准入的可见化通道）；缺省静默。装配期由组合根注入。</summary>
    public IReferenceEdgeDiagnostics? EdgeDiagnostics { get; set; }

    /// <summary>可选归属边白名单（W1）：未装配 ⇒ **报告模式**（不拒绝任何边，行为与现状逐字一致）；
    /// 装配 <see cref="DeclaredReferenceEdgeCatalog"/> ⇒ **拒绝模式**（未声明边拒写 + 可见化）。
    /// 装配期由组合根注入（同 <see cref="NodeStore"/>；默认值 = 零行为变更，回退 = 置回默认）。</summary>
    public IReferenceEdgeCatalog EdgeCatalog { get; set; } = PermissiveReferenceEdgeCatalog.Instance;

    /// <summary>可选声明引用源（G3/G4）：装配期由 USharp 声明（workflows + reference + placement）投影产出权威边，
    /// 取代节点内 <c>RelationsJson</c> 推导。缺省 <c>null</c> ⇒ 回退 RelationsJson 兼容读（零行为变更）。
    /// 装配期由组合根注入（同 <see cref="NodeStore"/>）。</summary>
    public IDeclaredReferenceSource? DeclarationSource { get; set; }

    /// <summary>取某节点的权威边（G1/G3 单一取值缝）：配置声明源（seed）+ LiteDB <c>ur_relations</c>（active）投影；
    /// 节点内 RelationsJson 回退已退役（设计 §5-#2：边来源单源 = LiteDB 声明集合）。</summary>
    public IReadOnlyList<NodeRelation> AuthoritativeEdgesFor(string workflowId, NodeRecord node)
    {
        if (node is null) return Array.Empty<NodeRelation>();
        var result = new List<NodeRelation>();
        if (DeclarationSource is not null)
            result.AddRange(DeclarationSource.GetNodeRelations(workflowId, node.NodeKey));
        var nodeId = NodeRelationFactory.NodeId(workflowId, node.NodeKey);
        foreach (var e in _graph.GetActiveRelationEdges(workflowId))
            if (string.Equals(e.TargetId, nodeId, StringComparison.Ordinal)) result.Add(e);
        return result;
    }

    /// <summary>归属边准入（W1）：未在声明面登记 ⇒ 拒写 + 可见化（禁静默建边；未装配白名单 ⇒ 报告模式放行）。</summary>
    private bool AdmitEdge(string edgeType, string subject)
    {
        if (EdgeCatalog.IsDeclared(edgeType))
        {
            return true;
        }

        (EdgeDiagnostics ?? NullReferenceEdgeDiagnostics.Instance).EdgeUndeclared(edgeType, subject);
        return false;
    }

    // ---- 通用边算子（C 批：声明驱动重构）----
    // 目的：消除「每边一手」的准入 + 写边 / 删边 / 读边过滤样板。边类型仍是参数（唯一权威 = 声明面白名单，
    // 经 AdmitEdge 准入），端点构成由调用方给出（端点类型/Id 形态的声明化归 D-3 后续批次）。
    // 单一写边 owner 不变：一律经协调器 AddReferenceAsync / RemoveReferenceAsync（TH-12）。

    /// <summary>写归属边（幂等加法）：先准入（声明面白名单），后经协调器单一写边原语落图。</summary>
    private async Task AttachEdgeAsync(string edgeType, ResourceIdentifier source, ResourceIdentifier target, string subject, CancellationToken ct)
    {
        if (!AdmitEdge(edgeType, subject)) return;
        await _graph.AddReferenceAsync(source, target, edgeType, 1.0, ct).ConfigureAwait(false);
    }

    /// <summary>删归属边（幂等：不存在则无副作用）——经协调器单一写边原语。</summary>
    private Task DetachEdgeAsync(string edgeType, ResourceIdentifier source, ResourceIdentifier target, CancellationToken ct)
        => _graph.RemoveReferenceAsync(source, target, edgeType, ct);

    /// <summary>读某边类型的**出边目标 Id 集**（按边类型过滤、去重保序；裸键解析归调用方，禁本地拼切）。</summary>
    private async Task<IReadOnlyList<string>> GetOutgoingTargetIdsAsync(string edgeType, ResourceIdentifier source, CancellationToken ct)
    {
        var refs = await _graph.GetReferencesAsync(source, includeIncoming: false, ct).ConfigureAwait(false);
        return refs
            .Where(r => string.Equals(r.RefType, edgeType, StringComparison.Ordinal))
            .Select(r => r.Target?.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public ReferenceGrounding(ReferenceGraphCoordinator graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>写透目标图协调器（graph-log.db 唯一 owner；LiteDB 只存边，实体以 SQL 为准）。</summary>
    public ReferenceGraphCoordinator Coordinator => _graph;

    /// <summary>兼容读面：暴露为 <see cref="IResourceReferenceStore"/>（对账器/编排器复用）。</summary>
    public IResourceReferenceStore Graph => _graph;

    /// <summary>
    /// 列出图库中无任何引用边的**悬空** node 端点 Id（A5；G3 改 LiteDB 内部自洽判据，不读 SQL）。
    /// 仅报告不删除（防级联误删）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
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

    /// <summary>
    /// 图对账差异报告（A5）：全量对账收敛（图 ← SQL 权威）→ 列悬空端点 + 回报对账节点数。
    /// <para>幂等可重复：连跑两次第二次差异 = 0（对账已收敛的判据；门禁基线「只减不增」的运行时 counterpart）。</para>
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    public async Task<GraphMaintenanceDiff> GetDiffAsync(CancellationToken ct = default)
    {
        var reconciled = await ReconcileAsync(MaintenanceScope.All, ct: ct).ConfigureAwait(false);
        var dangling = await ListDanglingNodeResourcesAsync(ct).ConfigureAwait(false);
        return new GraphMaintenanceDiff(dangling, reconciled);
    }

    // ---- 节点业务实体镜像（wf.node） ----

    public Task EnsureNodeMirrorAsync(string workflowId, string nodeKey, string? symbol = null, CancellationToken ct = default)
        => EnsureResourceAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Node, Id = NodeRelationFactory.NodeId(workflowId, nodeKey), Location = workflowId },
            symbol ?? nodeKey, ct);

    public Task RemoveNodeMirrorAsync(string workflowId, string nodeKey, CancellationToken ct = default)
        => _graph.RemoveResourceCascadeAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Node, Id = NodeRelationFactory.NodeId(workflowId, nodeKey), Location = workflowId }, ct);

    // ---- task 镜像（task，Id=taskKey） ----

    public Task EnsureTaskMirrorAsync(string taskKey, CancellationToken ct = default)
        => EnsureResourceAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey }, taskKey, ct);

    public Task RemoveTaskMirrorAsync(string taskKey, CancellationToken ct = default)
        => _graph.RemoveResourceCascadeAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey }, ct);

    // ---- 会话图子图（方案 C：会话 = LiteDB 引用关系连接的节点；Id = session.{name}） ----
    // 「新建/刷新/载入历史」均为对引用关系的操作：新建=写根端点；刷新=枚举 Session 端点；载入=投影子图。
    // 写边唯一 owner 仍是 _graph（coordinator），本守卫只编排（SingleImplementation/TH-12）。

    /// <summary>会话根端点：幂等建 session.{name} 端点镜像（会话图子图锚点；缺省=无会话时的图条目展示锚）。</summary>
    public Task EnsureSessionMirrorAsync(string sessionName, CancellationToken ct = default)
        => EnsureResourceAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Session, Id = SessionNodeId(sessionName) },
            SessionNodeId(sessionName), ct);

    /// <summary>移除会话根端点镜像（级联清边；删除会话即删除其引用图锚点）。</summary>
    public Task RemoveSessionMirrorAsync(string sessionName, CancellationToken ct = default)
        => _graph.RemoveResourceCascadeAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Session, Id = SessionNodeId(sessionName) }, ct);

    /// <summary>会话归属边（session_owns）：把业务节点（消息/工作流产出）挂到会话根（幂等加法）。
    /// 与 <see cref="EnsureTaskNodeLinkAsync"/>（task 作用域）正交：本边以会话根为锚，不依赖任务池行。</summary>
    public async Task AddSessionMessageLinkAsync(string sessionName, string workflowId, string nodeKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        await EnsureSessionMirrorAsync(sessionName, ct).ConfigureAwait(false);
        var sessionId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Session, Id = SessionNodeId(sessionName), Location = workflowId };
        var nodeId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Node, Id = NodeRelationFactory.NodeId(workflowId, nodeKey), Location = workflowId };
        await AttachEdgeAsync(CrossDbReferenceTypes.SessionOwns, sessionId, nodeId, $"{sessionName}|{nodeKey}", ct).ConfigureAwait(false);
    }

    /// <summary>会话子图节点键集：按 session_owns 出边返回会话挂载的业务节点键（会话作用域寻址，根除跨会话污染）。
    /// 端点 Id = <c>{wf}.{nodeKey}</c>；裸键还原的唯一实现 = <see cref="HierarchyResourceKeys.TryParseNodeKey"/>
    /// （端点 Id 约定单源 = 层 2，禁本地前缀拼接/切片）。</summary>
    public async Task<IReadOnlyList<string>> GetSessionNodeKeysAsync(string sessionName, string workflowId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionName)) return Array.Empty<string>();
        var sessionId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Session, Id = SessionNodeId(sessionName), Location = workflowId };
        var targets = await GetOutgoingTargetIdsAsync(CrossDbReferenceTypes.SessionOwns, sessionId, ct).ConfigureAwait(false);
        var keys = new List<string>();
        foreach (var id in targets)
            if (HierarchyResourceKeys.TryParseNodeKey(id, workflowId, out var nodeKey)) keys.Add(nodeKey);
        return keys.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>会话子图投影（时序序，**唯一实现**）：按 <see cref="SessionMessageKeys.OrderComparer"/>
    /// （round → seq → 键名）升序投影消息节点键（<c>Order</c> 保留轮内序号，供只关心轮内位置的消费方）。
    /// 缺失会话/无挂载返回空。</summary>
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

    // ---- 历史 class 容器子图（History = Task 更上层的容器，归拢多 task 多 node） ----
    // 「创建历史 / 归拢任务 / 解除归拢」均为对引用关系的操作：创建=写根端点；归拢=写 history→task 边（history_owns_task）。
    // 写边唯一 owner 仍是 _graph（coordinator），本守卫只编排（SingleImplementation/TH-12）。历史成员集由出边集合推导，图即真源。

    /// <summary>历史 class 容器根端点：幂等建 history.{historyId} 端点镜像（History=Task 更上层的 class 容器锚点）。</summary>
    public Task EnsureHistoryMirrorAsync(string historyId, CancellationToken ct = default)
        => EnsureResourceAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.History, Id = HistoryNodeId(historyId) },
            HistoryNodeId(historyId), ct);

    /// <summary>移除历史根端点镜像（级联清边；删除历史即删除其引用图锚点）。</summary>
    public Task RemoveHistoryMirrorAsync(string historyId, CancellationToken ct = default)
        => _graph.RemoveResourceCascadeAsync(
            new ResourceIdentifier { ResourceType = NodeResourceTypes.History, Id = HistoryNodeId(historyId) }, ct);

    /// <summary>历史归拢任务：history.{historyId} → task（taskKey）引用边（幂等加法；一个历史归拢多个任务）。
    /// 先幂等建历史根端点 + task 端点，再写 history_owns_task 边。</summary>
    public async Task AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        await EnsureHistoryMirrorAsync(historyId, ct).ConfigureAwait(false);
        await EnsureTaskMirrorAsync(taskKey, ct).ConfigureAwait(false);
        var historyId2 = new ResourceIdentifier { ResourceType = NodeResourceTypes.History, Id = HistoryNodeId(historyId) };
        var taskId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey };
        await AttachEdgeAsync(CrossDbReferenceTypes.HistoryOwnsTask, historyId2, taskId, $"{historyId}|{taskKey}", ct).ConfigureAwait(false);
    }

    /// <summary>解除历史归拢任务边（历史 → task 的 history_owns_task 出边移除）。</summary>
    public Task DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
        => DetachEdgeAsync(
            CrossDbReferenceTypes.HistoryOwnsTask,
            new ResourceIdentifier { ResourceType = NodeResourceTypes.History, Id = HistoryNodeId(historyId) },
            new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey }, ct);

    /// <summary>按历史查 history_owns_task 出边，返回该历史归拢的任务键集（History class 容器成员）。</summary>
    public async Task<IReadOnlyList<string>> GetHistoryTasksAsync(string historyId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(historyId)) return Array.Empty<string>();
        var historyNodeId = new ResourceIdentifier { ResourceType = NodeResourceTypes.History, Id = HistoryNodeId(historyId) };
        return await GetOutgoingTargetIdsAsync(CrossDbReferenceTypes.HistoryOwnsTask, historyNodeId, ct).ConfigureAwait(false);
    }

    /// <summary>历史 Id 单一权威：history.{historyId}（构造与还原见 <see cref="HierarchyResourceKeys"/>）。</summary>
    private static string HistoryNodeId(string historyId) => HierarchyResourceKeys.History(historyId);

    /// <summary>【**已退役**（W1）】<c>history_owns_node</c> 写点已移除：该边降级为**派生视图**
    /// （历史节点集 = 其全部归拢任务的 task→node 边并集，读侧见 <see cref="GetHistoryNodeKeysAsync"/>）。
    /// 调用本方法即语义错误 ⇒ **抛**（W-4「缺失即抛」，禁静默 no-op、禁派生视图独立存边回流）。</summary>
    public Task AttachHistoryNodeAsync(string historyId, string workflowId, string nodeKey, CancellationToken ct = default)
        => throw new NotSupportedException(
            "history_owns_node 已退役（派生视图）：历史节点集须经 GetHistoryNodeKeysAsync 按 task→node 边派生，禁独立存边");

    /// <summary>历史的工作流节点键集（历史作用域寻址）。**派生视图**（history_owns_node 已降级，不再独立存边）：
    /// = 该历史全部归拢任务的 task→node（cross_db_link）边并集。数据模型：单 history → 多 task → 多 node（M:N）。</summary>
    public async Task<IReadOnlyList<string>> GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(historyId)) return Array.Empty<string>();
        var tasks = await GetHistoryTasksAsync(historyId, ct).ConfigureAwait(false);
        if (tasks.Count == 0) return Array.Empty<string>();
        var keys = new List<string>();
        foreach (var taskKey in tasks)
            keys.AddRange(await GetTaskNodeKeysAsync(taskKey, workflowId, ct).ConfigureAwait(false));
        return keys.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>会话根 Id（委托 <see cref="SessionMessageKeys.SessionRoot"/>：唯一权威，本层不拼串）。</summary>
    private static string SessionNodeId(string sessionName) => SessionMessageKeys.SessionRoot(sessionName);

    /// <summary>轮内时序序号解析（委托 <see cref="SessionMessageKeys.ParseSeq"/>：唯一权威）。</summary>
    private static int ParseMsgSeq(string? nodeKey) => SessionMessageKeys.ParseSeq(nodeKey);

    /// <summary>镜像业务节点内容（upsert 全镜像，对齐「内容跟随」）：资源存在 → 更新 symbol/metadata；不存在 → 新增。
    /// 更新仅改 symbol/metadata、不删引用边（UpdateResourceAsync 语义），避免修改节点内容误删其关系。</summary>
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
        // G2：不再镜像实体 payload 文本（避免实体内容渗入图库、双真源）；边/拓扑是 LiteDB 唯一职责。
        // UpdateResourceAsync = update-or-insert（资源存在保留 CreatedAt、仅更 symbol/metadata），即内容跟随的 upsert 镜像。
        return _graph.UpdateResourceAsync(id, metadata, node.NodeKey, ct);
    }

    /// <summary>
    /// 权威边集写入（唯一守卫端口）：以 <paramref name="authoritative"/> 为权威边集，收敛本节点入边到权威集。
    /// 唯一 diff 执行体在协调器 <see cref="ReferenceGraphCoordinator.ApplyRelationEdgesAsync"/>（删残留 + 补缺失），
    /// 本守卫仅做编排（SingleImplementation：不复制 diff 逻辑）。仅管理本节点入边（KeyMappingToNode / NodeToNode 中本节点为目标端）。
    /// </summary>
    public Task WriteRelationsAsync(string workflowId, string nodeKey,
        IReadOnlyCollection<NodeRelation> authoritative, CancellationToken ct = default)
        => _graph.ApplyRelationEdgesAsync(workflowId, nodeKey, authoritative, ct);

    /// <summary>task 关联节点（唯一守卫端口）：委托协调器注册 task 端点 + 建 task→node 边（幂等；增量维护任务关联）。</summary>
    public Task ReconcileTaskAsync(string taskKey, IReadOnlyCollection<string> relatedNodeKeys,
        string workflowId = "", CancellationToken ct = default)
        => _graph.ReconcileTaskAsync(taskKey, relatedNodeKeys, workflowId, ct);

    /// <summary>
    /// 运行时 task 归属边（cross_db_link，复合节点引用关系）：节点产出后把「task（ur_tasks）→ 该节点（ur_nodes）」作为
    /// 跨库归属关系写进 LiteDB 引用边 —— 节点关系唯一权威在 LiteDB（对齐 MCP「复合节点引用关系」），task 作用域寻址
    /// 经 task→node 边承载（根除跨 task 污染），RelationsJson 退化为快照备份。
    /// 幂等加法（走 <see cref="ReferenceGraphCoordinator.AddReferenceAsync"/>，不并入数据流权威边集，
    /// 避免 <see cref="WriteRelationsAsync"/> 按目标节点删残留时误删本归属边）。task 端点经
    /// <see cref="EnsureTaskMirrorAsync"/> 幂等注册（存在即跳过）。与入队时 <see cref="ReconcileTaskAsync"/>
    /// 的 node_to_node 边互补：本边记录「运行时实际产出归属」，前者记录「任务声明关注的节点」。
    /// </summary>
    public async Task EnsureTaskNodeLinkAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        await EnsureTaskMirrorAsync(taskKey, ct).ConfigureAwait(false);
        var taskId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey, Location = workflowId };
        var nodeId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Node, Id = NodeRelationFactory.NodeId(workflowId, nodeKey), Location = workflowId };
        await AttachEdgeAsync(CrossDbReferenceTypes.CrossDbLink, taskId, nodeId, $"{taskKey}|{nodeKey}", ct).ConfigureAwait(false);
    }

    /// <summary>解除 task 与节点的关联边（M:N 撤销）：移除 task→node 的 cross_db_link 出边（幂等：不存在则无副作用）。
    /// 真多对多语义下节点可被多 task 共享，本操作仅解除本 task 的归属、不影响其它 task 的关联。</summary>
    public Task UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        var taskId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey, Location = workflowId };
        var nodeId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Node, Id = NodeRelationFactory.NodeId(workflowId, nodeKey), Location = workflowId };
        return DetachEdgeAsync(CrossDbReferenceTypes.CrossDbLink, taskId, nodeId, ct);
    }

    /// <summary>
    /// 按 task 查 cross_db_link 出边，返回本 task 归属的节点键集（task 作用域寻址，更高抽象层）：
    /// 读侧「过滤枚举」据此只取本 task 可达节点，根除跨 task 污染。与全量枚举并存——调用方无 taskKey 时回退全量。
    /// 端点 Id = <c>{wf}.{nodeKey}</c>；裸键还原的唯一实现 = <see cref="HierarchyResourceKeys.TryParseNodeKey"/>
    /// （端点 Id 约定单源 = 层 2，禁本地前缀拼接/切片）。
    /// </summary>
    public async Task<IReadOnlyList<string>> GetTaskNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return Array.Empty<string>();
        var taskId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey, Location = workflowId };
        var targets = await GetOutgoingTargetIdsAsync(CrossDbReferenceTypes.CrossDbLink, taskId, ct).ConfigureAwait(false);
        var keys = new List<string>();
        foreach (var id in targets)
            if (HierarchyResourceKeys.TryParseNodeKey(id, workflowId, out var nodeKey)) keys.Add(nodeKey);
        return keys.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// task → sub-task 归属边（task_owns_task，**不定深度任务树**；幂等加法）。
    /// 先幂等建父 / 子 task 端点，再写边；深度一侧由遍历预算收口（本方法不做深度限制）。
    /// 非接口成员：供组合根按需接线（与其他会话 / 历史镜像方法一致）。
    /// </summary>
    public async Task LinkSubTaskAsync(string parentTaskKey, string subTaskKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentTaskKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(subTaskKey);
        if (string.Equals(parentTaskKey, subTaskKey, StringComparison.Ordinal)) return; // 自指无意义

        await EnsureTaskMirrorAsync(parentTaskKey, ct).ConfigureAwait(false);
        await EnsureTaskMirrorAsync(subTaskKey, ct).ConfigureAwait(false);
        var parentId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = parentTaskKey };
        var childId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = subTaskKey };
        await AttachEdgeAsync(CrossDbReferenceTypes.TaskOwnsTask, parentId, childId, $"{parentTaskKey}|{subTaskKey}", ct).ConfigureAwait(false);
    }

    /// <summary>解除 task → sub-task 归属边（幂等：不存在则无副作用）。</summary>
    public Task UnlinkSubTaskAsync(string parentTaskKey, string subTaskKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentTaskKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(subTaskKey);
        var parentId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = parentTaskKey };
        var childId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = subTaskKey };
        return DetachEdgeAsync(CrossDbReferenceTypes.TaskOwnsTask, parentId, childId, ct);
    }

    /// <summary>按 task 查 task_owns_task 出边，返回其**直属**子任务键集（不定深度树的一层展开；逐层展开由调用方 / 遍历引擎推进）。</summary>
    public async Task<IReadOnlyList<string>> GetSubTaskKeysAsync(string taskKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return Array.Empty<string>();
        var taskId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey };
        return await GetOutgoingTargetIdsAsync(CrossDbReferenceTypes.TaskOwnsTask, taskId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// task → sub-task 归属边（<c>task_owns_task</c>）的**带环检测**写入口（T 批 Compose）：
    /// 自指 / 会成环（<paramref name="parentTaskKey"/> 可从 <paramref name="subTaskKey"/> 经本边到达）⇒ 拒绝且不写边
    /// （对齐 <c>AX.DependencyCycleDetection</c>：「A→B ∧ B→A ⇒ redundant」）。
    /// <para>环检测复用既有读边 <see cref="GetSubTaskKeysAsync"/>（不定深度树逐层展开，遍历预算收口，信任反转）。</para>
    /// </summary>
    /// <returns>true = 已写边；false = 拒绝（自指 / 成环 / 空键）。</returns>
    public async Task<bool> LinkSubTaskCheckedAsync(string parentTaskKey, string subTaskKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(parentTaskKey) || string.IsNullOrWhiteSpace(subTaskKey)) return false;
        if (string.Equals(parentTaskKey, subTaskKey, StringComparison.Ordinal)) return false; // 自指无意义
        if (await IsReachableAsync(subTaskKey, parentTaskKey, ct).ConfigureAwait(false)) return false; // 成环拒绝
        await LinkSubTaskAsync(parentTaskKey, subTaskKey, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>沿 <c>task_owns_task</c> 出边判定 <paramref name="target"/> 是否自 <paramref name="from"/> 可达（环检测）。
    /// 遍历预算（<see cref="SubTaskTraversalBudget"/>）收口，信任反转：不信任层次数据自终止。</summary>
    private async Task<bool> IsReachableAsync(string from, string target, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { from };
        var queue = new Queue<string>();
        queue.Enqueue(from);
        var visited = 0;
        while (queue.Count > 0 && visited++ < SubTaskTraversalBudget)
        {
            var current = queue.Dequeue();
            foreach (var next in await GetSubTaskKeysAsync(current, ct).ConfigureAwait(false))
            {
                if (string.Equals(next, target, StringComparison.Ordinal)) return true;
                if (seen.Add(next)) queue.Enqueue(next);
            }
        }
        return false;
    }

    /// <summary>子任务树遍历预算（不定深度树的信任反转上界；环检测 / 逐层展开共用）。</summary>
    private const int SubTaskTraversalBudget = 4096;

    /// <summary>
    /// 历史容器合并（T 批 Merge）：把 <paramref name="fromHistory"/> 的 <c>history_owns_task</c> 出边
    /// **先写后撤** 迁至 <paramref name="intoHistory"/>（逐条 Attach(into) → Detach(from)），幂等可重跑。
    /// <para>先写后撤（对齐设计 §3 风险 3「迁边丢数据」对策）：先保证目标容器已持边，再撤源边 ⇒ 中途失败不丢归属。</para>
    /// <para>软删（源置 revoked）属 <c>ur_relations</c> 声明层语义（D5），见后续关系声明化批（本步只迁边）。</para>
    /// </summary>
    /// <returns>迁边成功数（源容器余边数 = 原边数 - 返回值）。</returns>
    public async Task<int> MergeHistoriesAsync(string intoHistory, string fromHistory, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(intoHistory) || string.IsNullOrWhiteSpace(fromHistory)) return 0;
        if (string.Equals(intoHistory, fromHistory, StringComparison.Ordinal)) return 0;

        await EnsureHistoryMirrorAsync(intoHistory, ct).ConfigureAwait(false);
        var tasks = await GetHistoryTasksAsync(fromHistory, ct).ConfigureAwait(false);
        var moved = 0;
        foreach (var taskKey in tasks)
        {
            if (string.IsNullOrWhiteSpace(taskKey)) continue;
            await AttachHistoryTaskAsync(intoHistory, taskKey, ct).ConfigureAwait(false); // 先写
            await DetachHistoryTaskAsync(fromHistory, taskKey, ct).ConfigureAwait(false); // 后撤
            moved++;
        }
        return moved;
    }

    /// <summary>
    /// 投影候选清单（projection 层，对齐 MCP「复合节点引用关系」）：给定 task，沿 LiteDB 引用边投影出
    /// 本 task 候选节点**索引**（不携带载荷）——task 作用域寻址（cross_db_link 挂载）+ 顺序由
    /// NodeToNode 组合边推导（Kahn 拓扑，上游先于下游）。体现「节点顺序由 LiteDB 引用边决定」。
    /// <para>
    /// 环处理：主循环的跨轮依赖（<c>cline_execute.exec_result → provide_prompt</c>，下一轮）与
    /// 本轮依赖（<c>provide_prompt.system/l1/l2 → cline_execute</c>）会形成组合边环；Kahn 拓扑对
    /// 入度为 0 的节点严格定序，环内剩余节点（入度始终 &gt; 0）按 <see cref="string.CompareOrdinal(string?, string?)"/>
    /// 稳定序追加——顺序确定、不崩溃、不依赖枚举序。载荷由 selection 层按需加载，本层只出索引清单。
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<NodeProjectionItem>> ProjectTaskNodesAsync(string taskKey, string workflowId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskKey)) return Array.Empty<NodeProjectionItem>();
        var mounted = await GetTaskNodeKeysAsync(taskKey, workflowId, ct).ConfigureAwait(false);
        if (mounted.Count == 0) return Array.Empty<NodeProjectionItem>();
        var mountedSet = new HashSet<string>(mounted, StringComparer.Ordinal);

        // 每个挂载节点的 NodeToNode 下游 + 入度（源也须是挂载节点；环内入度永不归零）。
        var downstream = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in mounted) { downstream[n] = new List<string>(); indegree[n] = 0; }
        foreach (var n in mounted)
        {
            foreach (var next in await _graph.GetRelatedNodesAsync(workflowId, n, NodeRelationType.NodeToNode, outgoing: true, ct: ct).ConfigureAwait(false))
                if (mountedSet.Contains(next) && !downstream[n].Contains(next))
                {
                    downstream[n].Add(next);
                    indegree[next]++;
                }
        }

        // Kahn：入度 0 队列（稳定序）严格拓扑；环内剩余按 nodeKey 稳定序追加。
        var queue = new List<string>(mounted.Where(n => indegree[n] == 0).OrderBy(n => n, StringComparer.Ordinal));
        var result = new List<string>();
        var processed = new HashSet<string>(StringComparer.Ordinal);
        for (var qi = 0; qi < queue.Count; qi++)
        {
            var n = queue[qi];
            if (!processed.Add(n)) continue; // InsertSorted 在遍历中插入 → 跳过已处理，避免重复入 result
            result.Add(n);
            foreach (var next in downstream[n])
                if (--indegree[next] == 0)
                    InsertSorted(queue, next);
        }
        foreach (var n in mounted.OrderBy(x => x, StringComparer.Ordinal))
            if (!result.Contains(n))
                result.Add(n); // 环内节点：稳定序（nodeKey 升序），顺序确定、不崩溃

        return result.Select((nk, idx) => new NodeProjectionItem(nk, workflowId, idx + 1)).ToList();
    }

    /// <summary>Kahn 队列按 nodeKey 有序插入，保证同级节点相对顺序确定（不依赖枚举/迭代序）。</summary>
    private static void InsertSorted(List<string> queue, string value)
    {
        var i = queue.FindIndex(x => string.CompareOrdinal(x, value) > 0);
        if (i < 0) queue.Add(value); else queue.Insert(i, value);
    }

    /// <summary>工作流静态引用（组合 + 控制流）幂等写入：建端点 → 删同端点同 refType 旧边 → 新增。
    /// 与 <see cref="WriteRelationsAsync"/>（节点关系权威边集）互补互不干扰：本路径服务「静态引用」，
    /// 经协调器 <see cref="ReferenceGraphCoordinator.AddReferenceAsync"/> 单一写边原语（幂等），不复制写边逻辑。</summary>
    public async Task WriteStaticReferencesAsync(string workflowId,
        IReadOnlyCollection<StaticReference> references, CancellationToken ct = default)
    {
        if (references is null || references.Count == 0) return;
        foreach (var r in references)
        {
            if (string.IsNullOrWhiteSpace(r.RefType) || string.IsNullOrWhiteSpace(r.SourceId) || string.IsNullOrWhiteSpace(r.TargetId))
                continue;
            var src = new ResourceIdentifier { ResourceType = r.SourceResourceType, Id = r.SourceId, Location = workflowId };
            var tgt = new ResourceIdentifier { ResourceType = r.TargetResourceType, Id = r.TargetId, Location = workflowId };
            // 端点镜像（幂等：已存在跳过）：工作流端点 + 步骤节点端点，保证图可读性（不造裸资源）。
            await _graph.EnsureResourceAsync(src, r.SourceId, null, ct).ConfigureAwait(false);
            await _graph.EnsureResourceAsync(tgt, r.TargetId, null, ct).ConfigureAwait(false);
            // 幂等写边：先删同端点同 refType 旧边（防重复累积），再新增（对齐 AddRelationAsync 幂等语义）。
            await _graph.RemoveReferenceAsync(src, tgt, r.RefType, ct).ConfigureAwait(false);
            await _graph.AddReferenceAsync(src, tgt, r.RefType, r.Strength, ct).ConfigureAwait(false);
        }
    }

    /// <summary>主动声明关系（G1）：批量写 LiteDB <c>ur_relations</c>（USharp [Record]，_id=SQL NodeKey）+ 推导边写入图（幂等）。
    /// 声明权威 = LiteDB（设计 §2），不再依赖节点库/SQL。</summary>
    public async Task DeclareRelationsAsync(string workflowId,
        IReadOnlyCollection<RelationDeclarationEntry> declarations, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        foreach (var entry in declarations)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.RelationId)) continue;
            var decl = entry.Declaration ?? new RelationDeclaration();
            var nodeKey = RelationDeclarationKeys.NodeKey(workflowId, entry.RelationId);
            // ① 声明即权威：写 LiteDB ur_relations（_id = SQL NodeKey 文法，幂等 upsert）。
            await _graph.UpsertRelationDeclarationAsync(nodeKey, ToRelationDoc(workflowId, entry.RelationId, nodeKey, decl), ct).ConfigureAwait(false);

            // ② 推导边写入图（active 声明建边；revoked 声明清边；幂等 Add/RemoveRelationAsync）。
            var edge = NodeRelationDeriver.DeriveEdgeFromDeclaration(workflowId, decl);
            if (edge is { } e)
            {
                if (decl.IsActive) await _graph.AddRelationAsync(workflowId, e, ct).ConfigureAwait(false);
                else await _graph.RemoveRelationAsync(workflowId, e, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>撤销声明（G1/D5）：软删置 status=revoked（只增，保留记录）+ 清该声明推导边。声明权威在 LiteDB。</summary>
    public async Task UndeclareRelationsAsync(string workflowId,
        IReadOnlyCollection<string> relationIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(relationIds);
        foreach (var relationId in relationIds)
        {
            if (string.IsNullOrWhiteSpace(relationId)) continue;
            var nodeKey = RelationDeclarationKeys.NodeKey(workflowId, relationId);
            var doc = await _graph.GetRelationDeclarationAsync(nodeKey, ct).ConfigureAwait(false);
            if (doc is null) continue;

            // ① 清边：按原声明推导边移除（撤销 = 图库不再多出该声明关系）。
            var edge = NodeRelationDeriver.DeriveEdgeFromDeclaration(workflowId, ReferenceGraphCoordinator.ToDeclaration(doc));
            if (edge is not null)
                await _graph.RemoveRelationAsync(workflowId, edge, ct).ConfigureAwait(false);

            // ② 软删：status=revoked（记录保留，对齐 InformationMonotonicity）。
            await _graph.RevokeRelationDeclarationAsync(nodeKey, ct).ConfigureAwait(false);
        }
    }

    /// <summary>列出全部声明（含状态）；workflowId 为空则列全部（LiteDB ur_relations）。</summary>
    public async Task<IReadOnlyList<RelationDeclarationEntry>> ListRelationsAsync(
        string? workflowId = null, CancellationToken ct = default)
    {
        var docs = await _graph.GetRelationDeclarationsAsync(workflowId, ct).ConfigureAwait(false);
        var result = new List<RelationDeclarationEntry>(docs.Count);
        foreach (var d in docs)
            result.Add(new RelationDeclarationEntry { RelationId = d.RelationId, Declaration = ReferenceGraphCoordinator.ToDeclaration(d) });
        return result;
    }

    /// <summary>
    /// 装配期声明 seed（G5）：把配置声明源（.ushar 工作流声明）的静态边固化进 LiteDB <c>ur_relations</c>，
    /// 使运行时声明权威 = LiteDB（冷启动不再依赖配置源逐次求值）。幂等可重复。
    /// </summary>
    public Task SeedDeclarationsFromAsync(IDeclaredReferenceSource source, CancellationToken ct = default)
        => SeedDeclarationsFromAsync(source, null, ct);

    /// <summary>
    /// 装配期声明 seed（G5，含 node 数据流）：静态边 + <c>nodeKeysByWorkflow</c> 指定的节点数据流/归属关系。
    /// <para><paramref name="nodeKeysByWorkflow"/> 由 Agent 侧自工作流登记端口（<c>IWorkflowRegistrar</c>，USharp 声明投影）
    /// 枚举后以**纯数据**供入 —— 存储层不引用引擎层（守同层隔离）。缺省 <c>null</c> ⇒ 仅 seed 静态边（零行为变更）。</para>
    /// 幂等可重复。
    /// </summary>
    public async Task SeedDeclarationsFromAsync(
        IDeclaredReferenceSource source,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? nodeKeysByWorkflow,
        CancellationToken ct = default)
    {
        if (source is null) return;
        foreach (var workflowId in source.WorkflowIds)
        {
            if (string.IsNullOrWhiteSpace(workflowId)) continue;

            // ① 静态边（组合 Owns / 控制流 Calls / 位置 at·contains 等）。
            var statics = source.GetStaticEdges(workflowId);
            if (statics is { Count: > 0 })
            {
                var index = 0;
                foreach (var s in statics)
                {
                    if (s is null || string.IsNullOrWhiteSpace(s.SourceId) || string.IsNullOrWhiteSpace(s.TargetId)) continue;
                    var relationId = $"{s.RefType}-s{index++}";
                    var nodeKey = RelationDeclarationKeys.NodeKey(workflowId, relationId);
                    await _graph.UpsertRelationDeclarationAsync(nodeKey, new RelationDoc
                    {
                        RelationId = relationId,
                        WorkflowId = workflowId,
                        NodeKey = nodeKey,
                        Source = s.SourceId,
                        SourceType = s.SourceResourceType,
                        Target = s.TargetId,
                        TargetType = s.TargetResourceType,
                        RefType = s.RefType,
                        Status = "active",
                    }, ct).ConfigureAwait(false);

                    // 原始静态边入图（不经 DeriveEdgeFromDeclaration：静态 refType 不属 NodeRelationType 两类）。
                    var src = new ResourceIdentifier { ResourceType = s.SourceResourceType, Id = s.SourceId, Location = workflowId };
                    var tgt = new ResourceIdentifier { ResourceType = s.TargetResourceType, Id = s.TargetId, Location = workflowId };
                    await _graph.EnsureResourceAsync(src, s.SourceId, null, ct).ConfigureAwait(false);
                    await _graph.EnsureResourceAsync(tgt, s.TargetId, null, ct).ConfigureAwait(false);
                    await _graph.AddReferenceAsync(src, tgt, s.RefType, s.Strength, ct).ConfigureAwait(false);
                }
            }

            // ② node 数据流/归属关系（G5 补：按工作流枚举节点键 → GetNodeRelations → 声明 + 推导边，复用 DeclareRelationsAsync 幂等）。
            if (nodeKeysByWorkflow is null
                || !nodeKeysByWorkflow.TryGetValue(workflowId, out var nodeKeys)
                || nodeKeys is not { Count: > 0 })
                continue;

            foreach (var nodeKey in nodeKeys)
            {
                if (string.IsNullOrWhiteSpace(nodeKey)) continue;
                var relations = source.GetNodeRelations(workflowId, nodeKey);
                if (relations is null || relations.Count == 0) continue;

                var entries = new List<RelationDeclarationEntry>(relations.Count);
                var ordinal = 0;
                foreach (var r in relations)
                {
                    if (r is null || string.IsNullOrWhiteSpace(r.SourceId) || string.IsNullOrWhiteSpace(r.TargetId)) continue;
                    var refType = r.Type == NodeRelationType.KeyMappingToNode
                        ? Modules.Node.Relations.ReferenceTypes.KeyMappingToNode
                        : Modules.Node.Relations.ReferenceTypes.NodeToNode;
                    entries.Add(new RelationDeclarationEntry
                    {
                        RelationId = $"{refType}-n{nodeKey}-{ordinal++}",
                        Declaration = new RelationDeclaration
                        {
                            Source = new RelationEndpoint { ResourceType = r.SourceResourceType, Id = r.SourceId, Port = r.SourcePort },
                            Target = new RelationEndpoint { ResourceType = r.TargetResourceType, Id = r.TargetId, Port = r.TargetPort },
                            RefType = refType,
                            KeyMappingRef = r.KeyMappingRef,
                            Status = "active",
                        },
                    });
                }

                if (entries.Count > 0)
                    await DeclareRelationsAsync(workflowId, entries, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>全量重建（G3/D4）：清空图库 → 从 LiteDB 自身集合重灌（声明边自 ur_relations 重建；不读 SQL）。幂等可重复。</summary>
    public async Task RebuildAllAsync(CancellationToken ct = default)
    {
        await _graph.ClearAllAsync(ct).ConfigureAwait(false);
        await RebuildDeclarationsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>从 LiteDB 声明集合（ur_relations，active）重建声明关系边（幂等 AddRelationAsync；不读 SQL）。</summary>
    private async Task RebuildDeclarationsAsync(CancellationToken ct)
    {
        var docs = await _graph.GetRelationDeclarationsAsync(null, ct).ConfigureAwait(false);
        foreach (var d in docs)
        {
            if (!d.IsActive) continue;
            var edge = NodeRelationDeriver.DeriveEdgeFromDeclaration(d.WorkflowId, ReferenceGraphCoordinator.ToDeclaration(d));
            if (edge is not null)
                await _graph.AddRelationAsync(d.WorkflowId, edge, ct).ConfigureAwait(false);
        }
    }

    /// <summary>构造声明文档（G1）：_id=SQL NodeKey 文法；键构造只经 <see cref="RelationDeclarationKeys"/> 单源。</summary>
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
        Status = string.IsNullOrWhiteSpace(d.Status) ? "active" : d.Status,
    };

    /// <summary>对账（单节点 / 全图两级），返回处理数（G3：以 LiteDB 声明集合为参照，不读 SQL）。
    /// 单节点：按 ur_relations 中目标为该节点的 active 声明重推导并入边；全图：全量 active 声明重推导并幂等写边。</summary>
    public async Task<int> ReconcileAsync(MaintenanceScope scope, string? workflowId = null, string? nodeKey = null, CancellationToken ct = default)
    {
        if (scope == MaintenanceScope.Node)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
            ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
            var nodeId = NodeRelationFactory.NodeId(workflowId, nodeKey);
            var edges = _graph.GetActiveRelationEdges(workflowId)
                .Where(e => string.Equals(e.TargetId, nodeId, StringComparison.Ordinal))
                .ToList();
            if (edges.Count == 0) return 0; // 无权威边 → 不越权对账
            await WriteRelationsAsync(workflowId, nodeKey, edges, ct).ConfigureAwait(false);
            return 1;
        }

        // All：从 ur_relations 全量 active 声明重推导边并幂等写入（不读 SQL）。
        var docs = await _graph.GetRelationDeclarationsAsync(null, ct).ConfigureAwait(false);
        var processed = 0;
        foreach (var d in docs)
        {
            if (!d.IsActive) continue;
            var edge = NodeRelationDeriver.DeriveEdgeFromDeclaration(d.WorkflowId, ReferenceGraphCoordinator.ToDeclaration(d));
            if (edge is null) continue;
            await _graph.AddRelationAsync(d.WorkflowId, edge, ct).ConfigureAwait(false);
            processed++;
        }
        return processed;
    }

    /// <summary>端点镜像幂等（GetResourceAsync 已存在即跳过；唯一键冲突容错），收敛到 图协调器 EnsureResourceAsync。</summary>
    private Task EnsureResourceAsync(ResourceIdentifier id, string? symbol, CancellationToken ct)
        => _graph.EnsureResourceAsync(id, symbol, null, ct);

    // ---- IHierarchyReferenceManager（三层层级引用唯一中立端口：history(1)→task(N)→node(M:N)，历史节点集派生）----

    Task IHierarchyReferenceManager.EnsureHistoryAsync(string historyId, CancellationToken ct)
        => EnsureHistoryMirrorAsync(historyId, ct);

    Task<bool> IHierarchyReferenceManager.DeleteHistoryAsync(string historyId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(historyId)) return Task.FromResult(false);
        return RemoveHistoryMirrorAsync(historyId, ct).ContinueWith(
            t => !t.IsCanceled && !t.IsFaulted,
            ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    Task IHierarchyReferenceManager.AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct)
        => AttachHistoryTaskAsync(historyId, taskKey, ct);

    Task IHierarchyReferenceManager.DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct)
        => DetachHistoryTaskAsync(historyId, taskKey, ct);

    Task<IReadOnlyList<string>> IHierarchyReferenceManager.GetHistoryTaskKeysAsync(string historyId, CancellationToken ct)
        => GetHistoryTasksAsync(historyId, ct);

    Task IHierarchyReferenceManager.LinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct)
        => EnsureTaskNodeLinkAsync(taskKey, workflowId, nodeKey, ct);

    Task IHierarchyReferenceManager.UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct)
        => UnlinkTaskNodeAsync(taskKey, workflowId, nodeKey, ct);

    Task<IReadOnlyList<string>> IHierarchyReferenceManager.GetTaskNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct)
        => GetTaskNodeKeysAsync(taskKey, workflowId, ct);

    Task<IReadOnlyList<string>> IHierarchyReferenceManager.GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct)
        => GetHistoryNodeKeysAsync(historyId, workflowId, ct);

    Task<IReadOnlyList<string>> IHierarchyReferenceManager.GetSessionNodeKeysAsync(string sessionName, string workflowId, CancellationToken ct)
        => GetSessionNodeKeysAsync(sessionName, workflowId, ct);

    Task<IReadOnlyList<NodeProjectionItem>> IHierarchyReferenceManager.ProjectSessionNodesAsync(string sessionName, string workflowId, CancellationToken ct)
        => ProjectSessionNodesAsync(sessionName, workflowId, ct);
}
