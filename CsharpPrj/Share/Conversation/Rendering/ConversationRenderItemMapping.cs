using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Conversation.Rendering;

/// <summary>
/// 消息记录 → 渲染项（<see cref="ConversationRenderItem"/>）的投影映射。
/// 与 <c>ConversationPipeline.ExtractStages</c>（节点快照 → StageInfo）的投影模式同构：
/// 存储记录原样保留，仅在此投影出"供渲染器消费"的中立视图。
/// </summary>
public static class ConversationRenderItemMapping
{
    /// <summary>消息列表 → 渲染项列表；null 输入返回空列表（容错）。</summary>
    public static IReadOnlyList<ConversationRenderItem> ToRenderItems(this IEnumerable<ConversationMessage>? messages)
    {
        if (messages is null) return Array.Empty<ConversationRenderItem>();
        return messages
            .Select(m => new ConversationRenderItem(
                m.Role, m.Stage, m.SubAgent, m.Text ?? string.Empty, m.Round))
            .ToList();
    }

    /// <summary>会话日志 → 渲染项列表；null 输入返回空列表（容错）。</summary>
    public static IReadOnlyList<ConversationRenderItem> ToRenderItems(this ConversationLog? log)
        => log is null ? Array.Empty<ConversationRenderItem>() : log.Messages.ToRenderItems();
}
