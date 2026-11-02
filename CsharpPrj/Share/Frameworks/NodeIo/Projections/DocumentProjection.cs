using Modules.Node.Projection;

namespace Share.NodeIo;

/// <summary>原样文档投影（缺省）：不塑形、不丢字段。</summary>
public sealed class DocumentProjection : INodeDocumentProjection
{
    public string Id => NodeDocumentProjectionRegistry.DocumentId;

    public IReadOnlyList<NodeProjectionItem> Project(
        NodeDocumentScope scope,
        ResolvedPlacement placement,
        IReadOnlyList<NodeProjectionItem> items,
        NodePayloadSchema schema) => items;
}
