namespace Share.Conversation.ContextBinding;

/// <summary>
/// 消息来源域 —— 标注一条消息的「投喂目标」语义，供 UI≡LLM 一致性校验（ContextBindingValidator）。
/// 遵循「零额外逻辑」原则：不含 UiOnly/DisplayOnly（任何仅展示、不进 LLM 的消息都属额外逻辑，禁止）。
/// </summary>
public enum MessageOrigin
{
    /// <summary>进 LLM 对话历史（conversation_context wire）。</summary>
    LlmHistory,

    /// <summary>进 LLM system 提示（provide_prompt 装配的 L0/L1/L2/SkillBlock）。</summary>
    LlmSystem,
}
