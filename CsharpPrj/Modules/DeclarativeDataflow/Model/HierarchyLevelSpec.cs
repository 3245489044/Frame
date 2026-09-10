using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.DeclarativeDataflow.Model;

/// <summary>
/// 一条包含声明（父层 → 子层）：层次图的最小关系单元。
/// <para>
/// <see cref="ChildLevelId"/> 与本层 <see cref="HierarchyLevelSpec.LevelId"/> 相同即**自递归**——
/// 这是"同构不定深度"（如子任务树）的表达方式，深度由运行期数据决定，由深度预算收口。
/// </para>
/// </summary>
/// <param name="ChildLevelId">子层标识（可等于父层 = 自环）。</param>
/// <param name="ContainsEdgeType">父 → 子 包含边类型（外部词汇，非本框架枚举）。</param>
/// <param name="ParentEdgeType">可选：子 → 父 使用独立边类型时声明；缺省由读取端口按 <paramref name="ContainsEdgeType"/> 反向查询。</param>
public sealed record HierarchyContainment(
    string ChildLevelId,
    string ContainsEdgeType,
    string? ParentEdgeType = null)
{
    /// <summary>该包含声明是否为自递归（父层包含自身）。</summary>
    public bool IsSelfRecursive(string parentLevelId)
        => string.Equals(parentLevelId, ChildLevelId, StringComparison.Ordinal);

    /// <summary>用于反向（子 → 父）查询的边类型。</summary>
    public string EffectiveParentEdgeType => string.IsNullOrWhiteSpace(ParentEdgeType) ? ContainsEdgeType : ParentEdgeType!;
}

/// <summary>
/// 层声明（层次外置的最小外置单元）——"该层是什么 + 它包含哪些层 + 它默认怎么被处理"。
/// <para>
/// 层不再等同于 C# 类型：新增一层（异构 N 层）或让某层自嵌套（不定深度）都只改本声明数据，
/// 不改框架代码。每层的处理方式经 <see cref="ProcessId"/> 外置（可被
/// <see cref="Processing.HierarchyProcessPlan.ProcessIdOverride"/> 覆盖）。
/// </para>
/// </summary>
/// <param name="LevelId">层标识（schema 内唯一）。</param>
/// <param name="EntityType">该层实体类型名（供上层映射到其存储/资源类型；框架不解释）。</param>
/// <param name="Contains">出向包含声明集（空 = 叶子层）。</param>
/// <param name="ProcessId">该层默认处理器 Id（可空 = 容器层透传，无处理逻辑）。</param>
public sealed record HierarchyLevelSpec(
    string LevelId,
    string EntityType,
    IReadOnlyList<HierarchyContainment> Contains,
    string? ProcessId = null)
{
    /// <summary>叶子层（不包含任何子层）。</summary>
    public static HierarchyLevelSpec Leaf(string levelId, string entityType, string? processId = null)
        => new(levelId, entityType, Array.Empty<HierarchyContainment>(), processId);

    /// <summary>是否声明了至少一条包含关系（统一的空值兜底判据，避免各处写法漂移）。</summary>
    private bool HasContains => Contains is { Count: > 0 };

    /// <summary>本层是否含自环声明（决定不定深度是否可能出现）。</summary>
    public bool ContainsSelf => HasContains && Contains.Any(c => c.IsSelfRecursive(LevelId));

    /// <summary>全部子层 Id（保序、去重）。</summary>
    public IReadOnlyList<string> ChildLevelIds
        => HasContains
            ? Contains.Select(c => c.ChildLevelId).Distinct(StringComparer.Ordinal).ToList()
            : Array.Empty<string>();
}
