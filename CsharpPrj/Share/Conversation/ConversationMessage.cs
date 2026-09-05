using System;
using Share.Conversation.ContextBinding;

namespace Share.Conversation;

/// <summary>
/// 单条会话消息。
/// </summary>
/// <param name="Role">消息角色（<see cref="ConversationRole"/>）。</param>
/// <param name="Stage">会话工作流步骤标识（声明式：如 "cline_execute"/"answer"…，不限定层数/子智能体）；null = 会话级/user/assistant。</param>
/// <param name="SubAgent">产生方执行器函数名（"cline_execute"/"answer"/…）；null = user/assistant。</param>
/// <param name="Text">消息文本。</param>
/// <param name="Round">会话累计轮次（0 = 会话级/system；user 自 1 起单调）。</param>
/// <param name="AtUtc">产生时刻（UTC）。</param>
/// <param name="Payload">外部消息原文载荷（可选；如 Cline 对话映射时保留 <c>ClineMessage</c> 原文 JSON，供 resume 精确重建；默认 null）。</param>
/// <param name="Binding">上下文绑定（可选；锚点键/来源域/投喂目标/文本哈希，供 UI≡LLM 一致性校验；默认 null）。尾参，不影响既有位置构造调用。</param>
public sealed record ConversationMessage(
    ConversationRole Role,
    string? Stage,
    string? SubAgent,
    string Text,
    int Round,
    DateTimeOffset AtUtc,
    string? Payload = null,
    MessageBinding? Binding = null)
{
    /// <summary>缺失归档字段的向前/向后兼容默认（旧数据无 Stage/SubAgent 时用于反序列化兜底）。</summary>
    public static readonly ConversationMessage Empty = new(ConversationRole.System, null, null, string.Empty, 0, default);
}
