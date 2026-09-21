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
}
