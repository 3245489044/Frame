namespace Operation.LoopFoldMethod.Decision;

/// <summary>
/// 终止信号 —— 决策的确定性终止结果（对齐 <c>LoopDecision</c> 终态域 / AX.DecisionOutputSet）。
/// 决策 = 产出折叠链标识符 D′ 或终止信号；本枚举承载「终止信号」一侧。
/// </summary>
public enum TerminationSignal
{
    /// <summary>成功（TS / 链终点收敛）。</summary>
    Success,

    /// <summary>超限终止（TO / 预算耗尽，ProgressToken 达 MAX_PROGRESS）。</summary>
    Timeout,

    /// <summary>熔断终止（TM / 短路，FailureBudget 熔断）。</summary>
    TripTerminated,

    /// <summary>控制位 halt（熔断短路显式化，等效 TM 由 ControlFlag.halt 触发）。</summary>
    Halt,
}

/// <summary>
/// 决策统一结果 ——「决策 = 产出折叠链标识符 D′ 或终止信号」。统一决策式终止（RoundLifecycle.Decide）
/// 与折叠链收敛（resolver 表驱动）为单一语义：每步决策产物即下一步折叠链标识符 D′（<see cref="ContinueTo"/>），
/// 无 D′ = 链终点 / 终止（<see cref="Terminate"/>）。对齐 [CR: axioms:AX.DecisionOutputSet@consume]。
/// 分层（SEP.DeterministicCoreDisjoint）：确定性硬性部分经 <see cref="IDeterministicTermination{TState}"/> 引擎内裁决
/// 产 <see cref="Terminate"/>；LLM 软决策经 <see cref="IDecisionSkill{TState,TVertex}"/> 协程产 <see cref="ContinueTo"/>。
/// </summary>
public abstract record DecisionOutcome
{
    /// <summary>是否终态（终止信号 / 显式中止，折叠应停止）。</summary>
    public bool IsTerminal => this is Terminate or Abort;

    /// <summary>产出下一步折叠链标识符 D′（软推进）。</summary>
    public static DecisionOutcome Next(string nextKey) => new ContinueTo(nextKey);

    /// <summary>软推进 + 附带动作意图（如 tool_call）—— 决策除产出 D′ 外还可挂载待执行动作，由上游 skill/适配器消费。</summary>
    public static DecisionOutcome NextWithAction(string nextKey, DecisionAction action) => new ContinueTo(nextKey, action);

    /// <summary>产出终止信号（确定性终止 / 链终点收敛）。</summary>
    public static DecisionOutcome Stop(TerminationSignal signal) => new Terminate(signal);

    /// <summary>显式中止（fail-safe，R7：降级不再等价成功）—— 携原因供审计。</summary>
    public static DecisionOutcome Fail(string reason) => new Abort(reason);

    /// <summary>软推进：产出下一步折叠链标识符 D′（可附带动作意图 <see cref="ContinueTo.Action"/>）。</summary>
    public sealed record ContinueTo(string NextKey, DecisionAction? Action = null) : DecisionOutcome;

    /// <summary>终止：确定性硬终止信号 或 链终点收敛。</summary>
    public sealed record Terminate(TerminationSignal Signal) : DecisionOutcome;

    /// <summary>显式中止：非终止信号、非成功，携带原因（R7 fail-safe，缺工具/规则失败显式可见，不回退静默 continue）。</summary>
    public sealed record Abort(string Reason) : DecisionOutcome;
}
