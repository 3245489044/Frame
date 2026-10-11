namespace Operation.HierarchyMethod.History.Conversation;

/// <summary>
/// 切分**中性条目**（域无关；由宿主 <c>IConversationItemReader</c> 从原始载荷映射而来）。
/// <para>存在理由：切分算法（分组/计数/摘要）只依赖这些中性字段，不感知任何宿主消息形态（如 Cline）。</para>
/// </summary>
/// <param name="Index">源下标（0 起；段 <c>seq</c> = 段内首条下标 + 1）。</param>
/// <param name="Type">原始类型名（宿主域语义；段 Kind 的 Message 模式来源）。</param>
/// <param name="Role">归一角色（user / assistant / tool）。</param>
/// <param name="Text">文本载荷。</param>
/// <param name="Ts">时间戳（可空，不伪造）。</param>
/// <param name="ToolCallId">工具调用标识（可空）。</param>
/// <param name="IsToolCall">是否工具调用请求。</param>
/// <param name="IsToolResult">是否工具执行结果。</param>
/// <param name="IsAssistantOutput">是否 LLM 回合产出（assistant/say）；iteration 由切分器据此派生。</param>
public readonly record struct ConversationItem(
    int Index,
    string Type,
    string Role,
    string Text,
    long? Ts,
    string? ToolCallId,
    bool IsToolCall,
    bool IsToolResult,
    bool IsAssistantOutput);

