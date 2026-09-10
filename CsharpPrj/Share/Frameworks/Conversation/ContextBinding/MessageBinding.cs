namespace Share.Conversation.ContextBinding;

/// <summary>
/// 消息绑定 —— 标注一条规范消息的锚点键 / 来源域 / 投喂目标 / 文本哈希，
/// 供 ContextBindingValidator 依 R1–R6 双向比对（UI ≡ LLM）。
/// </summary>
public sealed record MessageBinding(
    AnchorKey Key,
    MessageOrigin Origin,
    FeedTarget FeedTarget,
    string TextHash)
{
    public bool IsEmpty => Key.IsEmpty;
}
