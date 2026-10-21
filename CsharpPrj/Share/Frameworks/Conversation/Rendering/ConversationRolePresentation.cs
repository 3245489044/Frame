using System;

namespace Share.Conversation.Rendering;

/// <summary>
/// 会话角色展示映射（单一实现）——「角色 → 结构样式 token / 展示标签」的唯一口径。
/// <para>
/// 供气泡渲染器与卡片/窗口列表共用，消除两处各写一份 role switch。
/// 样式 token 为框架类名词表（user/assistant/proc/system/error），不含领域负词。
/// </para>
/// </summary>
public static class ConversationRolePresentation
{
    /// <summary>角色 → 结构样式 token（<c>user/assistant/proc/system/error</c>）。</summary>
    public static string ClassOf(ConversationRole role)
        => role switch
        {
            ConversationRole.User => "user",
            ConversationRole.Assistant => "assistant",
            ConversationRole.Tool => "proc",
            ConversationRole.Error => "error",
            _ => "system",
        };

    /// <summary>角色 → 展示标签（中文标签为会话域默认文案，可由上层覆盖）。</summary>
    public static string LabelOf(ConversationRole role)
        => role switch
        {
            ConversationRole.User => "用户",
            ConversationRole.Assistant => "助手",
            ConversationRole.Tool => "工具",
            ConversationRole.Error => "错误",
            _ => "系统",
        };
}
