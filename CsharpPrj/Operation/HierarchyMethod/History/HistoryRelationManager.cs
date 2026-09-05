using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.LoopFoldMethod.Reference.Gateways;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.HierarchyMethod.History;

/// <summary>
/// 历史关系管理实现（Agent 专用引用关系项目）—— 管理 task / nodes / history 之间的逻辑关系，
/// History = Task 更上层的 class 容器（一个历史归拢多个任务）。
/// <para>
/// 职责边界（SingleImplementation / TH-12）：本实现为**薄转发门面**，编排逻辑已收敛为
/// <see cref="HistoryRelationGateway"/>（Reference.Gateways 单一 owner），本类型仅做端口契约适配映射
/// （<see cref="IHistoryRelationManager"/> DTO ↔ Reference 中性字符串），并保持 <see cref="IHistoryRelationManager"/>
/// 消费方端口形态不变（历史创建/删除、归拢任务、挂节点、时序、查询）。写边原语仍委托
/// <see cref="IReferenceMaintenance"/>（唯一守卫 → 图协调器纯写边），不复制任何写边逻辑。
/// </para>
/// <para>
/// 实体真源在 SQL（ur_histories / ur_tasks / ur_nodes），引用图（LiteDB graph-log.db）只存关系投影，
/// 成员集（归拢任务 / 挂载节点）由图边推导，可 graph.rebuild 幂等重建。装配可空：未装配守卫/图则返回空集/诊断，不阻断运行。
/// </para>
/// </summary>
public sealed class HistoryRelationManager : IHistoryRelationManager
{
    private readonly HistoryRelationGateway _gateway;

    public HistoryRelationManager(IReferenceMaintenance? maintenance, IResourceReferenceStore? graph)
    {
        _gateway = new HistoryRelationGateway(maintenance, graph);
    }

    /// <summary>幂等建历史根端点；未装配守卫返回仅含 Id 的空视图（不写图）。</summary>
    public async Task<HistoryRecord> CreateAsync(string historyId, string? name = null, CancellationToken ct = default)
    {
        var id = await _gateway.CreateAsync(historyId, ct).ConfigureAwait(false);
        return new HistoryRecord(id, name ?? historyId, await GetTaskKeysAsync(historyId, ct).ConfigureAwait(false));
    }

    /// <summary>删除历史（级联清其引用边；未装配守卫返回 false）。</summary>
    public Task<bool> DeleteAsync(string historyId, CancellationToken ct = default)
        => _gateway.DeleteAsync(historyId, ct);

    public Task AttachTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
        => _gateway.AttachTaskAsync(historyId, taskKey, ct);

    public Task DetachTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
        => _gateway.DetachTaskAsync(historyId, taskKey, ct);

    public Task AttachNodeAsync(string historyId, string workflowId, string nodeKey, CancellationToken ct = default)
        => _gateway.AttachNodeAsync(historyId, workflowId, nodeKey, ct);

    public Task LinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _gateway.LinkNodeAsync(taskKey, workflowId, nodeKey, ct);

    public Task UnlinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _gateway.UnlinkNodeAsync(taskKey, workflowId, nodeKey, ct);

    public Task AddSequenceAsync(string workflowId, string fromNode, string toNode, CancellationToken ct = default)
        => _gateway.AddSequenceAsync(workflowId, fromNode, toNode, ct);

    public Task<IReadOnlyList<string>> GetTaskKeysAsync(string historyId, CancellationToken ct = default)
        => _gateway.GetTaskKeysAsync(historyId, ct);

    public async Task<IReadOnlyList<HistoryTaskLink>> GetTasksAsync(string historyId, CancellationToken ct = default)
    {
        var keys = await GetTaskKeysAsync(historyId, ct).ConfigureAwait(false);
        return keys.Select(k => new HistoryTaskLink(historyId, k)).ToList();
    }

    public Task<IReadOnlyList<string>> GetNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
        => _gateway.GetNodeKeysAsync(historyId, workflowId, ct);

    /// <summary>列出全部历史 Id（枚举图中 ResourceType=history 端点，去 history. 前缀）。</summary>
    public Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default)
        => _gateway.ListHistoryIdsAsync(ct);
}
