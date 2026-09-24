namespace Share.Conversation.Facts;

/// <summary>
/// 事实类别名（**线格式受控词默认值**）——平台层默认词表，与
/// <c>Facts/Schema/session-fact.v1.schema.json</c> 的 <c>kind</c> 域同源。
/// <para>
/// 归属（R5 词表归属）：本类只承载**载体中立的会话通用类别名**；载体的私有类别
/// （如 Agent 治理事件 <c>GovernanceObs</c>）由载体侧扩展，**不进平台层**。
/// </para>
/// <para>
/// 用法：机制层（<see cref="FactCompleteness"/> / <see cref="FactDocument"/> / <see cref="RoundFactView"/>）
/// 只比较 <see cref="FactEnvelope.Kind"/> 的**字符串值**，不依赖任何载体枚举；
/// 载体侧枚举（如 Agent 的 <c>SessionFactKind</c>）成员名与本类常量逐字一致，
/// 经 <c>ToString()</c> 即得线格式值（单一真值来源 = 线格式）。
/// </para>
/// </summary>
public static class FactKindNames
{
    /// <summary>轮次受理（谁派发 / 什么模式 / 预算）。</summary>
    public const string RoundDispatch = "RoundDispatch";

    /// <summary>工作流步骤开始（步骤名 / 执行端）。</summary>
    public const string StepStart = "StepStart";

    /// <summary>执行面 LLM 回合（iteration / stop_reason）。</summary>
    public const string LlmIteration = "LlmIteration";

    /// <summary>工具调用请求。</summary>
    public const string ToolCall = "ToolCall";

    /// <summary>工具执行结果（ok / 耗时 / 产出摘要）。</summary>
    public const string ToolResult = "ToolResult";

    /// <summary>步骤终态（文本 / 失败阶段 / 来源）。</summary>
    public const string StepResult = "StepResult";

    /// <summary>终答（唯一权威；权威级见 <see cref="FactAuthority"/>）。</summary>
    public const string FinalAnswer = "FinalAnswer";

    /// <summary>轮次终止（term_kind / 原因）。</summary>
    public const string RoundTerminate = "RoundTerminate";

    /// <summary>错误分类（ErrorClass / 容忍度）。</summary>
    public const string ErrorClassified = "ErrorClassified";

    /// <summary>成本度量（token in/out / 耗时）。</summary>
    public const string CostMeasured = "CostMeasured";
}
