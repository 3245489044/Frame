using Operation.LoopFoldMethod.Fold;
using Share.TaskStatusPool.Loops;

namespace Operation.LoopFoldMethod.Select;

/// <summary>
/// 折叠决策域的终决与 <see cref="FoldCut"/> ↔ <see cref="LoopDecision"/> 映射，确定性，LLM 不参与
/// （对齐 clinerules AX.DecisionOutputSet / AX.TerminationBipartition）。
/// 终值集合判定 + 终值 ↔ 引擎承载的预算终止值 TS/TO/TM 映射。
/// 注：本静态类与 <c>Signal.FoldDecision</c>（决策信号枚举 Continue/Stop/Error）职责不同、曾同名造成混淆，
/// 已更名为 <see cref="FoldCutMapping"/> 消歧（Signal.FoldDecision 保留为决策信号域投影）。
/// </summary>
public static class FoldCutMapping
{
    /// <summary>是否为终值（折叠应停止）。</summary>
    public static bool IsTerminal(FoldCut cut)
        => cut is FoldCut.Success or FoldCut.BudgetExhausted or FoldCut.TripTerminated or FoldCut.Cancelled;

    /// <summary>
    /// <see cref="FoldCut"/> → <see cref="LoopDecision"/>（终值映射到引擎预算终止值；Continue→Continue）。
    /// 注：<see cref="FoldCut.Cancelled"/> 属<b>探索终止</b>（外部取消），由折叠引擎经
    /// <c>SnapshotLoopResult.TerminatedByExternal</c> 承载，不落预算终止裁决（budgetTerminate 仅接受
    /// TS/TO/TM/Continue）。本映射显式拒绝该域错误，防止被静默吞成 Continue（终值语义失守）。
    /// </summary>
    public static LoopDecision ToLoopDecision(FoldCut cut) => cut switch
    {
        FoldCut.Success => LoopDecision.Success,
        FoldCut.BudgetExhausted => LoopDecision.Timeout,
        FoldCut.TripTerminated => LoopDecision.TripTerminated,
        FoldCut.Cancelled => throw new InvalidOperationException(
            "FoldCut.Cancelled 属探索终止（外部取消），不得经预算终止裁决（budgetTerminate）映射为 LoopDecision；" +
            "应由折叠引擎经 SnapshotLoopResult.TerminatedByExternal 承载。"),
        _ => LoopDecision.Continue,
    };

    /// <summary><see cref="LoopDecision"/> → <see cref="FoldCut"/>（反向映射；Cancelled 由外部终止承载，此处不映射）。</summary>
    public static FoldCut FromLoopDecision(LoopDecision decision) => decision switch
    {
        LoopDecision.Success => FoldCut.Success,
        LoopDecision.Timeout => FoldCut.BudgetExhausted,
        LoopDecision.TripTerminated => FoldCut.TripTerminated,
        _ => FoldCut.Continue,
    };
}
