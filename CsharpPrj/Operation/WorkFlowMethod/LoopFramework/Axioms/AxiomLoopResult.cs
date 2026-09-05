namespace WorkFlowMethod.LoopFramework.Axioms;

/// <summary>
/// 循环运行结果 —— Agent 全系统公共结果契约（AgentRunner / AgentOrchestrator / TaskPool / Launcher 共享）。
/// 定义独立于循环引擎：统一确定性循环通道收敛到 <c>Share.TaskStatusPool.Loops.SnapshotDrivenLoopEngine</c> 后，
/// 本类型仍作为对外结果载体，保证调用方契约不变。
/// </summary>
public sealed class AxiomLoopResult
{
    public LoopDecision FinalDecision { get; set; }

    public ILoopState? FinalContext { get; init; }

    public List<AxiomRound> Rounds { get; } = new();

    public TimeSpan TotalDuration { get; set; }
}
