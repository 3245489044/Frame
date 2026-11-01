#nullable enable

using Share.Conversation;

namespace Share.Conversation.Rendering;

/// <summary>
/// 旧数据的**锚点角色词表**（唯一来源）：仅供 <see cref="Presentation.PresentationResolver"/> 的
/// Legacy 分支使用（无事实、无声明时的兜底）。
/// <para>
/// 纪律：本表不得再参与布局判定——呈现身份只由 <see cref="Presentation.PresentationResolver"/> 决定；
/// 通道与终答事实在解析器中优先于本表。
/// </para>
/// </summary>
public static class ConversationAnchors
{
    /// <summary>是否旧数据锚点角色（user/assistant/error/system）。</summary>
    /// <param name="role">消息角色。</param>
    public static bool IsAnchor(ConversationRole role)
        => role is ConversationRole.User or ConversationRole.Assistant
                 or ConversationRole.Error or ConversationRole.System;
}
