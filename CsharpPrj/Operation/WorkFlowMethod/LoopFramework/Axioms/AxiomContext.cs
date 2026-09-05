namespace WorkFlowMethod.LoopFramework.Axioms;

/// <summary>
/// 统一上下文（clinerules axioms §2）。把散落的计数器/标志收敛为三个结构体，
/// 作为循环运算框架的一等状态载体：FailureBudget + ProgressToken + ControlFlag。
/// 终止判定唯一依据 = ProgressToken.IsExhausted 或 ControlFlag.Halt（TerminationPriority，axioms §3）。
/// </summary>
public sealed class AxiomContext : ILoopState
{
    private IDictionary<string, object> _data = new Dictionary<string, object>();
    private readonly FailureBudget _budget;
    private readonly ProgressToken _token;
    private readonly ControlFlag _flag;

    public FailureBudget Budget => _budget;

    public ProgressToken Token => _token;

    public ControlFlag Flag => _flag;

    /// <summary>纯数据存储（history / last / lastFileHash 等），非状态机输入。</summary>
    public IDictionary<string, object> Data => _data;

    // ILoopState 契约视图：把同一数据字典以接口类型暴露（兼容现有调用）。
    IDictionary<string, object> ILoopState.Data => _data;

    /// <summary>默认：核心对象（Budget/Token/Flag）独立新建。</summary>
    public AxiomContext() : this(null, null, null, null) { }

    /// <summary>共享外部数据字典；核心对象独立新建。供混合调度以 SchedulerState.Data 为单一数据持有方派生每轮执行租约，避免双 Data 事实源。</summary>
    public AxiomContext(IDictionary<string, object>? data) : this(data, null, null, null) { }

    /// <summary>
    /// 共享外部数据字典 + 注入核心对象引用（Budget/Token/Flag）。
    /// 供混合调度以 SchedulerState 为确定性核心单一事实源（StateRootSingleSource）派生每轮执行租约：
    /// 注入其核心对象引用，使 ProgressUpdater/BudgetEscalator 直接写根状态对象，消除「派生复制 + 收割回写」双写漂移。
    /// 任一核心参数为 null = 新建独立对象（保持既有行为）。
    /// </summary>
    public AxiomContext(IDictionary<string, object>? data, FailureBudget? budget, ProgressToken? token, ControlFlag? flag)
    {
        if (data != null) _data = data;
        _budget = budget ?? new FailureBudget();
        _token = token ?? new ProgressToken();
        _flag = flag ?? new ControlFlag();
    }

    /// <summary>当前轮次号（从 1 起）。</summary>
    public int Round { get; set; }

    /// <summary>用户输入（主循环入口参数）。</summary>
    public string? UserInput { get; set; }

    /// <summary>上一轮执行结果文本（供 Adjust 阶段生成下一步提示）。</summary>
    public string? LastResultText { get; set; }

    /// <summary>上一轮评估结果。</summary>
    public EvalOutcome LastEval { get; set; } = EvalOutcome.None;

    /// <summary>错误处理作用域（Scope），决定 handle 的合法动作子集。</summary>
    public Scope Scope { get; set; } = Scope.LoopBody;
}
