namespace Share.Conversation.ContextBinding;

/// <summary>
/// 投喂目标 —— 消息进 LLM 的哪个槽位；同时决定 UI 侧气泡类型（头提示层 / 对话历史）。
/// </summary>
public enum FeedTarget
{
    /// <summary>LLM 对话历史槽位（wire [{role,content}] 序列）。</summary>
    History,

    /// <summary>LLM system 提示槽位（L0/L1/L2/SkillBlock 头提示层）。</summary>
    System,

    /// <summary>
    /// 状态变更槽位（task create/activate/pause/stop/settle 等 Δstate）——
    /// 默认**不进** LLM <c>conversation_context</c>（<c>LlmViewProjector.HistoryToWire</c> 仅取 <see cref="History"/>），
    /// 仍全量落库/审计（只增不丢）。History 状态响应智能体（HSRA）的状态产出标此值。
    /// </summary>
    State,
}
