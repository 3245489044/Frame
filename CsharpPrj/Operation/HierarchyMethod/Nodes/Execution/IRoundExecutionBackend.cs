using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 轮执行后端端口（编排层接入点）：把「一轮节点编排」的执行从具体编排器实现（如 <see cref="NodeGraphOrchestrator"/>）
/// 收口为可注入的后端，返回 <see cref="NodeRoundResult"/>。职责边界 = 「seed → 逐节点执行 + 持久化横切 → 成功准则裁决
/// → harvest → 终答归集」的整轮语义；具体「如何驱动单节点」由实现决定（Agent 应用层经该端口注入：
/// 现状 = <see cref="NodeGraphOrchestrator"/> 线性遍历；NodeProcess 翻面路径将经 NodeProcess 执行端点按图/链驱动）。
/// 依赖方向：本端口仅依赖 Nodes 层类型（<see cref="NodeRoundResult"/>/<see cref="NodeRoundContext"/>/<see cref="RoundSpec"/>），
/// 不感知 NodeProcess —— 后端实现可落在 Agent 应用层（上层），既引用 Nodes 又引用 NodeProcess，无环。
/// </summary>
public interface IRoundExecutionBackend
{
    /// <summary>执行一轮节点编排，返回节点执行快照 + 终态决策。</summary>
    Task<NodeRoundResult> RunRoundAsync(RoundSpec spec, NodeRoundContext ctx, CancellationToken ct = default);
}
