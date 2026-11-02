using Modules.Node.Diagnostics;
using Modules.Node.Materialization;

namespace Share.NodeIo;

public sealed partial class NodeDocumentPort
{
    /// <inheritdoc />
    public async Task<NodeMaterializationResult> ApplyAsync(
        NodeDocumentChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var resolved = _resolver.Resolve(change.Scope, forWrite: true);
        if (!resolved.Ok)
        {
            return Rejected(change.Scope, resolved.Reasons);
        }

        return change.Mode switch
        {
            NodeChangeMode.Delete => await DeleteAsync(resolved, cancellationToken).ConfigureAwait(false),
            NodeChangeMode.Patch => await PatchAsync(change, resolved, cancellationToken).ConfigureAwait(false),
            _ => await UpsertAsync(change, resolved, cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<NodeMaterializationResult> UpsertAsync(
        NodeDocumentChange change,
        ResolvedScope resolved,
        CancellationToken cancellationToken)
    {
        var json = change.PayloadJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return Rejected(change.Scope, new[] { NodeDocumentReasons.PayloadMissing });
        }

        var diagnostics = Validate(resolved, json, change.Scope.KeyContext);
        if (diagnostics.Count > 0)
        {
            Report(diagnostics);
            return new NodeMaterializationResult(
                MaterializeStatus.Rejected,
                change.Scope.KeyContext,
                Array.Empty<NodePlacementResult>(),
                diagnostics);
        }

        var placementIds = (change.PlacementIds is { Count: > 0 }
            ? resolved.PlacementIds.Where(id => change.PlacementIds.Contains(id))
            : resolved.PlacementIds).ToArray();
        if (placementIds.Length == 0)
        {
            return Rejected(change.Scope, new[] { NodeDocumentReasons.NoWritablePlacement + ":" + change.Scope.Domain });
        }

        return await _framework.Materializer.MaterializeAsync(
                new NodeMaterializeRequest(
                    change.Scope.KeyContext, json!, change.Scope.Domain, placementIds, Meta: change.RecordMeta),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private List<NodeDiagnostic> Validate(ResolvedScope resolved, string json, string keyContext)
    {
        var diagnostics = new List<NodeDiagnostic>();
        foreach (var placement in resolved.Placements)
        {
            var validation = NodePayloadValidator.Validate(
                _schemas.Resolve(placement.PayloadSchemaId), placement.Form, json, keyContext);
            if (validation.Ok)
            {
                continue;
            }

            diagnostics.Add(new NodeDiagnostic(
                "payload.rejected",
                NodeDiagnosticSeverity.Error,
                validation.Reason ?? NodeDocumentReasons.PayloadMissing,
                placement.PlacementId));
        }

        return diagnostics;
    }
}
