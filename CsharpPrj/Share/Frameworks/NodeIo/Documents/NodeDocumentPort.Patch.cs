using Modules.Node.Carriers;
using Modules.Node.Diagnostics;
using Modules.Node.Materialization;

namespace Share.NodeIo;

public sealed partial class NodeDocumentPort
{
    private async Task<NodeMaterializationResult> PatchAsync(
        NodeDocumentChange change,
        ResolvedScope resolved,
        CancellationToken cancellationToken)
    {
        if (change.Patches is not { Count: > 0 })
        {
            return Rejected(change.Scope, new[] { NodeDocumentReasons.PatchEmpty });
        }

        var results = new List<NodePlacementResult>();
        var diagnostics = new List<NodeDiagnostic>();
        foreach (var placement in resolved.Placements)
        {
            if (!_framework.Carriers.TryGet(placement.CarrierId, out var carrier))
            {
                results.Add(new NodePlacementResult(
                    placement.PlacementId, placement.PositionKey, null, NodeDocumentReasons.CarrierUnbound));
                continue;
            }

            var outcome = await _rmwGate.RunAsync(
                    $"{placement.CarrierId}|{placement.PositionKey}",
                    () => PatchOneAsync(change, placement, carrier, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            results.Add(new NodePlacementResult(
                placement.PlacementId, placement.PositionKey, outcome.Status, outcome.Reason));
            if (outcome.Status == CarrierWriteStatus.Rejected)
            {
                diagnostics.Add(new NodeDiagnostic(
                    "patch.rejected",
                    NodeDiagnosticSeverity.Error,
                    outcome.Reason ?? NodeDocumentReasons.PatchApplyFailed,
                    placement.PlacementId));
            }
        }

        Report(diagnostics);
        return new NodeMaterializationResult(Aggregate(results), change.Scope.KeyContext, results, diagnostics);
    }

    private async Task<(CarrierWriteStatus Status, string? Reason)> PatchOneAsync(
        NodeDocumentChange change,
        ResolvedPlacement placement,
        INodeCarrier carrier,
        CancellationToken cancellationToken)
    {
        var record = await carrier.ReadAsync(placement.PositionKey, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return (CarrierWriteStatus.Rejected, NodeDocumentReasons.PatchKeyMissing);
        }

        if (change.ExpectedDigest is { Length: > 0 } expected &&
            !string.Equals(NodeDocumentDigest.Of(record.Json), expected, StringComparison.Ordinal))
        {
            return (CarrierWriteStatus.Rejected, NodeDocumentReasons.PatchDigestMismatch);
        }

        if (!NodeDocumentPatcher.TryApply(record.Json, change.Patches!, out var patched, out var patchReason))
        {
            return (CarrierWriteStatus.Rejected, NodeDocumentReasons.PatchApplyFailed + ":" + patchReason);
        }

        var validation = NodePayloadValidator.Validate(
            _schemas.Resolve(placement.PayloadSchemaId), placement.Form, patched, change.Scope.KeyContext);
        if (!validation.Ok)
        {
            return (CarrierWriteStatus.Rejected, validation.Reason);
        }

        var payload = _framework.FormCodec.Encode(placement.Form, patched);
        var write = await carrier.UpsertAsync(placement.PositionKey, payload, cancellationToken)
            .ConfigureAwait(false);
        return (write.Status, write.Reason);
    }
}
