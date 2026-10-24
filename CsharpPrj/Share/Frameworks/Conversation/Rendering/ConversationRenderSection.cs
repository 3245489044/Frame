using System.Collections.Generic;

namespace Share.Conversation.Rendering;

/// <summary>
/// 会话流分段（增量落地单位）：<paramref name="Key"/> 为稳定段键（同一段 ⇒ 定点更新，新段 ⇒ 追加），
/// <paramref name="Html"/> 为该段完整 HTML。段键由实现派生（作者禁手写），供流原语按 key 对齐。
/// </summary>
public sealed record ConversationRenderSection(string Key, string Html);
