namespace Operation.LoopFoldMethod.Decision;

/// <summary>决策动作类型 —— 决策除产出折叠链标识符 D′ 外，可附带的中立动作意图。</summary>
public enum DecisionActionKind
{
    /// <summary>继续（无副作用动作）。</summary>
    Continue,

    /// <summary>停止（链终点）。</summary>
    Stop,

    /// <summary>错误/中止（显式）。</summary>
    Error,

    /// <summary>工具调用（tool_call）：经工具注册表校验后执行（缺工具 → Abort，不回退）。</summary>
    ToolCall,
}

/// <summary>
/// 决策动作意图（LLM-free 中立负载，Agent 无关）—— 随 <see cref="DecisionOutcome.ContinueTo.Action"/> 挂载，
/// 由上游 skill/适配器消费：<see cref="DecisionActionKind.ToolCall"/> 时 <see cref="ToolName"/>+<see cref="ToolArgumentsJson"/>
/// 经工具注册表（Share.Registry.ToolRegistry / Agent 侧 LlmToolRegistry）校验并执行；校验失败/缺工具 → 显式 Abort。
/// </summary>
public sealed record DecisionAction(
    DecisionActionKind Kind,
    string? ToolName = null,
    string? ToolArgumentsJson = null,
    string? ScheduleKey = null)
{
    /// <summary>工具调用动作（便捷构造）。</summary>
    public static DecisionAction ToolCall(string toolName, string? argumentsJson)
        => new(DecisionActionKind.ToolCall, ToolName: toolName, ToolArgumentsJson: argumentsJson);
}