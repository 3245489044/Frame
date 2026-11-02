using Modules.Node.Projection;

namespace Share.NodeIo;

/// <summary>
/// 读侧投影适配（声明 <c>projection</c> 的消费点）：位置声明的 <c>ProjectionId</c> → 投影实现。
/// 投影只塑形已读到的行，不读库、不解析位置。
/// </summary>
public interface INodeDocumentProjection
{
    string Id { get; }

    IReadOnlyList<NodeProjectionItem> Project(
        NodeDocumentScope scope,
        ResolvedPlacement placement,
        IReadOnlyList<NodeProjectionItem> items,
        NodePayloadSchema schema);
}

/// <summary>投影注册表（未声明 / 未注册 ⇒ 原样文档，零回归）。</summary>
public sealed class NodeDocumentProjectionRegistry
{
    public const string DocumentId = "document";
    public const string StreamFramesId = "stream-frames";

    private readonly Dictionary<string, INodeDocumentProjection> _projections = new(StringComparer.Ordinal);

    public NodeDocumentProjectionRegistry(bool registerDefaults = true)
    {
        if (!registerDefaults)
        {
            return;
        }

        Register(new DocumentProjection());
        Register(new StreamFramesProjection());
    }

    public void Register(INodeDocumentProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        _projections[projection.Id] = projection;
    }

    public bool TryResolve(string? id, out INodeDocumentProjection projection)
    {
        var key = string.IsNullOrWhiteSpace(id) ? DocumentId : id!;
        return _projections.TryGetValue(key, out projection!);
    }

    public INodeDocumentProjection Resolve(string? id) =>
        TryResolve(id, out var projection) ? projection : _projections[DocumentId];
}
