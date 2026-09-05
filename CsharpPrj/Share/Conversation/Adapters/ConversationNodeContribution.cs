namespace Share.Conversation.Adapters;

/// <summary>
/// 单个工作流节点（step 产出）对会话的中立贡献 —— 载体无关（不依赖 Agent.Contract.NodeExecutionRecord）。
/// 由 Agent 侧从节点产出投影而来，再交给 <see cref="IConversationNodeMapper"/> 映射为规范对话结构。
/// <see cref="Round"/> = 0 表示由映射器按起始轮次对齐；&gt;0 使用显式轮次。
/// </summary>
public sealed record ConversationNodeContribution(
    string Stage,
    string SubAgent,
    string Text,
    int Round = 0);
