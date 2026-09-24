namespace Share.Conversation.Facts;

/// <summary>
/// 事实权威级（治 D3：叙述 ≠ 终答）——终答事实的构造走**判据式选取**，而非「最后一条非空文本」。
/// <para>
/// 判据（方案 §2.4）：<see cref="Authoritative"/> = 执行面回合终态（cline <c>agent_end.turn.outputText</c>，
/// 非空且 stop_reason 合法）；<see cref="Session"/> = SDK 会话快照的 <c>completion_result</c>；
/// <see cref="Streamed"/> = 事件流末尾未被覆盖的叙述（**须标 <c>result_kind=narration</c> 且
/// <c>partial=true</c>**，不得对外表现为「成功未截断」）；<see cref="Error"/> = 失败路径文本。
/// </para>
/// <para>
/// 受控词声明面 = <c>Config\session-facts.ushar</c> 的 <c>[Space] FactAuthority</c>（A1–A4 四值）；
/// <see cref="Primary"/> 为 C# 侧缺省值（= 未分级，非权威级），供非终答事实（工具/步骤/成本）使用。
/// </para>
/// </summary>
public enum FactAuthority
{
    /// <summary>未分级（缺省）：非终答事实（工具调用 / 步骤 / 成本 / 观测）的常态。</summary>
    Primary,

    /// <summary>A1 执行面回合终态（首选）。</summary>
    Authoritative,

    /// <summary>A2 SDK 会话快照的 completion_result（次选）。</summary>
    Session,

    /// <summary>A3 流式叙述兜底（须标 narration + partial）。</summary>
    Streamed,

    /// <summary>A4 失败路径文本（result_kind=error）。</summary>
    Error,
}
