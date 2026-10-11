namespace Operation.HierarchyMethod.History.Conversation;

/// <summary>重组后的单条历史消息（读侧派生结果，<b>不落库</b>：重组 = 读侧投影）。</summary>
/// <param name="Key">显示键（读侧内存合成）。</param>
/// <param name="Role">角色。</param>
/// <param name="Kind">内容类别。</param>
/// <param name="Text">文本。</param>
/// <param name="Mark">入史标记（载荷字段）。</param>
/// <param name="Parent">父键引用（可空）。</param>
public sealed record ComposedHistoryMessage(
    string Key, string Role, string Kind, string Text, string Mark, string? Parent);
