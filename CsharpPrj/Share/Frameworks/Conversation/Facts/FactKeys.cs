namespace Share.Conversation.Facts;

/// <summary>
/// 载荷字段名（**机制层共用键名的唯一字面量源**）—— 键名小写下划线，与
/// <c>Facts/Schema/session-fact.v1.schema.json</c> 的 <c>payload</c> 字段同源。
/// <para>
/// 归属（R5）：本类只收**机制层判据实际读取的通用字段**（成败位 / 停止原因 / 文本 / 耗时 / 模式 / 结果种类 /
/// token）；载体私有字段（如 <c>step_name</c> / <c>failure_stage</c>）由载体侧词表持有，
/// 不进平台层。载体侧可经 <c>const</c> 别名引用本类常量（单一字面量源，禁抄写第二份）。
/// </para>
/// </summary>
public static class FactKeys
{
    /// <summary>成败位（工具结果 / 步骤终态）。</summary>
    public const string Ok = "ok";

    /// <summary>不完整标志位（布尔；与受控词 <see cref="PartialReason"/> 并存）。</summary>
    public const string Partial = "partial";

    /// <summary>未推进 / 不完整原因（受控词见 <see cref="PartialReasons"/>）。</summary>
    public const string PartialReason = "partial_reason";

    /// <summary>执行面停止原因（<c>stop_reason</c>）。</summary>
    public const string StopReason = "stop_reason";

    /// <summary>停止原因兼容键（读侧容忍历史写法 <c>stopReason</c>；产出侧只用 <see cref="StopReason"/>）。</summary>
    public const string StopReasonCompat = "stopReason";

    /// <summary>结果种类（narration / completion_result / error）。</summary>
    public const string ResultKind = "result_kind";

    /// <summary>结果来源（sink / disk / sdk / endpoint…）。</summary>
    public const string ResultSource = "result_source";

    /// <summary>文本（终答 / 步骤文本；大文本只存摘要 + <see cref="TextLen"/>）。</summary>
    public const string Text = "text";

    /// <summary>文本字符数（大文本体积判据的唯一来源）。</summary>
    public const string TextLen = "text_len";

    /// <summary>耗时（毫秒）。</summary>
    public const string ElapsedMs = "elapsed_ms";

    /// <summary>调度模式（act / plan）。</summary>
    public const string Mode = "mode";

    /// <summary>轮次终止类别（受控词由载体声明面持有）。</summary>
    public const string TermKind = "term_kind";

    /// <summary>输入 token。</summary>
    public const string TokensIn = "tokens_in";

    /// <summary>输出 token。</summary>
    public const string TokensOut = "tokens_out";
}
