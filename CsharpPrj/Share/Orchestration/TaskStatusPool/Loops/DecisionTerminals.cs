namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 决策终态分类（单一权威，对齐 <c>AX.DecisionOutputSet</c> 预算终止域）。
/// 收敛多处字面 {TS/TO/TM} 判定（Operation.LoopFoldMethod.SpinalDecisions / 折叠投影）与终值收敛
/// （ConvergeFinal），消重 TH-12 ANTI-DUPLICATED-IMPLEMENTATION。本类为「LoopDecision 终态判定 + 终值收敛」
/// 的唯一实现，其余点一律薄委托（原 Agent 侧 AgentLoopTermination 中间层已删除）。
/// </summary>
public static class DecisionTerminals
{
    /// <summary>
    /// 是否预算终止终态（TS/TO/TM；引擎内 break 语义，阈值到达即触发）。
    /// Continue / None / SwitchStrategy 均非终态（继续推进侧）。
    /// </summary>
    public static bool IsTerminal(LoopDecision decision)
        => decision is LoopDecision.Success or LoopDecision.Timeout or LoopDecision.TripTerminated;

    /// <summary>
    /// 收敛最终决策：引擎承载的预算终止值（TS/TO/TM）优先，lastDecision（Success/Continue）兜底；
    /// 双空（None）兜 Continue。
    /// </summary>
    public static LoopDecision ConvergeFinal(LoopDecision snapshotFinal, LoopDecision lastDecision)
        => snapshotFinal != LoopDecision.None && snapshotFinal != LoopDecision.Continue
            ? snapshotFinal
            : (lastDecision != LoopDecision.None ? lastDecision : LoopDecision.Continue);
}
