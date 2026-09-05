using Operation.LoopFoldMethod.Node.Model;

namespace Operation.LoopFoldMethod.Node.Assemble;

/// <summary>
/// 槽位策略端口 —— 把一个节点解析到 <see cref="NodeSlot"/>（头/中/尾）的扩展点。
/// 消费域注入自己的排布策略（如提示词域「稳定前缀优先」）；框架默认实现
/// <see cref="CacheStableSlotPolicy"/> 为通用的「稳定前置、多变后置」策略，非提示词强相关。
/// </summary>
public interface INodeSlotPolicy
{
    /// <summary>解析节点的编排槽位（显式节点 Slot 优先，否则依类别）。</summary>
    NodeSlot Resolve(NodeItem node);
}
