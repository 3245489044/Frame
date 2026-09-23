using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 空间注册中心 —— 闸A 的唯一判据来源：<c>SpaceId → 坐标集合</c>。
/// 一个空间 = 一组已声明坐标 + 每个坐标的类型与值域（键型空间无值域）。
/// </summary>
public sealed class SpaceRegistry
{
    private readonly Dictionary<string, UsharpSpaceDecl> _spaces = new(StringComparer.Ordinal);
    private readonly List<UsharpDiagnostic> _diagnostics = new();

    /// <summary>已登记空间（声明序）。</summary>
    public IReadOnlyList<UsharpSpaceDecl> Spaces => _spaces.Values.ToList();

    /// <summary>空间数。</summary>
    public int SpaceCount => _spaces.Count;

    /// <summary>全部空间已声明坐标总数（闸A 覆盖面取数：坐标数越多，闸A 拦截面越大）。</summary>
    public int TotalCoordinates => _spaces.Values.Sum(s => s.Axes.Count);

    public IReadOnlyList<UsharpDiagnostic> Diagnostics => _diagnostics;

    /// <summary>登记一个空间声明（重复空间号 → 报错并保留首个）。</summary>
    public void Add(UsharpSpaceDecl decl)
    {
        if (!_spaces.TryAdd(decl.SpaceId, decl))
        {
            _diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.SpaceDup,
                $"空间重复声明：{decl.SpaceId}（首个已登记）", decl.SourceFile, decl.LineNumber));
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var axis in decl.Axes)
            if (!seen.Add(axis.Name))
                _diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.AxisDup,
                    $"空间 {decl.SpaceId} 内坐标重复声明：{axis.Name}", decl.SourceFile, decl.LineNumber));

        // 声明面取值合法性：kind= 在场但非法 ⇒ 显式报错（不静默按 values 装配）
        if (decl.KindRaw is not null
            && !UsharpLanguage.SpaceKinds.Contains(decl.KindRaw, StringComparer.OrdinalIgnoreCase))
            _diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.SpaceKindUnknown,
                $"空间 {decl.SpaceId}: kind='{decl.KindRaw}' 非法（合法值：{string.Join("|", UsharpLanguage.SpaceKinds)}）",
                decl.SourceFile, decl.LineNumber));

        // 缩进块内不可解析的声明行 ⇒ 显式报错（禁止静默丢弃）
        foreach (var line in decl.UnparsedLines)
            _diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.AxisLineUnparsed,
                $"空间 {decl.SpaceId}: 坐标行无法解析（'{line}'）——形如 <坐标>[:<值类>] 或 <坐标>:<值类>(<min>..<max>)",
                decl.SourceFile, decl.LineNumber));

        if (decl.Axes.Count == 0)
            _diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.DeclEmpty,
                $"空间 {decl.SpaceId} 未声明任何坐标（缩进块为空）", decl.SourceFile, decl.LineNumber));
    }

    /// <summary>取空间声明。</summary>
    public bool TryGet(string spaceId, out UsharpSpaceDecl space)
        => _spaces.TryGetValue(spaceId ?? "", out space!);

    /// <summary>闸A 判据：坐标 (spaceId, key) 是否已在空间中声明。</summary>
    public bool HasCoordinate(string spaceId, string key, out UsharpAxis axis)
    {
        axis = null!;
        if (!TryGet(spaceId, out var space)) return false;
        foreach (var a in space.Axes)
        {
            if (!string.Equals(a.Name, key, StringComparison.Ordinal)) continue;
            axis = a;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 祖先空间链（自近及远；沿 <c>contains=</c> 向上，BFS 且去重 —— 多父层时取并集）。
    /// 环由闸A（US1008）报出，此处以 visited 集合保证**永不挂起**（只读遍历，不做环判定）。
    /// </summary>
    public IReadOnlyList<string> AncestorSpaces(string spaceId)
    {
        var result = new List<string>();
        if (!TryGet(spaceId, out var start)) return result;

        var seen = new HashSet<string>(StringComparer.Ordinal) { start.SpaceId };
        var queue = new Queue<string>();
        if (start.Contains.Count > 0) queue.Enqueue(start.SpaceId);

        while (queue.Count > 0)
        {
            if (!TryGet(queue.Dequeue(), out var current)) continue;
            foreach (var containment in current.Contains)
            {
                if (!seen.Add(containment.ParentSpaceId)) continue;
                if (!TryGet(containment.ParentSpaceId, out var parent)) continue;
                result.Add(parent.SpaceId);
                if (parent.Contains.Count > 0) queue.Enqueue(parent.SpaceId);
            }
        }

        return result;
    }

    /// <summary>
    /// 可读域判据（设计方案 §3.2 读侧）：坐标 ∈ 自身空间 ∪ 祖先闭包。
    /// <para>兄弟块 / 跨子树块的连接**不**在此放宽（须显式 `connects=`，见设计方案 §3.8）。</para>
    /// </summary>
    public bool IsReadable(string spaceId, string key, out UsharpAxis axis)
    {
        if (HasCoordinate(spaceId, key, out axis)) return true;
        foreach (var ancestor in AncestorSpaces(spaceId))
            if (HasCoordinate(ancestor, key, out axis)) return true;

        axis = null!;
        return false;
    }

    /// <summary>可读域描述（闸A 报文用；空串 = 仅自身）。</summary>
    public string ReadScope(string spaceId)
    {
        var ancestors = AncestorSpaces(spaceId);
        return ancestors.Count == 0 ? string.Empty : string.Join("/", ancestors);
    }
}
