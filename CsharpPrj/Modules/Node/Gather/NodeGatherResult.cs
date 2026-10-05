namespace Modules.Node.Gather;

/// <summary>分组结果（泛型）：组键 + 表头标签 + 组内形态项（保序）。</summary>
/// <param name="GroupKey">分组键（默认 = 家族名）。</param>
/// <param name="Label">表头。</param>
/// <param name="Count">组内项数。</param>
/// <param name="Items">组内形态项（按项键序数序，N6）。</param>
public sealed record NodeShapeGroup<TGroupKey, TShape>(
    TGroupKey GroupKey,
    string Label,
    int Count,
    IReadOnlyList<TShape> Items);

/// <summary>
/// **聚拢结果**（与存储解耦的纯输出 DTO；消费方只认它）。
/// <para>
/// <see cref="Read"/> 承载适配三态/对账信息，但**只是诊断字段**，不参与「聚拢出什么」——
/// 迁移脚手架与结果解耦。
/// </para>
/// </summary>
/// <param name="DomainKey">域键。</param>
/// <param name="Container">容器（任务键 / 会话键）。</param>
/// <param name="Groups">分组序列（按族声明序）。</param>
/// <param name="Read">适配三态（诊断/归因）。</param>
/// <param name="Readable">读面可用（含空集）；不可用 ⇒ 由调用方回退。</param>
public sealed record NodeGatherResult<TGroupKey, TShape>(
    string DomainKey,
    string Container,
    IReadOnlyList<NodeShapeGroup<TGroupKey, TShape>> Groups,
    NodeGatherOutcome Read,
    bool Readable)
{
    /// <summary>总项数（跨组求和；空结果判据）。</summary>
    public int ItemCount
    {
        get
        {
            var total = 0;
            foreach (var group in Groups)
            {
                total += group.Count;
            }

            return total;
        }
    }

    /// <summary>无任何可产出项。</summary>
    public bool IsEmpty => ItemCount == 0;
}
