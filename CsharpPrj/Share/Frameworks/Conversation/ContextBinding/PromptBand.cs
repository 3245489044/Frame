namespace Share.Conversation.ContextBinding;

/// <summary>
/// L0 分段（按 DSL 承载技能/文件拆分）：一段 = 一个技能（= 一个 SKILL.md 文件）。
/// 供写侧快照携带分段边界，使 UI 头提示层按文件逐段渲染（一文件一气泡），喂 LLM 的合并 L0 逐字不变。
/// </summary>
/// <param name="Name">技能名（SKILL.md 所属目录名，如 l0-format；气泡标签用）。</param>
/// <param name="Text">段正文。</param>
public sealed record PromptBand(string Name, string Text);