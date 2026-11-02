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
        // 结果构造单源（D3）：诊断 code/severity 只由 NodeDocumentResultFactory 决定。
        var result = NodeDocumentResultFactory.Rejected(scope, reasons);
        Report(result.Diagnostics);
        return result;
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
