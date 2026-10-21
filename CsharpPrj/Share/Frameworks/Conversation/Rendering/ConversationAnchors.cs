using Share.Conversation;

namespace Share.Conversation.Rendering;

/// <summary>
/// 会话锚点判定（**单源**）：<c>user/assistant/error/system</c> 为气泡锚点，其余（Tool 等过程项）可挂靠。
/// <para>
/// 渲染层（近邻锚分组）与领域侧（观测帧合并插入点）共用同一判定，避免两处各写一遍角色集合而静默分叉。
/// </para>
/// </summary>
public static class ConversationAnchors
{
    /// <summary>是否锚点角色（过程项挂靠的目标）。</summary>
    /// <param name="role">消息角色。</param>
    public static bool IsAnchor(ConversationRole role)
        => role is ConversationRole.User or ConversationRole.Assistant
                 or ConversationRole.Error or ConversationRole.System;
}
