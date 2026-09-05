namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 评估结果（clinerules AX.EvaluationOutcomeSet：pass/partial/fail/parse_error）。
/// Pass ↦ T；Partial ↦ ⊥；Fail/ParseError ↦ F。
/// </summary>
public enum EvalOutcome
{
    None,
    Pass,        // ↦ T
    Partial,     // ↦ ⊥
    Fail,        // ↦ F
    ParseError,  // ↦ F
}

/// <summary>熔断等级 level∈{0..3}。升降级由 <see cref="BudgetEscalator"/> 单一裁决（FailureBudgetEscalation，clinerules axioms §5）。</summary>
public sealed class FailureBudget
{
    public const int MinLevel = 0;
    public const int MaxLevel = 3;

    public int Level { get; private set; }

    /// <summary>错误升级：level=min(level+1,3)。</summary>
    public void Escalate() => Level = Math.Min(Level + 1, MaxLevel);

    /// <summary>成功衰减：level=max(0,level-1)。</summary>
    public void Deescalate() => Level = Math.Max(Level - 1, MinLevel);

    /// <summary>irrecoverable 直接打满。</summary>
    public void Trip() => Level = MaxLevel;

    public bool IsTripped => Level >= MaxLevel;

    public void Reset() => Level = MinLevel;
}

/// <summary>轮次进度 value∈{0..Max}。更新唯一入口 = <see cref="ProgressUpdater"/>（EvaluateDrivenUpdate，formula §4.5）。</summary>
public sealed class ProgressToken
{
    public int Value { get; private set; }

    /// <summary>上限，对应 MAX_PROGRESS；默认对齐 Profile.SAFE(MAX_PROGRESS=1) 语义取 3。</summary>
    public int Max { get; set; } = 3;

    public bool IsExhausted => Value >= Max;

    /// <summary>失败递增：value=min(value+1,Max)。</summary>
    public void Advance() => Value = Math.Min(Value + 1, Max);

    /// <summary>成功回退：value=max(value-1,0)。</summary>
    public void Retreat() => Value = Math.Max(Value - 1, 0);

    public void Reset() => Value = 0;
}

/// <summary>控制位掩码。halt 由 irrecoverable 或 FailureBudget.level=3 触发。</summary>
public sealed class ControlFlag
{
    public bool Halt { get; private set; }

    public void SetHalt() => Halt = true;

    public void Clear() => Halt = false;
}

/// <summary>
/// 循环状态契约（clinerules axioms §2 统一上下文）。把「通用逻辑」与「状态」分离：
/// 算法层（BudgetEscalator/ProgressUpdater/TerminationDecider…）只依赖本接口、不持有任何具体状态类型；
/// 状态宿主由实现方提供。
/// </summary>
public interface ILoopState
{
    /// <summary>熔断等级 level∈{0..3}。升降级由 BudgetEscalator 单一裁决（FailureBudgetEscalation）。</summary>
    FailureBudget Budget { get; }

    /// <summary>轮次进度 value∈{0..Max}。更新唯一入口 = ProgressUpdater（EvaluateDrivenUpdate）。</summary>
    ProgressToken Token { get; }

    /// <summary>控制位掩码。halt 由 irrecoverable 或 FailureBudget.level=3 触发。</summary>
    ControlFlag Flag { get; }

    /// <summary>当前轮次号（从 1 起）。</summary>
    int Round { get; set; }

    /// <summary>用户输入（主循环入口参数）。</summary>
    string? UserInput { get; set; }

    /// <summary>上一轮执行结果文本（供 Adjust 阶段生成下一步提示）。</summary>
    string? LastResultText { get; set; }

    /// <summary>上一轮评估结果。</summary>
    EvalOutcome LastEval { get; set; }

    /// <summary>纯数据存储（history / last / lastFileHash 等），非状态机输入。</summary>
    IDictionary<string, object> Data { get; }
}

/// <summary>
/// 循环决策域（clinerules AX.DecisionOutputSet：{TS,TO,TM,switch_strategy,continue}）。
/// 作为状态机/裁决器的一等产出，替代把终止条件硬编码为迭代计数的做法。
/// </summary>
public enum LoopDecision
{
    None,

    /// <summary>continue —— 循环级继续决策，不受 Scope 约束。</summary>
    Continue,

    /// <summary>TS —— 成功（SUCCESS_CRITERIA: pass ∧ n≥1 达成）。</summary>
    Success,

    /// <summary>TO —— 超限终止（ProgressToken.IsExhausted）。</summary>
    Timeout,

    /// <summary>TM —— 熔断终止（ControlFlag.Halt）。</summary>
    TripTerminated,

    /// <summary>switch_strategy —— 切换策略（需 Scope 允许）。</summary>
    SwitchStrategy,
}

/// <summary>错误分类（clinerules axioms §4）。</summary>
public enum ErrorClass
{
    ParseError,
    CompilationError,
    ExecutionFailure,
    Timeout,
    PartialOutput,
    EmptyResult,
    ResourceExhausted,
    Irrecoverable,
}
