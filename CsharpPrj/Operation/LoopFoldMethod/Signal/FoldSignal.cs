namespace Operation.LoopFoldMethod.Signal;

/// <summary>
/// 信号类型 —— 步进循环的唯一驱动事件（外循环推进唯一机制 = 信号驱动步进）。
/// 与既有会话语义对齐：ExternalInput（外部直接输入）∪ StepCompleted（子智能体/子步骤产出）。
/// LLM-free：信号是框架中立事件，不携带任何 Agent/SK 概念。
/// </summary>
public enum FoldSignalKind
{
    /// <summary>外部直接输入：启动或介入一次步进（如用户消息 / 外部智能体消息）。</summary>
    ExternalInput,

    /// <summary>子步骤产出：某一步完成并产出 → 沿链驱动下一步（SubAgentCompleted 语义）。</summary>
    StepCompleted,

    /// <summary>节点产出：节点落库（NodeWritePipeline 写 ur_nodes + cross_db_link 边）同步产出，
    /// 携带节点信号 NodeSignal（最近/价值/区域评判载体）→ 区域化信号解析据此推进。仅作信号面枚举，
    /// 载荷由 Agent 侧 ConversationSignal 承载（NodeSignal），经 adapter 映射为本 Kind。</summary>
    NodeProduced,

    /// <summary>链终点终止信号：某步判定无后续 → 收敛（run_to_end）。</summary>
    Terminal,
}

/// <summary>
/// 一条驱动信号（Generic，框架中立，LLM-free）：<see cref="Kind"/> + 目标步骤键 + 载荷 + 可选算子结论。
/// </summary>
/// <typeparam name="TVertex">每步算子结论类型。</typeparam>
/// <param name="Kind">信号类型。</param>
/// <param name="StepKey">已完成/入口步骤键（ExternalInput 为链入口；StepCompleted 为该步键，供解析器映射下一步）。</param>
/// <param name="Payload">载荷文本（外部输入文本 / 子步骤产出文本；可选）。</param>
/// <param name="Vertex">算子结论（StepCompleted 时携带该步结论，供下一步入参/信号解析；可选）。</param>
/// <param name="Decision">决策载荷（可选；规则语义 continue/stop/error 在信号域的投影，供 <see cref="RuleSignalResolver{TState,TVertex}"/> 裁决）。</param>
public sealed record FoldSignal<TVertex>(
    FoldSignalKind Kind,
    string StepKey,
    string? Payload = null,
    TVertex? Vertex = default,
    FoldDecision? Decision = null);

/// <summary>
/// 信号驱动算子单步产出：下一状态点 + 该步算子结论 + 该步步骤键（供 <see cref="ISignalResolver{TState,TVertex}"/> 映射下一步）。
/// 外部算子 <see cref="ISignalDrivenOperator{TState,TVertex}"/> 的 ExpandAsync 返回载体 ——
/// 「读当前状态 → 消费信号 → 展开一步 → 产出状态点 + 顶点」的最小闭环。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
/// <typeparam name="TVertex">该步算子结论类型。</typeparam>
/// <param name="Next">下一状态点。</param>
/// <param name="Vertex">该步算子结论。</param>
/// <param name="StepKey">本步实际执行的步骤键（如链节点键；信号解析据此推进到下一步）。</param>
public sealed record SignalFoldStep<TState, TVertex>(TState Next, TVertex Vertex, string StepKey);
