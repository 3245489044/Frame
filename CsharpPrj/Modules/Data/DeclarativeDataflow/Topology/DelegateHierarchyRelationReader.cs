using Modules.DeclarativeDataflow.Model;

namespace Modules.DeclarativeDataflow.Topology;

/// <summary>
/// 声明面驱动的**通用实例关系读取器**：父子事实由宿主**注入**（两个纯函数），本类只持框架类型
/// （<see cref="LevelRef"/> / <see cref="HierarchyContainment"/>），故对任意已声明的包含边同构可跑。
/// <para>
/// 与领域引用图适配器的分工：后者把引用图边翻译为父/子事实（资源类型投影硬编码于具体域）；
/// 本类是**宿主注入式**读取器，供非引用图来源（如语言声明面编译产出的层图 + 节点定位三元组）
/// 复用同一 <see cref="IHierarchyRelationReader"/> 端口，从而**不需要**在框架侧引入语言类型。
/// </para>
/// <para>只读、不写边；未注入事实函数 → 子层空集 / 父层 null（对齐「未装配返回空集」模式，不抛不阻断）。</para>
/// </summary>
public sealed class DelegateHierarchyRelationReader : IHierarchyRelationReader
{
    private readonly Func<LevelRef, HierarchyContainment, IReadOnlyList<LevelRef>>? _childrenOf;
    private readonly Func<LevelRef, string?, HierarchyContainment, LevelRef?>? _parentOf;

    /// <param name="childrenOf">父节点 + 包含声明 → 子节点事实（可空：未注入 = 子层查询返回空集）。</param>
    /// <param name="parentOf">子节点 + 父层 Id（可空 = 不消歧）+ 包含声明 → 父节点事实（可空：反向查询返回 null）。</param>
    public DelegateHierarchyRelationReader(
        Func<LevelRef, HierarchyContainment, IReadOnlyList<LevelRef>>? childrenOf,
        Func<LevelRef, string?, HierarchyContainment, LevelRef?>? parentOf = null)
    {
        _childrenOf = childrenOf;
        _parentOf = parentOf;
    }

    /// <summary>是否已注入正向（父 → 子）事实。</summary>
    public bool IsAttached => _childrenOf is not null;

    /// <summary>是否已注入反向（子 → 父）事实。</summary>
    public bool SupportsReverse => _parentOf is not null;

    /// <inheritdoc />
    public Task<IReadOnlyList<LevelRef>> ChildrenOfAsync(
        LevelRef parent, HierarchyContainment containment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(containment);
        if (!IsAttached || parent.IsEmpty) return Task.FromResult<IReadOnlyList<LevelRef>>(Array.Empty<LevelRef>());

        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Normalize(_childrenOf!(parent, containment)));
    }

    /// <inheritdoc />
    public Task<LevelRef?> ParentOfAsync(
        LevelRef child, HierarchyContainment containment, CancellationToken ct = default)
        => ParentOfAsync(child, parentLevelId: null, containment, ct);

    /// <inheritdoc />
    public Task<LevelRef?> ParentOfAsync(
        LevelRef child, string parentLevelId, HierarchyContainment containment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(containment);
        if (_parentOf is null || child.IsEmpty) return Task.FromResult<LevelRef?>(null);

        ct.ThrowIfCancellationRequested();
        var parent = _parentOf(child, parentLevelId, containment);
        return Task.FromResult(parent is { IsEmpty: false } p ? (LevelRef?)p : null);
    }

    /// <summary>保序去重 + 过滤空引用（事实来源可能重复产出同一实体）。</summary>
    private static IReadOnlyList<LevelRef> Normalize(IReadOnlyList<LevelRef>? facts)
        => LevelRefSet.Normalize(facts);
}
