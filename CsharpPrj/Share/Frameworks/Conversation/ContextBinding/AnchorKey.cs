using System;
using Share.Conversation.Rendering;

namespace Share.Conversation.ContextBinding;

/// <summary>
/// 稳定锚点键 —— 一条消息在「UI ≡ LLM」双向比对中的全局唯一标识。
/// 复用既有键约定：节点 <c>wf.{wf}.{node}</c>（可被 SplitWfKey 解析）、history.{seq}、prompt.l0/l1/l2、skill.{name}.block / skill.block。
/// 键一经发出即参与 R4（全局唯一）与 R5（SplitWfKey 可解析）校验，禁悬空/重复。
/// </summary>
public readonly record struct AnchorKey(string Value)
{
    public static AnchorKey PromptL0 => new("prompt.l0");
    public static AnchorKey PromptL1 => new("prompt.l1");
    public static AnchorKey PromptL2 => new("prompt.l2");

    /// <summary>组合技能块锚点（SkillBlock 经 SkillPromptAssembler 拼为单一文本时的稳定键）。</summary>
    public static AnchorKey SkillCombined => new("skill.block");

    /// <summary>工具面（MCP）契约块锚点 —— 本步可用工具面（工具名 + 描述）的稳定键，
    /// 与技能块同级作独立头提示层段（stage=prompt:tools），使「装配的 MCP」与基础提示词同层可见。</summary>
    public static AnchorKey ToolCombined => new("tool.block");

    /// <summary>模式声明段锚点 —— 注入提示词装配声明的行为模式（如 <c>reply</c>/<c>task.create</c>）；
    /// History 状态响应智能体（HSRA）经此段读取 mode（零重算，UI ≡ LLM）。</summary>
    public static AnchorKey PromptMode => new("prompt.mode");

    /// <summary>按技能名取头块锚点（键约定权威 = <see cref="SkillHistoryMapping.KeyForBlock"/>，禁复制模板）。</summary>
    public static AnchorKey SkillBlock(string name) => new(SkillHistoryMapping.KeyForBlock(name));

    /// <summary>对话序号锚点 history.{seq}（ur_histories 消息按序编号）。</summary>
    public static AnchorKey History(int seq) => new($"history.{seq}");

    /// <summary>节点产出锚点：原样取节点键（wf.{wf}.{node} 或裸 node，与 SplitWfKey 解析一致）。</summary>
    public static AnchorKey Node(string nodeKey) => new(nodeKey);

    public static AnchorKey From(string value) => new(value ?? string.Empty);

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public override string ToString() => Value;
}
