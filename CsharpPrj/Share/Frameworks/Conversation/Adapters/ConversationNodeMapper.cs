using System;
using System.Collections.Generic;

namespace Share.Conversation.Adapters;

/// <summary>
/// 通用节点映射器（默认实现，SingleImplementation）：把「用户输入 + 节点贡献 + 终答」组装为规范对话消息序列。
/// Round 推导：贡献 Round=0 → 对齐 startRound（同轮分层气泡）；&gt;0 → 使用显式轮次。
/// </summary>
public sealed class ConversationNodeMapper : IConversationNodeMapper
{
    /// <summary>映射为规范对话消息序列（user → tool(Stage) → assistant）。</summary>
    public IReadOnlyList<ConversationMessage> MapToConversation(
        IReadOnlyList<ConversationNodeContribution> contributions,
        string? userInput = null,
        string? assistantAnswer = null,
        int startRound = 1)
    {
        var list = new List<ConversationMessage>();
        var round = Math.Max(1, startRound);

        if (!string.IsNullOrWhiteSpace(userInput))
            list.Add(new ConversationFrame(
                    ConversationRole.User, string.Empty, userInput, round, ConversationFacts.Empty)
                .ToMessage());

        if (contributions is not null)
            foreach (var c in contributions)
            {
                if (c is null || string.IsNullOrWhiteSpace(c.Text)) continue;
                var r = c.Round > 0 ? c.Round : round;
                list.Add(new ConversationFrame(
                        ConversationRole.Tool,
                        c.Stage ?? string.Empty,
                        c.Text,
                        r,
                        string.IsNullOrWhiteSpace(c.Stage)
                            ? ConversationFacts.Empty
                            : ConversationFacts.FromPairs(("kind", c.Stage)),
                        Stage: c.Stage,
                        SubAgent: c.SubAgent,
                        Channel: MessageChannel.Process,
                        Targets: c.Targets)
                    .ToMessage());
            }

        if (!string.IsNullOrWhiteSpace(assistantAnswer))
            list.Add(new ConversationFrame(
                    ConversationRole.Assistant, string.Empty, assistantAnswer, round, ConversationFacts.Empty)
                .ToMessage());

        return list;
    }

    /// <summary>反向映射（history → nodes）：提取 Tool 消息为节点贡献（保留 Stage/SubAgent/Round）；非 Tool/空文本跳过。</summary>
    public IReadOnlyList<ConversationNodeContribution> ToContributions(IReadOnlyList<ConversationMessage>? messages)
    {
        if (messages is null) return Array.Empty<ConversationNodeContribution>();
        var list = new List<ConversationNodeContribution>();
        foreach (var m in messages)
            if (m.Role == ConversationRole.Tool && !string.IsNullOrWhiteSpace(m.Text))
                list.Add(new ConversationNodeContribution(m.Stage ?? "node", m.SubAgent ?? "tool", m.Text, m.Round, m.Targets));
        return list;
    }
}
