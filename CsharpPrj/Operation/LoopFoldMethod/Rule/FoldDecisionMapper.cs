using Operation.LoopFoldMethod.Signal;
using Share.TaskStatusPool.Loops;
using Share.TaskStatusPool.Routing;

namespace Operation.LoopFoldMethod.Rule;

/// <summary>
/// 决策词 → <see cref="FoldDecision"/> 信号域投影（LLM-free 通用，词表单源 = <see cref="DecisionWords"/>，禁止第三套）。
/// 供规则决策器与状态驱动框架的决策记录投影共用，替代在 Agent 侧重复实现（SingleImplementation / TH-12）。
/// </summary>
public static class FoldDecisionMapper
{
    /// <summary>决策词 → FoldDecision：显式 error 词 → Error；否则经 <see cref="DecisionWords.Map"/>（LoopDecision）投影
    /// （Continue→Continue，Success→Stop，其余→Error）。</summary>
    public static FoldDecision FromWord(string? word)
        => DecisionWords.IsError(word) ? FoldDecision.Error : FromDecision(DecisionWords.Map(word));

    /// <summary><see cref="LoopDecision"/> → FoldDecision：Continue→Continue、Success→Stop、其余→Error。</summary>
    public static FoldDecision FromDecision(LoopDecision? decision) => decision switch
    {
        LoopDecision.Continue => FoldDecision.Continue,
        LoopDecision.Success => FoldDecision.Stop,
        _ => FoldDecision.Error,
    };
}