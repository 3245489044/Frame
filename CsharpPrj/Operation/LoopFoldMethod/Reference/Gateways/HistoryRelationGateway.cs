using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.LoopFoldMethod.Reference.Gateways;

/// <summary>
/// 历史关系编排门面（Reference 空间维度，中性编排 owner）—— 收敛自 <c>HierarchyMethod.History.HistoryRelationManager</c>：
/// History=Task 更上层的 class 容器的逻辑关系编排（建历史 / 归拢任务 / 挂载节点 / 时序 / 查询）。
/// <para>
/// 职责边界（SingleImplementation / TH-12）：本门面**只做编排**，写边原语委托 <see cref="IReferenceMaintenance"/>
/// （唯一守卫 → 图协调器纯写边），读边经 <see cref="IResourceReferenceStore"/>；不复制任何写边逻辑。
/// 中性接口：一律以字符串键 / 字符串集进出，不承载 HierarchyMethod 领域 DTO（<c>HistoryRecord</c>/<c>HistoryTaskLink</c>
/// 由消费方适配映射），使 Reference 层不认识 HierarchyMethod 具体类型（防反向依赖）。
/// </para>
/// <para>
/// 装配可空：未装配守卫/图则返回空集 / no-op，不阻断 DB-only 路径（对齐「未装配返回诊断」模式）。
/// 实体真源在 SQL（ur_histories / ur_tasks / ur_nodes），引用图（LiteDB graph-log.db）只存关系投影，可 graph.rebuild 幂等重建。
/// </para>
/// </summary>
public sealed class HistoryRelationGateway
{
    private const string HistoryIdPrefix = "history.";

    private readonly IReferenceMaintenance? _maintenance;
    private readonly IResourceReferenceStore? _graph;

    /// <param name="maintenance">引用维护守卫（可空；未装配则编排 no-op）。</param>
    /// <param name="graph">引用图读侧（可空；未装配则枚举/查询返回空集）。</param>
    public HistoryRelationGateway(IReferenceMaintenance? maintenance, IResourceReferenceStore? graph)
    {
        _maintenance = maintenance;
        _graph = graph;
    }

    /// <summary>幂等建历史根端点；未装配守卫返回仅含 Id 的空视图（不写图）。</summary>
    public async Task<string> CreateAsync(string historyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyId);
        if (_maintenance is null) return historyId;
        await _maintenance.EnsureHistoryMirrorAsync(historyId, ct).ConfigureAwait(false);
        return historyId;
    }

    /// <summary>删除历史（级联清其引用边；未装配守卫返回 false）。</summary>
    public async Task<bool> DeleteAsync(string historyId, CancellationToken ct = default)
    {
        if (_maintenance is null) return false;
        await _maintenance.RemoveHistoryMirrorAsync(historyId, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>历史归拢任务（history → task，history_owns_task 边；幂等）。</summary>
    public Task AttachTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
        => _maintenance is null ? Task.CompletedTask : _maintenance.AttachHistoryTaskAsync(historyId, taskKey, ct);

    /// <summary>解除历史归拢任务边。</summary>
    public Task DetachTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
        => _maintenance is null ? Task.CompletedTask : _maintenance.DetachHistoryTaskAsync(historyId, taskKey, ct);

    /// <summary>历史挂载节点产出（history → node，history_owns_node 边；幂等）。
    /// **已降级**：历史节点集改为派生视图（经任务归属推导），本写路径仅保留向后兼容；新写请用 <see cref="LinkNodeAsync"/>。</summary>
    public Task AttachNodeAsync(string historyId, string workflowId, string nodeKey, CancellationToken ct = default)
        => _maintenance is null ? Task.CompletedTask : _maintenance.AttachHistoryNodeAsync(historyId, workflowId, nodeKey, ct);

    /// <summary>task → node 关联（cross_db_link，真多对多；幂等加法）。同一节点可被多 task 共享引用。
    /// 历史节点集经此边派生，故本方法是历史挂载节点的**新权威写路径**。</summary>
    public Task LinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _maintenance is null ? Task.CompletedTask : _maintenance.EnsureTaskNodeLinkAsync(taskKey, workflowId, nodeKey, ct);

    /// <summary>解除 task → node 关联（M:N 撤销边；仅解除本 task 归属，不影响其它 task）。</summary>
    public Task UnlinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _maintenance is null ? Task.CompletedTask : _maintenance.UnlinkTaskNodeAsync(taskKey, workflowId, nodeKey, ct);

    /// <summary>消息时序边（node → node，round_sequence；历史顺序由引用边决定）。</summary>
    public Task AddSequenceAsync(string workflowId, string fromNode, string toNode, CancellationToken ct = default)
        => _maintenance is null ? Task.CompletedTask : _maintenance.AddRoundSequenceEdgeAsync(workflowId, fromNode, toNode, ct);

    /// <summary>会话归属边（session_owns）：把会话消息/产出节点挂到会话根（入史层挂载；幂等加法）。</summary>
    public Task AddSessionMessageLinkAsync(string sessionName, string workflowId, string nodeKey, CancellationToken ct = default)
        => _maintenance is null ? Task.CompletedTask : _maintenance.AddSessionMessageLinkAsync(sessionName, workflowId, nodeKey, ct);

    /// <summary>返回该历史归拢的任务键集（History class 容器成员）。</summary>
    public async Task<IReadOnlyList<string>> GetTaskKeysAsync(string historyId, CancellationToken ct = default)
    {
        if (_maintenance is null) return Array.Empty<string>();
        return await _maintenance.GetHistoryTasksAsync(historyId, ct).ConfigureAwait(false);
    }

    /// <summary>返回该历史挂载的业务节点键（历史作用域寻址）。</summary>
    public async Task<IReadOnlyList<string>> GetNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
    {
        if (_maintenance is null) return Array.Empty<string>();
        return await _maintenance.GetHistoryNodeKeysAsync(historyId, workflowId, ct).ConfigureAwait(false);
    }

    /// <summary>列出全部历史 Id（枚举图中 ResourceType=history 端点，去 history. 前缀）。</summary>
    public async Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default)
    {
        if (_graph is null) return Array.Empty<string>();
        var resources = await _graph.GetAllResourcesAsync(ct).ConfigureAwait(false);
        return resources
            .Where(r => string.Equals(r.Identifier.ResourceType, NodeResourceTypes.History, StringComparison.Ordinal))
            .Select(r => r.Identifier.Id ?? string.Empty)
            .Where(id => id.StartsWith(HistoryIdPrefix, StringComparison.Ordinal))
            .Select(id => id.Substring(HistoryIdPrefix.Length))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }
}
