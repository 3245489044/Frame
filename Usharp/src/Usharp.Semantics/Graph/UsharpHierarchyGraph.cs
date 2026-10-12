using Modules.DeclarativeDataflow.Model;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>划分声明问题类别（<see cref="UsharpHierarchyIssue"/> 的判据；遍历单源内区分报错口径）。</summary>
public enum UsharpHierarchyDeclProblem
{
    /// <summary>缺父空间 / 包含边类型为空（形状非法）。</summary>
    MissingParentOrEdge,

    /// <summary>父空间未声明（层图悬空）。</summary>
    UndeclaredParent,
}

/// <summary>
/// 划分声明的结构化问题（声明序）：声明原文 + 类别 —— 闸A 据此分口径报错，
/// 产出面据此汇总「不可产出」。替代早前只存描述串（丢失类别信息）。
/// </summary>
public readonly record struct UsharpHierarchyIssue(
    UsharpSpaceDecl Space,
    string ParentSpaceId,
    string EdgeType,
    UsharpHierarchyDeclProblem Problem)
{
    /// <summary>描述串（保留既有「不可产出」报文形态；<c>&lt;父&gt;:&lt;边类型&gt;</c>）。</summary>
    public override string ToString()
        => $"[Space] {Space.SpaceId}: {ParentSpaceId}:{EdgeType}";
}

/// <summary>
/// <c>[Space] contains=</c> 子空间图的**唯一次遍历入口**：包含对 / 层 Id 集 / 根 / 自环层 / 层序 /
/// 结构化问题集。闸A（<see cref="UsharpHierarchyGate"/>）与产出面（schema 发射器）共用同一构建。
/// <para>
/// 消费方：层次产出面（schema 发射器）与其闸A 投影 —— 层序委托内核
/// <see cref="HierarchySchema.LevelOrderFromRoot"/>（自环不推进、未声明层跳过），
/// 禁止各处自写 BFS / 根判定 / 自环判定（D13 收敛点；对齐内核单一实现）。
/// </para>
/// </summary>
public sealed class UsharpHierarchyGraph
{
    private readonly IReadOnlyList<(string Parent, string Child, string Edge)> _pairs;

    private UsharpHierarchyGraph(
        IReadOnlyList<(string Parent, string Child, string Edge)> pairs,
        IReadOnlyList<string> levelIds,
        IReadOnlyList<string> roots,
        IReadOnlyList<string> selfRecursiveLayers,
        IReadOnlyList<UsharpHierarchyIssue> issues,
        IReadOnlyList<UsharpSpaceDecl> declaredSpaces)
    {
        _pairs = pairs;
        LevelIds = levelIds;
        Roots = roots;
        SelfRecursiveLayers = selfRecursiveLayers;
        Issues = issues;
        DeclaredSpaces = declaredSpaces;
    }

    /// <summary>包含对（父 → 子 + 包含边类型；声明序）。</summary>
    public IReadOnlyList<(string Parent, string Child, string Edge)> Pairs => _pairs;

    /// <summary>参与划分的层 Id 集（声明序去重）。</summary>
    public IReadOnlyList<string> LevelIds { get; }

    /// <summary>根层（非自环的无父层；单根模型要求恰一个）。</summary>
    public IReadOnlyList<string> Roots { get; }

    /// <summary>含自环（<c>contains=自我:edge</c>）的层 Id 集。</summary>
    public IReadOnlyList<string> SelfRecursiveLayers { get; }

    /// <summary>形状非法（缺父 / 边类型为空 / 父未声明）的划分声明（声明序；空 = 全部合法）。</summary>
    public IReadOnlyList<UsharpHierarchyIssue> Issues { get; }

    /// <summary>
    /// 形状非法的划分声明描述串（**派生自 <see cref="Issues"/>**；既有「不可产出」报文消费方的稳定面）。
    /// </summary>
    public IReadOnlyList<string> InvalidDeclarations
        => Issues.Select(i => i.ToString()).ToList();

    /// <summary>含 <c>contains=</c> 的空间声明（声明序；归因用）。</summary>
    public IReadOnlyList<UsharpSpaceDecl> DeclaredSpaces { get; }

    /// <summary>单根成立（恰一个根）。</summary>
    public bool IsSingleRoot => Roots.Count == 1;

    /// <summary>取某层的出向包含（子层 + 边类型；声明序）。</summary>
    public IReadOnlyList<(string Child, string Edge)> ChildrenOf(string levelId)
        => _pairs.Where(p => string.Equals(p.Parent, levelId, StringComparison.Ordinal))
            .Select(p => (p.Child, p.Edge)).ToList();

    /// <summary>
    /// 层序（自根 BFS，自环不推进）—— 委托内核 <see cref="HierarchySchema.LevelOrderFromRoot"/>（单一实现）。
    /// </summary>
    public IReadOnlyList<string> LevelOrder(string root)
    {
        if (LevelIds.Count == 0 || !LevelIds.Contains(root, StringComparer.Ordinal)) return Array.Empty<string>();

        // 委托内核遍历：临时 schema 须带真实包含对（BFS 依 ChildLevelIds 展开）
        var schema = new HierarchySchema(root, LevelIds.Select(id =>
            new HierarchyLevelSpec(id, id, ChildrenOf(id).Select(c => new HierarchyContainment(c.Child, c.Edge)).ToList())));
        return schema.LevelOrderFromRoot();
    }

    /// <summary>构建子空间图；本编译**无** <c>contains=</c> 声明 → null（无层次面）。</summary>
    public static UsharpHierarchyGraph? Build(SpaceRegistry spaces)
    {
        ArgumentNullException.ThrowIfNull(spaces);

        var declared = spaces.Spaces.Where(s => s.Contains.Count > 0).ToList();
        if (declared.Count == 0) return null;

        var pairs = new List<(string Parent, string Child, string Edge)>();
        var issues = new List<UsharpHierarchyIssue>();
        foreach (var space in declared)
            foreach (var containment in space.Contains)
            {
                if (containment.ParentSpaceId.Length == 0 || containment.EdgeType.Length == 0)
                {
                    issues.Add(new UsharpHierarchyIssue(space, containment.ParentSpaceId, containment.EdgeType,
                        UsharpHierarchyDeclProblem.MissingParentOrEdge));
                    continue;
                }

                if (!spaces.TryGet(containment.ParentSpaceId, out _))
                {
                    issues.Add(new UsharpHierarchyIssue(space, containment.ParentSpaceId, containment.EdgeType,
                        UsharpHierarchyDeclProblem.UndeclaredParent));
                    continue;
                }

                pairs.Add((containment.ParentSpaceId, space.SpaceId, containment.EdgeType));
            }

        // 去重保声明序（HashSet 而非 List.Contains 的 O(n²)）；层 Id 集仅含用户层（合成根属闸A 校验投影，不入图）。
        var levelIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var childIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (parent, child, _) in pairs)
        {
            if (seen.Add(parent)) levelIds.Add(parent);
            if (seen.Add(child)) levelIds.Add(child);
            if (!string.Equals(parent, child, StringComparison.Ordinal)) childIds.Add(child);
        }

        var selfRecursive = declared
            .Where(s => s.Contains.Any(c => string.Equals(c.ParentSpaceId, s.SpaceId, StringComparison.Ordinal)))
            .Select(s => s.SpaceId).ToList();

        return new UsharpHierarchyGraph(
            pairs,
            levelIds,
            levelIds.Where(id => !childIds.Contains(id)).ToList(),
            selfRecursive,
            issues,
            declared);
    }
}
