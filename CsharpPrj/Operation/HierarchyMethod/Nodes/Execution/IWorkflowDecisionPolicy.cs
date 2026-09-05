using Share.TaskStatusPool.Loops;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 工作流决策词判定策略（LLM-free，可注入）：决策词 → <see cref="LoopDecision"/> 的映射。
/// Agent 侧以 DecisionWordClassifier 实现（continue→Continue / stop·complete→Success / 未知→null）。
/// 未装配 → 决策词判定退化为「任何非空决策词均视为继续」（继续循环语义由调用方兜底）。
/// </summary>
public interface IWorkflowDecisionPolicy
{
    /// <summary>决策词 → LoopDecision；未知/空 → null（非成功非继续，由调用方防御处理，不静默判成功）。</summary>
    LoopDecision? Map(string? word);
}
