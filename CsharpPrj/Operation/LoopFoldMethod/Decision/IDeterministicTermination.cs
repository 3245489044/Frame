namespace Operation.LoopFoldMethod.Decision;

/// <summary>
/// 确定性硬性终止（决策的确定性部分 → 直接产出终止信号）。引擎内裁决，阈值到达即触发（不外置），
/// 先于 LLM skill 拦截；单一写入确定性核心（ProgressToken / FailureBudget / ControlFlag）。
/// 对齐 SEP.DeterministicCoreDisjoint / AX.TerminationBipartition（预算终止引擎内强制，不可被 LLM 提议覆盖/延迟）。
/// </summary>
/// <typeparam name="TState">状态点类型（逆变，只读判定源）。</typeparam>
public interface IDeterministicTermination<in TState>
{
    /// <summary>命中 → 终止信号（折叠应立即停止）；未命中 → null（交 LLM skill 产出 D′）。</summary>
    TerminationSignal? Check(TState state);
}
