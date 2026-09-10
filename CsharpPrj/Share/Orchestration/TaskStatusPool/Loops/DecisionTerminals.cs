namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 决策终态分类（单一权威，对齐 <c>AX.DecisionOutputSet</c> 预算终止域）。
/// 收敛三处字面 {TS/TO/TM} 判定（Operation.LoopFoldMethod.SpinalDecisions / Agent.Core.AgentLoopTermination / 折叠投影），
/// 消重 TH-12 ANTI-DUPLICATED-IMPLEMENTATION。本类为「LoopDecision 是否预算终止终态」的唯一实现，其余点一律薄委托。
/// </summary>
public static class DecisionTerminals
{
    /// <summary>
    /// 是否预算终止终态（TS/TO/TM；引擎内 break 语义，阈值到达即触发）。
    /// Continue / None / SwitchStrategy 均非终态（继续推进侧）。
    /// </summary>
    public static bool IsTerminal(LoopDecision decision)
        => decision is LoopDecision.Success or LoopDecision.Timeout or LoopDecision.TripTerminated;
}
