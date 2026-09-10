using System;
using System.Collections.Generic;
using System.Linq;
using Modules.DeclarativeDataflow.Model;

namespace Modules.DeclarativeDataflow.Topology;

/// <summary>
/// 层次 schema 的纯拓扑（无 IO、可 CI 单测）：回答"层与层之间能不能连"。
/// <para>
/// 只读、无状态；实例级父子查询见 <see cref="RelationalHierarchyTopology"/>。
/// 本类取代"按层硬编码 switch"——一切层关系判定都从 <see cref="HierarchySchema"/> 导出。
/// </para>
/// </summary>
public sealed class SchemaTopology
{
    /// <param name="schema">层次 schema（已声明层与包含关系）。</param>
    public SchemaTopology(HierarchySchema schema)
        => Schema = schema ?? throw new ArgumentNullException(nameof(schema));

    /// <summary>被包装的层次 schema。</summary>
    public HierarchySchema Schema { get; }

    /// <summary>层序（自根按包含边广度优先展开；自环不参与推进）。</summary>
    public IReadOnlyList<string> LevelOrder
    {
        get
        {
            var order = new List<string>(Schema.LevelOrderFromRoot());
            var seen = new HashSet<string>(order, StringComparer.Ordinal);
            // 不可达层兜底追加（保持 schema 声明序）：本类可能在未过校验器的 schema 上使用，
            // 此处保证层序尽量完整；"不可达"本身的诊断由 HierarchySchemaValidator 负责。
            foreach (var level in Schema.Levels)
                if (seen.Add(level.LevelId)) order.Add(level.LevelId);
            return order;
        }
    }

    /// <summary>直接子层 Id 集（保序、去重）。</summary>
    public IReadOnlyList<string> ChildLevelIds(string parentLevelId) => Schema.ChildLevelIds(parentLevelId);

    /// <summary>指向该子层的全部父侧包含声明对（父层 Id + 声明；自环层会使本层同时为父）。</summary>
    public IReadOnlyList<(string ParentLevelId, HierarchyContainment Containment)> ParentContainmentPairs(string childLevelId)
        => Schema.ContainmentPairs()
            .Where(p => string.Equals(p.Containment.ChildLevelId, childLevelId, StringComparison.Ordinal))
            .Select(p => (p.Parent.LevelId, p.Containment))
            .ToList();

    /// <summary>直接父层 Id 集：所有在声明中把 <paramref name="childLevelId"/> 列为子层的层（自环层会使本层同时为父）。</summary>
    public IReadOnlyList<string> ParentLevelIds(string childLevelId)
        => ParentContainmentPairs(childLevelId)
            .Select(p => p.ParentLevelId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>指向该子层的全部父侧包含声明（实例级反向解析用；含自环声明）。</summary>
    public IReadOnlyList<HierarchyContainment> ParentContainments(string childLevelId)
        => ParentContainmentPairs(childLevelId)
            .Select(p => p.Containment)
            .ToList();

    /// <summary>取父子层之间的包含边类型；不存在返回 null。</summary>
    public string? EdgeFromTo(string parentLevelId, string childLevelId)
        => Schema.Containment(parentLevelId, childLevelId)?.ContainsEdgeType;

    /// <summary>层的实体类型名（层未声明返回 null）。</summary>
    public string? EntityTypeOf(string levelId) => Schema.Level(levelId)?.EntityType;

    /// <summary>层是否声明了自环（不定深度）。</summary>
    public bool IsSelfRecursive(string levelId) => Schema.Level(levelId)?.ContainsSelf ?? false;
}
