namespace BasicMethod.Initialization.Errors;

/// <summary>
/// 错误分类（axioms §4 FailureSubtyping 元素权威基；Gate.ClassifyError 的元素域）。
/// canonical 定义于本层（BasicMethod）——下游以别名引用，禁建第二枚举（Strategy.SingleImplementation）。
/// </summary>
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

/// <summary>
/// 容忍度域（axioms §2 ErrorTolerance）：tolerated 只记账（不升 level）；
/// fatal 升级熔断等级。判定见 <see cref="FailSink.ClassifyToTolerance"/>。
/// </summary>
public enum ErrorTolerance
{
    /// <summary>可容忍失败（单步增量试错）：只记账，不计熔断。</summary>
    Tolerated,

    /// <summary>不可容忍失败：升级熔断等级。</summary>
    Fatal,
}
