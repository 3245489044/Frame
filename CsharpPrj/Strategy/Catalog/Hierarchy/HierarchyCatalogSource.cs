using System;
using System.Collections.Generic;
using System.Linq;
using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Topology;
using Strategy.Catalog.Model;
using Strategy.Catalog.Ports;

namespace Strategy.Catalog.Hierarchy;

/// <summary>
/// 层次目录源 —— 把 <see cref="HierarchySchema"/>（多级 / 不定深度的**层声明**）投影为目录面，
/// 使「层数由数据决定的集合」（节点域 / 工作流→步骤→函数 / 资源类型发现 …）**不需要专用查询 API**。
/// <para>
/// 投影口径：层 = 条目（<c>Key = LevelId</c>），轴 <c>level</c> = 层 Id，摘要 = 实体类型 + 子层数，
/// 明细 = 该层的处理器 / 子层包含边 / 自递归标记 / 到根层序。
/// </para>
/// <para>
/// 实例级展开（每层有多少实例）走 <see cref="HierarchyCatalogExpander"/>
/// （复用 <c>HierarchyProcessEngine</c> 的遍历 + 环守卫 + 预算），本类只做**结构投影**。
/// </para>
/// </summary>
public sealed class HierarchyCatalogSource : ICatalogSource
{
    /// <summary>层级轴默认 Id。</summary>
    public const string LevelAxisId = "level";

    /// <summary>层级轴默认显示名。</summary>
    public const string LevelAxisDisplay = "层级";

    /// <summary>根层条目标签。</summary>
    public const string RootTag = "root";

    /// <summary>叶子层条目标签。</summary>
    public const string LeafTag = "leaf";

    /// <summary>自递归（不定深度）层条目标签。</summary>
    public const string SelfRecursiveTag = "self-recursive";

    private readonly SchemaTopology _topology;

    /// <param name="sourceId">目录源 Id。</param>
    /// <param name="display">显示名（空则回退 sourceId）。</param>
    /// <param name="schema">层声明（层数不定；自环 = 不定深度）。</param>
    /// <param name="axisId">层级轴 Id（缺省 <see cref="LevelAxisId"/>）。</param>
    public HierarchyCatalogSource(string sourceId, string display, HierarchySchema schema, string? axisId = null)
    {
        SourceId = string.IsNullOrWhiteSpace(sourceId) ? throw new ArgumentException("目录源 Id 不可为空。", nameof(sourceId)) : sourceId;
        Display = CatalogText.Or(display, sourceId);
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        AxisId = string.IsNullOrWhiteSpace(axisId) ? LevelAxisId : axisId!;
        _topology = new SchemaTopology(schema);
        Axes = new[] { CatalogAxis.Of(AxisId, LevelAxisDisplay) };
    }

    /// <inheritdoc />
    public string SourceId { get; }

    /// <inheritdoc />
    public string Display { get; }

    /// <inheritdoc />
    public IReadOnlyList<CatalogAxis> Axes { get; }

    /// <summary>层声明（真源）。</summary>
    public HierarchySchema Schema { get; }

    /// <summary>层级轴 Id（本实例生效值）。</summary>
    public string AxisId { get; }

    /// <summary>便捷构造（目录源 Id 缺省派生自 schema 根层）。</summary>
    public static HierarchyCatalogSource For(HierarchySchema schema, string? sourceId = null, string? display = null)
    {
        var root = schema?.RootLevelId ?? "root";
        return new(sourceId ?? $"hierarchy:{root}",
                   CatalogText.Or(display, $"{root} 层次"),
                   schema!);
    }

    /// <inheritdoc />
    public IReadOnlyList<CatalogItem> Enumerate()
    {
        var order = _topology.LevelOrder.ToList();
        var items = new List<CatalogItem>(order.Count);
        foreach (var levelId in order)
        {
            var level = Schema.Level(levelId);
            if (level is null) continue;
            items.Add(ToItem(level, order.IndexOf(levelId)));
        }
        return items;
    }

    /// <inheritdoc />
    public CatalogDetail? Describe(string key)
    {
        var level = Schema.Level(key);
        if (level is null) return null;

        var fields = new List<CatalogField>
        {
            new("层 Id", level.LevelId),
            new("实体类型", level.EntityType),
            new("处理器", string.IsNullOrWhiteSpace(level.ProcessId) ? null : level.ProcessId),
            new("自递归（不定深度）", level.ContainsSelf ? "是（深度由预算收口）" : "否"),
            new("到根层序", _topology.LevelOrder.ToList().IndexOf(level.LevelId).ToString()),
            new("父层", Join(_topology.ParentLevelIds(level.LevelId))),
            new("子层", Join(level.ChildLevelIds)),
        };
        foreach (var containment in level.Contains ?? Array.Empty<HierarchyContainment>())
        {
            fields.Add(new CatalogField($"包含边 → {containment.ChildLevelId}", containment.ContainsEdgeType));
        }

        return CatalogDetail.Of(level.LevelId, level.LevelId, fields,
            SummaryOf(level), level.ProcessId);
    }

    private CatalogItem ToItem(HierarchyLevelSpec level, int order)
    {
        var tags = new List<string>();
        if (string.Equals(level.LevelId, Schema.RootLevelId, StringComparison.Ordinal)) tags.Add(RootTag);
        if (level.ChildLevelIds.Count == 0) tags.Add(LeafTag);
        if (level.ContainsSelf) tags.Add(SelfRecursiveTag);

        return new CatalogItem(
            level.LevelId,
            level.LevelId,
            new[] { CatalogAxisValue.Of(AxisId, level.LevelId) },
            SummaryOf(level),
            string.IsNullOrWhiteSpace(level.ProcessId) ? null : level.ProcessId,
            Pinned: order == 0,
            Tags: tags);
    }

    private string SummaryOf(HierarchyLevelSpec level)
    {
        var children = level.ChildLevelIds;
        var suffix = children.Count == 0
            ? "叶子层"
            : $"子层 {children.Count}（{Join(children)}）";
        return $"{level.EntityType} · {suffix}";
    }

    private static string Join(IReadOnlyList<string>? values)
        => values is { Count: > 0 } ? string.Join(", ", values) : "(无)";
}
