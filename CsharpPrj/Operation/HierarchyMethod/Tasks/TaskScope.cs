using System.Collections.Generic;

namespace Operation.HierarchyMethod.Tasks;

/// <summary>
/// 一轮实例化的任务作用域（Tasks 层复用）：种子数据空间 + 已归并历史键集 + 轮状态头。
/// 承载「把一份工作流（<see cref="Nodes.Workflows.RoundSpec"/>）被不同 task/会话反复实例化」的显式作用域。
/// <see cref="Seeds"/> 为 <c>RoundStateMapper.Seed</c> 的通用形态（种子键值字典），
/// 由 <see cref="ITaskScopeProvider.ResolveTaskScopeAsync"/> 产出，供 Nodes 层 <see cref="Nodes.Execution.IRoundStatePort.SeedAsync"/> 注入轮内数据空间。
/// </summary>
public sealed record TaskScope(
    string TaskKey,
    string? SessionId,
    int Round,
    IReadOnlyDictionary<string, object?> Seeds,
    string? HistoryId = null);
