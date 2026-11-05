using Modules.Node.Projection;

namespace Modules.Node.Gather;

/// <summary>
/// **聚拢策略**（第二段）：把读项映射到分组键（默认 = kind 家族），并给出表头与组间序。
/// <para>
/// 治「分类轴错位」：聚拢轴从静态 <c>NodeType</c> 切回 **kind 家族**（动态节点），
/// 静态域仍可另供 <c>NodeType</c> 轴的策略（跨域复用同一引擎）。
/// </para>
/// </summary>
/// <typeparam name="TGroupKey">分组键（过程/消息域 = 家族名；任务域 = NodeType）。</typeparam>
public interface INodeGatherPolicy<TGroupKey>
{
    /// <summary>读项 → 分组键。</summary>
    TGroupKey GroupKey(NodeProjectionItem item);

    /// <summary>分组键 → 面板表头。</summary>
    string Label(TGroupKey group);

    /// <summary>分组键 → 组间排序权重（小者在前；同权重按标签序）。</summary>
    int Order(TGroupKey group);
}

/// <summary>
/// 内置族聚拢策略：由 <see cref="NodeGatherSpec"/> 的 <c>families</c> 推导，未知 kind 归
/// <see cref="NodeGatherSpec.GenericFamily"/>（D-5 容错），族间按声明序、族内由引擎按项键序数序保序。
/// </summary>
public sealed class DeclaredFamilyGatherPolicy : INodeGatherPolicy<string>
{
    private readonly NodeGatherSpec _spec;

    public DeclaredFamilyGatherPolicy(NodeGatherSpec spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
    }

    /// <summary>分组键 = 读项判别值（<c>kindField</c>）所属家族名。</summary>
    public string GroupKey(NodeProjectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _spec.FamilyOf(_spec.KindOf(item.Fields));
    }

    /// <summary>族表头 = 族名（呈现层可换本地化名而不改框架）。</summary>
    public string Label(string group) => group ?? NodeGatherSpec.GenericFamily;

    /// <summary>族序 = 声明序；未声明族（含 generic）排末尾。</summary>
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
