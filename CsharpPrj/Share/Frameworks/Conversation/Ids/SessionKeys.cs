namespace Share.Conversation.Ids;

/// <summary>
/// 会话键归一**唯一权威**（平台层纯函数）—— 会话名 / 会话 id → 会话键的归一与默认值单源。
/// <para>
/// 存在理由：归一实现曾分散三处（工作流层 <c>s =&gt; s.Trim()</c>、端点层 <c>ResolveSessionKey</c>、
/// 归档层 <c>ResolveSessionKey</c>），「会话名默认值」亦在三处各写 <c>"default"</c> 字面量 ⇒
/// 同一逻辑身份三套实现（台账 D1/D2）。本类把两者收敛为单源，消费方零自实现。
/// </para>
/// <para>
/// 语义边界（**只归一不改文法**）：本类不改变键的线格式，只做「trim / 空白回退」两级纯变换；
/// 键的文法与生成仍属 <see cref="FactIds"/> / <see cref="FactIdGrammar"/>。
/// </para>
/// </summary>
public static class SessionKeys
{
    /// <summary>会话名默认值（空白会话名的回退键；唯一字面量来源）。</summary>
    public const string Default = "default";

    /// <summary>会话键归一：去除首尾空白（不引入默认值，保持「空即空」语义供调用方判定）。</summary>
    /// <param name="sessionName">原始会话名（可为 null）。</param>
    public static string Normalize(string? sessionName)
        => (sessionName ?? string.Empty).Trim();

    /// <summary>会话键归一 + 空白回退默认值（<see cref="Default"/>）。</summary>
    /// <param name="sessionKey">原始会话键 / 会话名（可为 null 或空白）。</param>
    public static string Resolve(string? sessionKey)
        => string.IsNullOrWhiteSpace(sessionKey) ? Default : sessionKey!;
}
