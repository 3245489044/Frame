using Modules.Node.Materialization;
using Modules.Node.Placement;

namespace Share.NodeIo;

public sealed partial class NodeDocumentPort
{
    private Task<NodeMaterializationResult> PatchAsync(
        NodeDocumentChange change,
        ResolvedScope resolved,
        CancellationToken cancellationToken)
    {
        if (change.Patches is not { Count: > 0 })
        {
            return Task.FromResult(Rejected(change.Scope, new[] { NodeDocumentReasons.PatchEmpty }));
        }

        // 写路径收敛：补丁不再自建读写门、不再直连载体；
        // 由框架 MutateAsync 在门内读-改-写，键形门 / 运行域门 / 缺省合并 / 载体异常隔离与 Upsert 同源。
        return _framework.Materializer.MutateAsync(
            new NodeMaterializeRequest(
                change.Scope.KeyContext,
                change.PayloadJson ?? string.Empty,
                change.Scope.Domain,
                resolved.PlacementIds,
                Meta: change.RecordMeta),
            (placement, currentJson) => PatchPlacement(change, placement, currentJson),
            cancellationToken);
    }

    private NodePlacementMutation PatchPlacement(
        NodeDocumentChange change,
        PlacementSpec placement,
        string? currentJson)
    {
        if (currentJson is null)
        {
            return NodePlacementMutation.Reject(NodeDocumentReasons.PatchKeyMissing, "patch.rejected");
        }

        if (change.ExpectedDigest is { Length: > 0 } expected &&
            !string.Equals(NodeDocumentDigest.Of(currentJson), expected, StringComparison.Ordinal))
        {
            return NodePlacementMutation.Reject(NodeDocumentReasons.PatchDigestMismatch, "patch.rejected");
        }

        if (!NodeDocumentPatcher.TryApply(currentJson, change.Patches!, out var patched, out var patchReason))
        {
            return NodePlacementMutation.Reject(
                NodeDocumentReasons.PatchApplyFailed + ":" + patchReason, "patch.rejected");
        }

        var validation = NodePayloadValidator.Validate(
            _schemas.Resolve(placement.PayloadSchemaId), placement.Form, patched, change.Scope.KeyContext);
        if (!validation.Ok)
        {
            return NodePlacementMutation.Reject(validation.Reason, "patch.rejected");
        }

        return NodePlacementMutation.Accept(patched);
    }
}
