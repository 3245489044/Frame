using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.HierarchyMethod.History;

/// <summary>
/// 历史关系管理实现（唯一 owner）—— 管理 task / nodes / history 之间的逻辑关系，
/// History = Task 更上层的 class 容器（一个历史归拢多个任务）。
/// <para>
/// 职责边界（SingleImplementation / TH-12）：本实现**只编排**，写边原语委托 <see cref="IReferenceMaintenance"/>
/// （唯一守卫 → 图协调器纯写边），读边经 <see cref="IResourceReferenceStore"/>；不复制任何写边逻辑。
/// 原 <c>Operation.LoopFoldMethod.Reference.Gateways.HistoryRelationGateway</c> 的唯一消费者即本类（一跳空转发），
/// 已折叠进本类 —— 去一层包装，零消费者改动、零行为变化。
/// </para>
/// <para>
/// 实体真源在 SQL（ur_histories / ur_tasks / ur_nodes），引用图（LiteDB graph-log.db）只存关系投影，
/// 成员集（归拢任务 / 挂载节点）由图边推导，可 graph.rebuild 幂等重建。
/// 装配可空：未装配守卫/图则返回空集 / 诊断，不阻断运行（对齐「未装配返回诊断」模式）。
/// </para>
/// </summary>
public sealed class HistoryRelationManager : IHistoryRelationManager
{
    private readonly IReferenceMaintenance? _maintenance;
    private readonly IResourceReferenceStore? _graph;

    /// <param name="maintenance">引用维护守卫（可空；未装配则编排 no-op）。</param>
    /// <param name="graph">引用图读侧（可空；未装配则枚举/查询返回空集）。</param>
    public HistoryRelationManager(IReferenceMaintenance? maintenance, IResourceReferenceStore? graph)
    {
        _maintenance = maintenance;
        _graph = graph;
    }

    /// <summary>幂等建历史根端点；未装配守卫返回仅含 Id 的空视图（不写图）。</summary>
    public async Task<HistoryRecord> CreateAsync(string historyId, string? name = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyId);
        if (_maintenance is not null)
            await _maintenance.EnsureHistoryMirrorAsync(historyId, ct).ConfigureAwait(false);
        return new HistoryRecord(historyId, name ?? historyId, await GetTaskKeysAsync(historyId, ct).ConfigureAwait(false));
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

    /// <summary>task → node 关联（cross_db_link，真多对多；幂等加法）。同一节点可被多 task 共享引用。
    /// 历史节点集经此边派生，故本方法是历史挂载节点的**新权威写路径**。
    /// 注：原 <c>AttachNodeAsync</c>（history_owns_node 写点）已于 W1 退役（派生视图），
    /// RED-3 起端口成员一并移除 —— 该写路径已无正当调用者（禁回流）。</summary>
    public Task LinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _maintenance is null ? Task.CompletedTask : _maintenance.EnsureTaskNodeLinkAsync(taskKey, workflowId, nodeKey, ct);

    /// <summary>解除 task → node 关联（M:N 撤销边；仅解除本 task 归属，不影响其它 task）。</summary>
    public Task UnlinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _maintenance is null ? Task.CompletedTask : _maintenance.UnlinkTaskNodeAsync(taskKey, workflowId, nodeKey, ct);

    /// <summary>会话归属边（session_owns）：把会话消息/产出节点挂到会话根（入史层挂载；幂等加法）。</summary>
    public Task AddSessionMessageLinkAsync(string sessionName, string workflowId, string nodeKey, CancellationToken ct = default)
        => _maintenance is null ? Task.CompletedTask : _maintenance.AddSessionMessageLinkAsync(sessionName, workflowId, nodeKey, ct);

    /// <summary>返回该历史归拢的任务键集（History class 容器成员）。</summary>
    public async Task<IReadOnlyList<string>> GetTaskKeysAsync(string historyId, CancellationToken ct = default)
    {
        if (_maintenance is null) return Array.Empty<string>();
        return await _maintenance.GetHistoryTasksAsync(historyId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HistoryTaskLink>> GetTasksAsync(string historyId, CancellationToken ct = default)
    {
        var keys = await GetTaskKeysAsync(historyId, ct).ConfigureAwait(false);
        return keys.Select(k => new HistoryTaskLink(historyId, k)).ToList();
    }

    /// <summary>返回该历史挂载的业务节点键（历史作用域寻址；派生：= 该历史全部任务的节点并集）。</summary>
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
        var ids = new List<string>();
        foreach (var resource in resources)
        {
            if (!string.Equals(resource.Identifier.ResourceType, NodeResourceTypes.History, StringComparison.Ordinal)) continue;
            // 端点 Id → historyId 的唯一反解（单源 = HierarchyResourceKeys → 层 2；禁本地前缀拼接/切片）
            if (HierarchyResourceKeys.TryParseHistoryId(resource.Identifier.Id, out var historyId)
                && !string.IsNullOrWhiteSpace(historyId))
            {
                ids.Add(historyId);
            }
        }
        return ids.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
    }
}
