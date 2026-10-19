namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 终止策略 —— 唯一裁决面：读只读快照，给终值裁决（非终值 = Continue）。
/// 组合规则：策略链自上而下短路，第一个终值胜出（优先级即顺序）。
/// </summary>
public interface ITerminationPolicy
{
    TerminationVerdict Decide(in TerminationContext context);
}
