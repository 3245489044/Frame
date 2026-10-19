using System.Collections.Generic;
using Operation.HierarchyMethod.Nodes;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 一轮节点编排的结果（<see cref="NodeGraphOrchestrator.RunRoundAsync"/> 的返回类型）：各节点执行快照 + 终态决策。
/// 供 Tasks 层收割（Harvest）与轮末归因流消费（历史投影器已退役）。
/// </summary>
public sealed record NodeRoundResult(
    IReadOnlyList<NodeExecutionRecord> Records,
    string? Decision = null,
    bool Success = false,
    string? Error = null);
