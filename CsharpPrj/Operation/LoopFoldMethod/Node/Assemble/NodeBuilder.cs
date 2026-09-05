using System;
using System.Collections.Generic;
using System.Linq;
using Operation.LoopFoldMethod.Node.Model;

namespace Operation.LoopFoldMethod.Node.Assemble;

/// <summary>
/// 节点构建器（纯函数编排，零 AI / LLM-free）—— 把一组节点按槽位策略稳定排布为
/// <see cref="NodeAssembly"/>（头/中/尾三段）。确定性：段内按 <see cref="NodeItem.Order"/> 升序，
/// 同 Order 保持源序稳定；空输入 → 空装配。这是节点框架的第一编排抽象，不含任何提示词/LLM 语义。
/// </summary>
public static class NodeBuilder
{
    /// <summary>
    /// 构建节点装配。
    /// </summary>
    /// <param name="nodes">原始节点集（可空/空 = 空装配）。</param>
    /// <param name="policy">槽位策略；null = <see cref="CacheStableSlotPolicy.Instance"/>（通用稳定前缀策略）。</param>
    /// <param name="maxPerBand">每段上限（&gt;0 生效，超出截断并保持段内序）；null = 不限。</param>
    public static NodeAssembly Build(IEnumerable<NodeItem>? nodes, INodeSlotPolicy? policy = null, int? maxPerBand = null)
    {
        var p = policy ?? CacheStableSlotPolicy.Instance;
        var head = new List<NodeItem>();
        var body = new List<NodeItem>();
        var tail = new List<NodeItem>();

        if (nodes is not null)
        {
            foreach (var node in nodes)
            {
                switch (p.Resolve(node))
                {
                    case NodeSlot.Head: head.Add(node); break;
                    case NodeSlot.Tail: tail.Add(node); break;
                    default: body.Add(node); break;
                }
            }
        }

        var orderedHead = OrderByStable(head);
        var orderedBody = OrderByStable(body);
        var orderedTail = OrderByStable(tail);

        if (maxPerBand is > 0)
        {
            orderedHead = orderedHead.Take(maxPerBand.Value).ToList();
            orderedBody = orderedBody.Take(maxPerBand.Value).ToList();
            orderedTail = orderedTail.Take(maxPerBand.Value).ToList();
        }

        return new NodeAssembly(orderedHead, orderedBody, orderedTail);
    }

    /// <summary>段内稳定排序：Order 升序；Order 相等保持输入源序（LINQ OrderBy 稳定）。</summary>
    private static List<NodeItem> OrderByStable(IEnumerable<NodeItem> band) => band.OrderBy(n => n.Order).ToList();
}
