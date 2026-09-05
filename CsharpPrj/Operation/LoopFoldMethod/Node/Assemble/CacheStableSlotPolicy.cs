using Operation.LoopFoldMethod.Node.Model;

namespace Operation.LoopFoldMethod.Node.Assemble;

/// <summary>
/// 通用「缓存友好」稳定前缀槽位策略（默认）：稳定类别前置、多变类别后置。
/// Base→Head（稳定前缀头部）、Skill→Tail（多变后缀）、Dynamic/Instruction/Reference→Body（中间区）；
/// 节点显式声明 Slot 时优先（消费域可局部覆盖）。该排布最大化「稳定前缀缓存命中」，
/// 对任何消费域（提示词/引用/文档）通用，非提示词强相关。
/// </summary>
public sealed class CacheStableSlotPolicy : INodeSlotPolicy
{
    /// <summary>共享实例（无状态纯函数）。</summary>
    public static readonly CacheStableSlotPolicy Instance = new();

    /// <inheritdoc />
    public NodeSlot Resolve(NodeItem node)
    {
        if (node.Slot is not null) return node.Slot.Value;
        return node.Category switch
        {
            NodeCategory.Base => NodeSlot.Head,
            NodeCategory.Skill => NodeSlot.Tail,
            _ => NodeSlot.Body,
        };
    }
}
