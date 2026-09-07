using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Core;

/// <summary>
/// 管道决策判定（固定层纯规定）—— 收敛 = 出现轮世界终态（TS/TO/TM）。
/// 权威：<c>Share.TaskStatusPool.Loops.LoopDecision</c>（AX.DecisionOutputSet）。SwitchStrategy / Continue 均非管道终态。
/// </summary>
public static class SpinalDecisions
{
    /// <summary>是否为预算终止终态（引擎内 break 语义：TS/TO/TM，阈值到达即触发）。</summary>
    public static bool IsTerminal(LoopDecision decision)
        => decision is LoopDecision.Success or LoopDecision.Timeout or LoopDecision.TripTerminated;

    /// <summary>是否为纯继续（Continue / None / SwitchStrategy 均属需继续推进侧）。</summary>
    public static bool IsContinue(LoopDecision decision) => !IsTerminal(decision);
}
