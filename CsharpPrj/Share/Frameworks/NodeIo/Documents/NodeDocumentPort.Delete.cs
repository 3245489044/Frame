using Modules.Node.Carriers;
using Modules.Node.Diagnostics;
using Modules.Node.Materialization;

namespace Share.NodeIo;

public sealed partial class NodeDocumentPort
{
    private async Task<NodeMaterializationResult> DeleteAsync(
        ResolvedScope resolved,
        CancellationToken cancellationToken)
    {
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

            var deleted = await _rmwGate.RunAsync(
                    $"{placement.CarrierId}|{placement.PositionKey}",
                    () => carrier.DeleteAsync(placement.PositionKey, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            if (deleted)
            {
                results.Add(new NodePlacementResult(
                    placement.PlacementId, placement.PositionKey, CarrierWriteStatus.Written, null));
                continue;
            }

            results.Add(new NodePlacementResult(
                placement.PlacementId, placement.PositionKey, CarrierWriteStatus.Rejected,
                NodeDocumentReasons.DeleteKeyMissing));
            diagnostics.Add(new NodeDiagnostic(
                "delete.key-missing",
                NodeDiagnosticSeverity.Warning,
                NodeDocumentReasons.DeleteKeyMissing,
                placement.PlacementId));
        }

        Report(diagnostics);
        return new NodeMaterializationResult(
            Aggregate(results), resolved.Scope.KeyContext, results, diagnostics);
    }

    private NodeMaterializationResult Rejected(NodeDocumentScope scope, IReadOnlyList<string> reasons)
    {
        var diagnostics = reasons
            .Select(reason => new NodeDiagnostic(
                "document.rejected", NodeDiagnosticSeverity.Error, reason, scope.Domain))
            .ToArray();
        Report(diagnostics);
        return new NodeMaterializationResult(
            MaterializeStatus.Rejected,
            scope.KeyContext,
            Array.Empty<NodePlacementResult>(),
            diagnostics);
    }

    private void Report(IReadOnlyList<NodeDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            _framework.Diagnostics.Report(diagnostic);
        }
    }

    private static MaterializeStatus Aggregate(IReadOnlyList<NodePlacementResult> results)
    {
        if (results.Any(result => result.Status == CarrierWriteStatus.Conflict))
        {
            return MaterializeStatus.Conflict;
        }

        if (results.Any(result => result.Status == CarrierWriteStatus.Rejected))
        {
            return MaterializeStatus.Rejected;
        }

        if (results.Any(result => result.Status is null))
        {
            return MaterializeStatus.Unbound;
        }

        return results.Any(result => result.Status == CarrierWriteStatus.Written)
            ? MaterializeStatus.Written
            : MaterializeStatus.Unchanged;
    }
}
