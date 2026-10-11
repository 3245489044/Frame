namespace Operation.HierarchyMethod.History.Conversation;

/// <summary>
/// 历史切分模式（域无关；模式词表由宿主声明面校验，未声明即拒绝）。
/// </summary>
public enum SegmentationMode
{
    /// <summary>逐条消息一段。</summary>
    Message,

    /// <summary>按 LLM 回合（assistant/say 起新段）成段。</summary>
    Iteration,

    /// <summary>工具调用 → 结果成对成段（未配对则单独成段）。</summary>
    Tool,

    /// <summary>按角色切换成段。</summary>
    Role,

    /// <summary>按正则切（<see cref="SegmentationSpec.Value"/> 为模式）。</summary>
    Regex,

    /// <summary>累计至 <see cref="SegmentationSpec.BudgetLimit"/>（chars）成段。</summary>
    Budget,

    /// <summary>按显式标记成段（<see cref="SegmentationSpec.Value"/> 为标记文本，命中处起新段）。</summary>
    Marker,
}
