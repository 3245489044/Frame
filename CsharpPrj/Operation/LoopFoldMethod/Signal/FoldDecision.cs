namespace Operation.LoopFoldMethod.Signal;

/// <summary>
/// 决策信号 —— 规则语义（continue/stop/error）在信号域的投影（决策信号化：rule_match → FoldSignal）。
/// 对齐 clinerules 语义：<see cref="Continue"/> ↔ StepCompleted（链推进）、<see cref="Stop"/> ↔ 链终点收敛（run_to_end）、
/// <see cref="Error"/> → 终止（失败语义由调用方经 ICutoffPolicy / 错误分类处理）。
/// 决策词→决策的单一映射权威见 Agent.Operation.Loops.DecisionWordClassifier（本枚举为其信号域投影）。
/// </summary>
public enum FoldDecision
{
    /// <summary>继续下一步（链推进）。</summary>
    Continue,

    /// <summary>停止（链终点，收敛成功）。</summary>
    Stop,

    /// <summary>错误（终止；由调用方归入 ICutoffPolicy 截断/错误分类）。</summary>
    Error,
}
