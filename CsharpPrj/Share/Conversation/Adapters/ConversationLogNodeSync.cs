using System.Collections.Generic;

namespace Share.Conversation.Adapters;

/// <summary>
/// nodes ⇄ history 双向同步的便捷扩展：<see cref="ConversationLog"/> ↔ 节点贡献。
/// 正向（nodes→history）由 <see cref="IConversationNodeMapper.MapToConversation"/> 承担（Agent 侧把节点产出
/// 投影为 <see cref="ConversationNodeContribution"/> 后入会话）；反向（history→nodes）由此处提取 Tool 消息。
/// </summary>
public static class ConversationLogNodeSync
{
    private static readonly ConversationNodeMapper DefaultMapper = new();

    /// <summary>会话历史 → 节点贡献（提取 Tool 消息；供回放/恢复/再材料化为节点结构）。</summary>
    public static IReadOnlyList<ConversationNodeContribution> ToContributions(
        this ConversationLog? log, IConversationNodeMapper? mapper = null)
        => (mapper ?? DefaultMapper).ToContributions(log?.Messages);
}
