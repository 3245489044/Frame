using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Tasks;

/// <summary>
/// 任务作用域提供器（Tasks 层）：给定工作流模板 + taskKey/session，解析本轮种子状态供 Nodes 层编排消费。
/// 把 <c>RoundStateMapper.Seed/Harvest</c> 通用化——种子提取（<see cref="ResolveTaskScopeAsync"/> 产出 <see cref="TaskScope.Seeds"/>）
/// 是本端口的单一职责；轮内注入/收割由 <see cref="Nodes.Execution.IRoundStatePort"/> 消费本端口产出完成（方案 A 收敛）。
/// Agent 侧以 StateCenter / AxiomContext（RoundStateMapper）适配实现；框架本体零 LLM。
/// 未装配 → 消费方走纯内存 DataSpace 路径（种子为空字典，seed/harvest no-op）。
/// </summary>
public interface ITaskScopeProvider
{
    /// <summary>
    /// 给定工作流模板 + taskKey/session，解析本轮任务作用域（种子数据空间 + 历史归拢键 + 轮状态头）。
    /// <paramref name="spec"/> 为工作流模板（<see cref="RoundSpec"/>；声明式数据，仅用于键命名/作用域派生约定）。
    /// </summary>
    Task<TaskScope> ResolveTaskScopeAsync(
        RoundSpec spec, string taskKey, string? sessionId, CancellationToken ct);
}
