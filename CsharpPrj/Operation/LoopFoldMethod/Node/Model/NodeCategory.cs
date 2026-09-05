namespace Operation.LoopFoldMethod.Node.Model;

/// <summary>
/// 动态节点类别（消费域语义标签，可扩展）—— 框架不解释含义，只把类别作为编排/投影的输入。
/// 类别是「收集侧」视角（节点来自哪里、稳定性如何），与「槽位」（编排后摆在哪段）分离：槽位由
/// <see cref="Operation.LoopFoldMethod.Node.Assemble.INodeSlotPolicy"/> 依类别（或显式节点声明）决定。
/// </summary>
public enum NodeCategory
{
    /// <summary>基础·稳定（如提示词域的 L0 系统底座）—— 变化少，宜置于稳定前缀头部。</summary>
    Base,

    /// <summary>动态·每轮变化（如本轮载荷 / 上步产出）—— 居中编排。</summary>
    Dynamic,

    /// <summary>技能·多变（如按任务选中的技能正文）—— 宜置于尾部，避免打断稳定前缀。</summary>
    Skill,

    /// <summary>步骤级指令（当前步骤的操作约束）—— 居中编排。</summary>
    Instruction,

    /// <summary>引用（跨实体/历史的上下文片段）—— 居中编排。</summary>
    Reference,
}
