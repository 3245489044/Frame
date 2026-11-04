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
        => cut is FoldCut.Success or FoldCut.BudgetExhausted or FoldCut.TripTerminated
            or FoldCut.Cancelled or FoldCut.Failed;

    /// <summary>
    /// <see cref="FoldCut"/> → <see cref="LoopDecision"/>（终值映射到引擎预算终止值；Continue→Continue）。
    /// <see cref="FoldCut.Cancelled"/> 属<b>探索终止</b>（外部取消/宿主关闭），在本引擎中表现为
    /// <see cref="LoopDecision.Abort"/>（停止推进），其终值由折叠引擎记录在 <c>FoldResult.Cut</c>，
    /// 不落预算裁决语义；不再抛出不可达异常（取消是可达路径）。
    /// </summary>
    public static LoopDecision ToLoopDecision(FoldCut cut) => cut switch
    {
        FoldCut.Success => LoopDecision.Success,
        FoldCut.BudgetExhausted => LoopDecision.Timeout,
        FoldCut.TripTerminated => LoopDecision.TripTerminated,
        FoldCut.Cancelled => LoopDecision.Abort,
        FoldCut.Failed => LoopDecision.Abort,
        _ => LoopDecision.Continue,
    };

    /// <summary>
    /// <see cref="LoopDecision"/> → <see cref="FoldCut"/>（完整映射，禁止出现「终态落 Continue」）。
    /// 注：与 <see cref="ToLoopDecision"/> 非双射（Cancelled/Failed 均以 Abort 表达），禁止用于往返还原。
    /// </summary>
    public static FoldCut FromLoopDecision(LoopDecision decision) => decision switch
    {
        LoopDecision.Success or LoopDecision.Complete => FoldCut.Success,
        LoopDecision.Timeout => FoldCut.BudgetExhausted,
        LoopDecision.TripTerminated => FoldCut.TripTerminated,
        LoopDecision.Abort => FoldCut.Failed,
        _ => FoldCut.Continue,
    };
}
