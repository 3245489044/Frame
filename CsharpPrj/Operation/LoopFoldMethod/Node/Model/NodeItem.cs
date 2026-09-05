namespace Operation.LoopFoldMethod.Node.Model;

/// <summary>
/// 动态节点（不落库，内存即时组装）—— 通用载荷节点，非提示词强相关。
/// 一个节点 = 类别 + 段内序 + 来源 + 任意载荷；<see cref="Payload"/> 为 <see cref="object"/>，
/// 可承载提示词文本、引用、文档片段、查询等任意消费域数据。节点不带持久化身份（Key 可空，仅作可选项）。
/// 槽位 <see cref="Slot"/> 可空（未声明则由 <see cref="Operation.LoopFoldMethod.Node.Assemble.INodeSlotPolicy"/> 依类别分配）；
/// 显式声明时优先于默认策略。
/// </summary>
/// <param name="Category">节点类别（收集侧语义标签）。</param>
/// <param name="Order">段内顺序（排序键；同序保持源序稳定）。</param>
/// <param name="Source">来源标识（哪个节点源/收集者产出，供诊断）。</param>
/// <param name="Payload">节点载荷（任意类型，消费域投影器解释）。</param>
/// <param name="Key">可选键（供投影器/去重引用）。</param>
/// <param name="Slot">可选显式槽位（null = 由槽位策略依类别分配）。</param>
public sealed record NodeItem(
    NodeCategory Category,
    int Order,
    string Source,
    object Payload,
    string? Key = null,
    NodeSlot? Slot = null);
