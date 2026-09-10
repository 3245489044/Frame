using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;

namespace Modules.DeclarativeDataflow.Topology;

/// <summary>
/// 层次关系读取端口（窄接口，实例级）——把"某节点的子节点 / 父节点"这一事实从具体存储解耦出来。
/// <para>
/// 框架只读、不写：实现由上层提供（如引用图只读适配器），未装配时上层拓扑返回空集，
/// 纯内存 / DB-only 路径不受阻断。窄接口便于以假实现做零 IO 单测。
/// </para>
/// </summary>
public interface IHierarchyRelationReader
{
    /// <summary>取父节点在指定包含声明下的子节点（实例级）。</summary>
    Task<IReadOnlyList<LevelRef>> ChildrenOfAsync(LevelRef parent, HierarchyContainment containment, CancellationToken ct = default);

    /// <summary>
    /// 反向取子节点的父节点（沿指定包含声明；无父返回 null）。
    /// 本重载不区分父层：多父层共用同一包含边类型时存在歧义，需要精确定位时重写父层感知重载。
    /// </summary>
    Task<LevelRef?> ParentOfAsync(LevelRef child, HierarchyContainment containment, CancellationToken ct = default);

    /// <summary>父层感知的反向查询：<paramref name="parentLevelId"/> 精确定位包含声明所属父层（多父同边类型时消歧）。</summary>
    Task<LevelRef?> ParentOfAsync(
        LevelRef child, string parentLevelId, HierarchyContainment containment, CancellationToken ct = default)
        => ParentOfAsync(child, containment, ct);
}

/// <summary>
/// 层次实例拓扑（层无关）：以 <see cref="SchemaTopology"/> 描述结构、以 <see cref="IHierarchyRelationReader"/> 读取实例关系。
/// <para>
/// 取代"每层一个硬编码分支"的父子解析：父 / 子 / 祖先 / 后代全部按 schema 声明的包含边推导，
/// 支持异构 N 层与自环（不定深度）。所有查询只读，写边由上层单一写通道负责（TH-12）。
/// </para>
/// </summary>
public sealed class RelationalHierarchyTopology
{
    private readonly IHierarchyRelationReader? _reader;

    /// <param name="schema">纯 schema 拓扑（结构来源）。</param>
    /// <param name="reader">实例关系读取端口（可空；未装配 = 只读查询返回空集，不阻断装配）。</param>
    public RelationalHierarchyTopology(SchemaTopology schema, IHierarchyRelationReader? reader = null)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _reader = reader;
    }

    /// <summary>纯 schema 拓扑（结构查询）。</summary>
    public SchemaTopology Schema { get; }

    /// <summary>底层层次 schema。</summary>
    public HierarchySchema RawSchema => Schema.Schema;

    /// <summary>是否已装配实例关系读取端口。</summary>
    public bool IsAttached => _reader is not null;

    /// <summary>取子节点集合（按 schema 声明的全部包含声明聚合；未装配端口 / 层未声明返回空集；按实体去重）。</summary>
    public async Task<IReadOnlyList<LevelRef>> ChildrenAsync(LevelRef parent, CancellationToken ct = default)
    {
        if (_reader is null || parent.IsEmpty) return Array.Empty<LevelRef>();

        var results = new List<LevelRef>();
        var seen = new HashSet<LevelRef>();
        foreach (var containment in RawSchema.Containments(parent.LevelId))
        {
            var children = await _reader.ChildrenOfAsync(parent, containment, ct).ConfigureAwait(false);
            if (children is null) continue;
            foreach (var child in children)
            {
                if (child.IsEmpty) continue;
                if (seen.Add(child)) results.Add(child);
            }
        }
        return results;
    }

    /// <summary>取父节点（指向该子层的全部父侧包含声明逐一尝试；无父 / 未装配返回 null）。</summary>
    public async Task<LevelRef?> ParentAsync(LevelRef child, CancellationToken ct = default)
    {
        if (_reader is null || child.IsEmpty) return null;
        // 父层感知：多父层共用同一包含边类型时按父层 Id 消歧（缺省实现回落到不区分父层的查询）。
        foreach (var (parentLevelId, containment) in Schema.ParentContainmentPairs(child.LevelId))
        {
            var parent = await _reader.ParentOfAsync(child, parentLevelId, containment, ct).ConfigureAwait(false);
            if (parent is { } p && !p.IsEmpty) return p;
        }
        return null;
    }

    /// <summary>取祖先链（自近及远；带环守卫）。</summary>
    public async Task<IReadOnlyList<LevelRef>> AncestorsAsync(LevelRef node, CancellationToken ct = default)
    {
        var chain = new List<LevelRef>();
        if (node.IsEmpty) return chain;
        var seen = new HashSet<LevelRef> { node };
        var cursor = node;
        while (true)
        {
            var parent = await ParentAsync(cursor, ct).ConfigureAwait(false);
            if (parent is not { } p || p.IsEmpty) break;
            if (!seen.Add(p)) break; // 环守卫：祖先链出现重复即停
            chain.Add(p);
            cursor = p;
        }
        return chain;
    }

    /// <summary>
    /// 取后代集合（广度优先；到达 <paramref name="stopLevelId"/> 不再向下展开；带环守卫与深度上限）。
    /// <para>
    /// <paramref name="maxDepth"/> 语义：收集深度 <c>&lt; maxDepth</c> 的后代（根 = 1），即最多向下展开
    /// <c>maxDepth - 1</c> 层；与 <see cref="Budget.ProcessBudget.MaxDepth"/>（"可处理的最大路径深度"）含义不同，
    /// 二者不可互换。
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<LevelRef>> DescendantsAsync(
        LevelRef node, string? stopLevelId = null, int maxDepth = 64, CancellationToken ct = default)
    {
        var results = new List<LevelRef>();
        if (node.IsEmpty || maxDepth <= 0) return results;

        var seen = new HashSet<LevelRef> { node };
        var queue = new Queue<(LevelRef Node, int Depth)>();
        queue.Enqueue((node, 1));
        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth >= maxDepth) continue;
            if (!string.IsNullOrWhiteSpace(stopLevelId) && string.Equals(current.LevelId, stopLevelId, StringComparison.Ordinal))
                continue;
            foreach (var child in await ChildrenAsync(current, ct).ConfigureAwait(false))
            {
                if (!seen.Add(child)) continue;
                results.Add(child);
                queue.Enqueue((child, depth + 1));
            }
        }
        return results;
    }
}
