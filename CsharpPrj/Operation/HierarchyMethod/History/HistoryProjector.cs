using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Execution;
using Operation.HierarchyMethod.Tasks;

namespace Operation.HierarchyMethod.History;

/// <summary>
/// 历史投影器默认实现（History 层，LLM-free）：把一轮编排结果沉淀到历史容器并回喂为下轮上下文。
/// 写边原语统一委托 <see cref="IHistoryRelationManager"/>（history_owns_task / session_owns / round_sequence），
/// 本实现只编排不复制写边（SingleImplementation / TH-12）。装配可空：未装配关系管理器 → 沉淀/回读均 no-op，
/// 对齐「未装配返回诊断、不阻断 DB-only 路径」模式。Agent 可注入本实现或替换为 Conversation 历史适配器。
/// </summary>
public sealed class HistoryProjector : IHistoryProjector
{
    private readonly IHistoryRelationManager? _relations;

    /// <param name="relations">历史关系管理端口（可空；未装配则沉淀/回读均跳过）。</param>
    public HistoryProjector(IHistoryRelationManager? relations = null)
    {
        _relations = relations;
    }

    /// <summary>
    /// 沉淀一轮：幂等建历史容器 → 归拢任务（history_owns_task）→ 挂载节点（session_owns）→ 消息时序边（round_sequence）。
    /// 历史容器 Id 优先取 <see cref="TaskScope.HistoryId"/>，缺失回退 taskKey（task-key 作用域历史）。
    /// </summary>
    public async Task PersistRoundAsync(NodeRoundResult round, TaskScope scope, CancellationToken ct = default)
    {
        if (_relations is null || scope is null) return;

        var historyId = string.IsNullOrWhiteSpace(scope.HistoryId) ? scope.TaskKey : scope.HistoryId;
        var taskKey = scope.TaskKey;
        await _relations.CreateAsync(historyId, ct: ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(taskKey))
            await _relations.AttachTaskAsync(historyId, taskKey, ct).ConfigureAwait(false);

        // 节点即结果：逐条以 task→node M:N 关联挂载（历史节点集派生 = 其任务节点并集，不再直写 history→node 边）
        // + 相邻节点建 round_sequence 时序边（历史顺序由引用边决定）。
        string? prev = null;
        foreach (var rec in round?.Records ?? Array.Empty<NodeExecutionRecord>())
        {
            if (string.IsNullOrWhiteSpace(rec.NodeKey)) continue;
            var workflowId = string.IsNullOrWhiteSpace(rec.WorkflowId) ? NodeKeys.DefaultWorkflowId : rec.WorkflowId;
            if (!string.IsNullOrWhiteSpace(taskKey))
                await _relations.LinkNodeAsync(taskKey, workflowId, rec.NodeKey, ct).ConfigureAwait(false);
            if (prev is not null)
                await _relations.AddSequenceAsync(workflowId, prev, rec.NodeKey, ct).ConfigureAwait(false);
            prev = rec.NodeKey;
        }
    }

    /// <summary>
    /// 从某 task 累积历史重建下一轮执行上下文（事务无关的只读重建）。本默认实现仅回喂已归拢任务键集；
    /// 完整历史投影（L0/L1/L2 提示词上下文）由 <see cref="IContextProjector"/> 层（阶段五）完成。
    /// </summary>
    public async Task<NodeRoundInput> BuildContextAsync(string taskKey, string? sessionId, CancellationToken ct = default)
    {
        var vars = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (_relations is not null && !string.IsNullOrWhiteSpace(taskKey))
        {
            // 以 taskKey 为历史容器枚举归拢任务键（历史回读的轻量形态）。
            var keys = await _relations.GetTaskKeysAsync(taskKey, ct).ConfigureAwait(false);
            if (keys is { Count: > 0 })
                vars["history_task_keys"] = keys;
        }

        return new NodeRoundInput(taskKey, sessionId, 0, vars);
    }
}
