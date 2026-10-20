using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;

namespace Modules.DeclarativeDataflow.Topology;

/// <summary>
/// 层次关系读取器**组合器**（主源优先，空则回退备源）—— 把"多来源读取策略"从各适配器收敛为单一实现。
/// <para>
/// 唯一合并规则：<see cref="ChildrenOfAsync"/> 主源归一化后非空即返回，否则问备源；
/// 两个 <see cref="ParentOfAsync"/> 重载同规则（主源无父则问备源，父层 Id 原样透传以保留消歧）。
/// </para>
/// <para>
/// 边界：本类只是**读取器组合**，不新增遍历 / 图算法，不写边；两个操作数均须是既有
/// <see cref="IHierarchyRelationReader"/> 实现（未装配的读取器返回空集，天然构成"回退"语义）。
/// </para>
/// </summary>
public sealed class CompositeHierarchyRelationReader : IHierarchyRelationReader
{
    private readonly IHierarchyRelationReader _primary;
    private readonly IHierarchyRelationReader? _fallback;

    /// <param name="primary">主来源（优先读取；不可为空）。</param>
    /// <param name="fallback">备来源（主源未覆盖 / 空时回退；可空 = 无备源）。</param>
    public CompositeHierarchyRelationReader(
        IHierarchyRelationReader primary, IHierarchyRelationReader? fallback = null)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback;
    }

    /// <summary>是否装配了备来源（诊断用）。</summary>
    public bool HasFallback => _fallback is not null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<LevelRef>> ChildrenOfAsync(
        LevelRef parent, HierarchyContainment containment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(containment);
        if (parent.IsEmpty) return Array.Empty<LevelRef>();

        var primary = LevelRefSet.Normalize(await _primary.ChildrenOfAsync(parent, containment, ct).ConfigureAwait(false));
        if (primary.Count > 0) return primary;
        if (_fallback is null) return Array.Empty<LevelRef>();

        return LevelRefSet.Normalize(await _fallback.ChildrenOfAsync(parent, containment, ct).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public Task<LevelRef?> ParentOfAsync(
        LevelRef child, HierarchyContainment containment, CancellationToken ct = default)
        => ParentOfAsync(child, parentLevelId: null, containment, ct);

    /// <inheritdoc />
    public async Task<LevelRef?> ParentOfAsync(
        LevelRef child, string parentLevelId, HierarchyContainment containment, CancellationToken ct = default)
        => await ResolveAsync(child, parentLevelId, containment, ct).ConfigureAwait(false);

    private async Task<LevelRef?> ResolveAsync(
        LevelRef child, string? parentLevelId, HierarchyContainment containment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(containment);
        if (child.IsEmpty) return null;

        var primary = parentLevelId is null
            ? await _primary.ParentOfAsync(child, containment, ct).ConfigureAwait(false)
            : await _primary.ParentOfAsync(child, parentLevelId, containment, ct).ConfigureAwait(false);
        if (primary is { IsEmpty: false } hit) return hit;
        if (_fallback is null) return null;

        var fallback = parentLevelId is null
            ? await _fallback.ParentOfAsync(child, containment, ct).ConfigureAwait(false)
            : await _fallback.ParentOfAsync(child, parentLevelId, containment, ct).ConfigureAwait(false);
        return fallback is { IsEmpty: false } other ? other : null;
    }
}
