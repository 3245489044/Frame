using System;
using System.Collections.Generic;

namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 循环运行结果 —— 全系统公共结果契约（AgentRunner / AgentOrchestrator / TaskPool / Launcher 共享）。
/// <para>定义独立于循环引擎：统一确定性循环通道收敛到 <see cref="SnapshotDrivenLoopEngine"/> 后，
/// 本类型仍作为对外结果载体。</para>
/// </summary>
public sealed class AxiomLoopResult
{
    /// <summary>最终决策（二值：Complete/Abort；默认 Abort = 未赋值即失败，禁默认成功）。</summary>
    public LoopDecision FinalDecision { get; set; } = LoopDecision.Abort;

    public ILoopState? FinalContext { get; init; }

    public List<AxiomRound> Rounds { get; } = new();

    public TimeSpan TotalDuration { get; set; }
}
