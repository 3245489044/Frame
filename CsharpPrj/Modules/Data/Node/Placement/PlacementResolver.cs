using Modules.Node.Diagnostics;

namespace Modules.Node.Placement;

/// <summary>反解结果：同一 (loc, keyContext) 恰一个 placement 集合（L-2 唯一；多解即错，禁择一静默）。</summary>
public sealed record PlacementResolution(
    string Loc,
    string KeyContext,
    IReadOnlyList<PlacementSpec> Placements,
    bool Resolved)
{
    public static PlacementResolution Empty(string loc, string keyContext) =>
        new(loc, keyContext, Array.Empty<PlacementSpec>(), false);
}

/// <summary>
/// 位置反解端口（loc → Placement）。唯一实现来源=声明（L-3）；失败即 ∅ + 可见化（L-5），禁回退默认载体。
/// loc 不是身份（L-1）：反解只产位置，不产键。
/// </summary>
public interface IPlacementResolver
{
    PlacementResolution Resolve(string loc, string keyContext);
}

/// <summary>未装配实现：∅ + 诊断（对齐 US9001 可见化范式）。</summary>
public sealed class NullPlacementResolver : IPlacementResolver
{
    private readonly INodeDiagnosticsSink _diagnostics;

    public NullPlacementResolver(INodeDiagnosticsSink? diagnostics = null) =>
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;

    public PlacementResolution Resolve(string loc, string keyContext)
    {
        _diagnostics.Report(new NodeDiagnostic(
            "placement.resolver-unbound",
            NodeDiagnosticSeverity.Warning,
            "位置反解端口未装配：位置集合为空（不写、不兜底）",
            $"{loc}|{keyContext}"));
        return PlacementResolution.Empty(loc, keyContext);
    }
}

/// <summary>
/// 声明驱动的反解实现：反解表由外部（声明/语言产出的矩阵）注入，框架内不含 loc→carrier 硬编码（P22）。
/// </summary>
public sealed class DeclaredPlacementResolver : IPlacementResolver
{
    private readonly PlacementRegistry _registry;
    private readonly Dictionary<string, IReadOnlyList<string>> _locToPlacementIds;
    private readonly INodeDiagnosticsSink _diagnostics;

    public DeclaredPlacementResolver(
        PlacementRegistry registry,
        IReadOnlyDictionary<string, IReadOnlyList<string>> locToPlacementIds,
        INodeDiagnosticsSink? diagnostics = null)
    {
        _registry = registry;
        _locToPlacementIds = locToPlacementIds.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    public IReadOnlyList<string> DeclaredLocs => _locToPlacementIds.Keys.ToArray();

    public PlacementResolution Resolve(string loc, string keyContext)
    {
        if (!_locToPlacementIds.TryGetValue(loc, out var placementIds) || placementIds.Count == 0)
        {
            _diagnostics.Report(new NodeDiagnostic(
                "placement.loc-undeclared",
                NodeDiagnosticSeverity.Error,
                "loc 未声明或反解无命中：位置集合为空（禁回退默认载体）",
                $"{loc}|{keyContext}"));
            return PlacementResolution.Empty(loc, keyContext);
        }

        var resolved = new List<PlacementSpec>();
        var unknown = new List<string>();

        foreach (var placementId in placementIds)
        {
            if (_registry.TryGet(placementId, out var spec))
            {
                resolved.Add(spec);
            }
            else
            {
                unknown.Add(placementId);
            }
        }

        if (unknown.Count > 0)
        {
            // L-2：多解/未知位置即错，禁止静默择一
            _diagnostics.Report(new NodeDiagnostic(
                "placement.reverse-ambiguous",
                NodeDiagnosticSeverity.Error,
                $"反解命中未声明的位置: {string.Join(",", unknown)}",
                $"{loc}|{keyContext}"));
            return PlacementResolution.Empty(loc, keyContext);
        }

        return new PlacementResolution(loc, keyContext, resolved, true);
    }
}
