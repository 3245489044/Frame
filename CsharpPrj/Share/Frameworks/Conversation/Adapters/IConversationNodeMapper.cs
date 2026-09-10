using System.Collections.Generic;

namespace Share.Conversation.Adapters;

/// <summary>
/// 节点贡献 → 规范对话结构（<see cref="ConversationMessage"/> 序列）的映射端口。
/// 把「一条用户输入 + 若干工作流节点产出 + 一条终答」组装为标准对话（user → tool(Stage) → assistant），
/// 供 Cline/Claude 等传统 JSON 对话智能体消费。通用、transport 无关。
/// </summary>
public interface IConversationNodeMapper
{
    /// <summary>
    /// 映射为规范对话消息序列。
    /// </summary>
    /// <param name="contributions">工作流节点产出贡献（每节点 = 一个可折叠气泡）。</param>
    /// <param name="userInput">本轮用户输入（空 = 省略 user 消息）。</param>
    /// <param name="assistantAnswer">本轮终答（空 = 省略 assistant 消息）。</param>
    /// <param name="startRound">用户消息起始轮次（默认 1；节点贡献 Round=0 时对齐此轮）。</param>
    IReadOnlyList<ConversationMessage> MapToConversation(
        IReadOnlyList<ConversationNodeContribution> contributions,
        string? userInput = null,
        string? assistantAnswer = null,
        int startRound = 1);

    /// <summary>
    /// 反向映射（history → nodes）：从规范对话消息序列中提取节点贡献（Tool 消息 → 贡献，含 Stage/SubAgent/Round）。
    /// 与 <see cref="MapToConversation"/> 构成双向同步（nodes ⇄ history），供回放/恢复/再材料化为节点结构。
    /// </summary>
    IReadOnlyList<ConversationNodeContribution> ToContributions(IReadOnlyList<ConversationMessage>? messages);
}
