using Modules.Node.Projection;

namespace Modules.Node.Gather;

/// <summary>
/// **聚拢引擎**（第二→三段编排）：<c>读项集合 → 聚拢（分组）→ 塑形 → 结果</c>。
/// <para>
/// 配合第一段 <see cref="INodeReadAdapter"/> 即成完整「适配 + 聚拢」通用框架。
/// 引擎本身**零领域知识**：域键/词表/聚拢策略/目标形态全部由业务侧注入
/// （<c>TGroupKey</c>/<c>TShape</c> 泛型）。
/// </para>
/// </summary>
/// <typeparam name="TGroupKey">分组键。</typeparam>
/// <typeparam name="TShape">目标形态。</typeparam>
public sealed class NodeGatherEngine<TGroupKey, TShape>
    where TGroupKey : notnull
{
    private readonly INodeGatherPolicy<TGroupKey> _policy;
    private readonly IReadOnlyList<INodeShaper<TShape>> _shapers;
    private readonly Func<NodeProjectionItem, string> _kindSelector;

    /// <param name="policy">聚拢策略（第二段）。</param>
    /// <param name="shapers">塑形器（第三段；按序择首个认领者）。</param>
    /// <param name="kindSelector">读项 → 判别值（业务侧由声明 <c>kindField</c> 提供，框架不自持领域字段名）。</param>
    public NodeGatherEngine(
        INodeGatherPolicy<TGroupKey> policy,
        IReadOnlyList<INodeShaper<TShape>> shapers,
        Func<NodeProjectionItem, string> kindSelector)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _shapers = shapers ?? throw new ArgumentNullException(nameof(shapers));
        _kindSelector = kindSelector ?? throw new ArgumentNullException(nameof(kindSelector));
    }

    /// <summary>
    /// 编排：聚拢（族序声明序）→ 组内保序（键序数序 N6）→ 塑形（声明优先）。
    /// <paramref name="read"/> 仅入结果为诊断字段，不参与「聚拢出什么」。
    /// </summary>
    /// <param name="domainKey">域键。</param>
    /// <param name="container">容器（任务键 / 会话键）。</param>
    /// <param name="items">单一化读项序列（<see cref="INodeReadAdapter"/> 产物；null 项忽略）。</param>
    /// <param name="read">适配三态（诊断/归因）。</param>
    public NodeGatherResult<TGroupKey, TShape> Build(
        string domainKey,
        string container,
        IEnumerable<NodeProjectionItem> items,
        NodeGatherOutcome read)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(read);

        var buckets = new Dictionary<TGroupKey, List<(string Key, TShape Shape)>>();
        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            var shape = Shape(_kindSelector(item), item);
            if (shape is null)
            {
                continue; // shaped=false / 无塑形器认领 ⇒ 该行不产出
            }

            var groupKey = _policy.GroupKey(item);
            if (!buckets.TryGetValue(groupKey, out var list))
            {
                list = new List<(string, TShape)>();
                buckets[groupKey] = list;
            }

            list.Add((item.Key, shape));
        }

        var groups = buckets
            .OrderBy(pair => _policy.Order(pair.Key))
            .ThenBy(pair => _policy.Label(pair.Key), StringComparer.Ordinal)
            .Select(pair =>
            {
                var ordered = pair.Value
                    .OrderBy(entry => entry.Key, NodeKeyOrdinalComparer.Instance)
                    .Select(entry => entry.Shape)
                    .ToArray();
                return new NodeShapeGroup<TGroupKey, TShape>(
                    pair.Key, _policy.Label(pair.Key), ordered.Length, ordered);
            })
            .ToArray();

        return new NodeGatherResult<TGroupKey, TShape>(
            domainKey ?? string.Empty, container ?? string.Empty, groups, read, read.IsOk);
    }

    private TShape? Shape(string kind, NodeProjectionItem item)
    {
        foreach (var shaper in _shapers)
        {
            if (shaper.Claims(kind))
            {
                return shaper.Shape(item);
            }
        }

        return default;
    }
}
