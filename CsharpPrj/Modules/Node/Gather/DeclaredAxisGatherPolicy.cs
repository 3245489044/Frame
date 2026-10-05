using Modules.Node.Projection;

namespace Modules.Node.Gather;

/// <summary>
/// **多轴聚拢策略**（声明驱动）：读项判别值命中声明族 ⇒ 族名；否则退**二级分组轴**字段值
/// （<see cref="NodeGatherSpec.GroupByField"/>，如 <c>nodeType</c>）。
/// <para>
/// 相对 <see cref="DeclaredFamilyGatherPolicy"/> 的单轴（kind→族），本策允许「异构来源同面板」：
/// 过程类按 kind 家族分轴、其余按声明的二级轴分轴。域无关：轴字段名来自声明。
/// 组序 = 族声明序 → 二级轴（同权重按标签序）。
/// </para>
/// </summary>
public sealed class DeclaredAxisGatherPolicy : INodeGatherPolicy<string>
{
    private readonly NodeGatherSpec _spec;

    public DeclaredAxisGatherPolicy(NodeGatherSpec spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
    }

    /// <summary>分组键 = 族名 ∨ 二级轴字段值 ∨ <see cref="NodeGatherSpec.GenericFamily"/>。</summary>
    public string GroupKey(NodeProjectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _spec.GroupOf(item.Fields);
    }

    /// <summary>表头 = 组键（呈现层可换本地化名而不改框架）。</summary>
    public string Label(string group) => group ?? NodeGatherSpec.GenericFamily;

    /// <summary>族序 = 声明序；二级轴/未声明组排末尾（同权重按标签序）。</summary>
    public int Order(string group)
    {
        var order = _spec.FamilyOrder;
        for (var index = 0; index < order.Count; index++)
        {
            if (string.Equals(order[index], group, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return order.Count;
    }
}
