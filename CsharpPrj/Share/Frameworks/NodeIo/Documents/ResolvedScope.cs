using Modules.Node.Placement;

namespace Share.NodeIo;

/// <summary>已定位位置（位置键 = 身份模板求值产物；读写共用）。</summary>
public sealed record ResolvedPlacement(
    string PlacementId,
    string CarrierId,
    string PositionKey,
    PlacementForm Form,
    string WriterId,
    string? PayloadSchemaId,
    string? RecordType,
    string? ThreadCoordinate,
    string? ProjectionId);

/// <summary>作用域解析结果（失败给原因；<see cref="Ok"/> 由 <see cref="Placements"/> 非空决定）。</summary>
public sealed record ResolvedScope(
    NodeDocumentScope Scope,
    IReadOnlyList<ResolvedPlacement> Placements,
    bool Ok,
    IReadOnlyList<string> Reasons,
    PlacementForm? Form)
{
    public IReadOnlyList<string> PlacementIds => Placements.Select(p => p.PlacementId).ToArray();

    public static ResolvedScope Failed(NodeDocumentScope scope, params string[] reasons) =>
        new(scope, Array.Empty<ResolvedPlacement>(), false, reasons, null);
}
