using System;
using Share.Conversation;

namespace Share.Conversation.Rendering;

/// <summary>
/// 会话锚点判定（**单源**）：<c>user/assistant/error/system</c> 且走**对话通道**才是有气泡锚点；
/// 过程通道（<see cref="MessageChannel.Process"/>）的助手/工具帧属过程项，只能折叠，不得冒充气泡。
/// <para>
/// 渲染层（近邻锚分组）与领域侧（观测帧合并插入点）共用同一判定，避免两处各写一遍角色集合而静默分叉。
/// </para>
/// </summary>
public static class ConversationAnchors
{
    /// <summary>是否锚点角色（不含通道判定；供只关心角色的场景使用）。</summary>
    /// <param name="role">消息角色。</param>
    public static bool IsAnchor(ConversationRole role)
        => role is ConversationRole.User or ConversationRole.Assistant
                 or ConversationRole.Error or ConversationRole.System;

    /// <summary>锚点 = 锚点角色 ∧ 对话通道（过程帧不论角色一律非锚点）。</summary>
    /// <param name="role">消息角色。</param>
    /// <param name="channel">呈现通道。</param>
    public static bool IsAnchor(ConversationRole role, MessageChannel channel)
        => channel == MessageChannel.Dialogue && IsAnchor(role);

    /// <summary>渲染项锚点判定（结构事实：角色 + 通道；不读领域 stage 语义）。</summary>
    /// <param name="item">渲染项。</param>
    public static bool IsAnchor(ConversationRenderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return IsAnchor(item.Role, item.Channel);
    }
}
