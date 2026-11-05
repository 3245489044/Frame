using Modules.Node.Diagnostics;
using Modules.Node.Materialization;
using Modules.Node.Placement;

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

        // 写面收窄单点（E4）：Upsert/Patch/Delete 三模式共用同一收窄结果，禁各分支各自过滤。
        resolved = NarrowPlacements(resolved, change.PlacementIds);
        if (!resolved.Ok)
        {
            return Rejected(change.Scope, resolved.Reasons);
        }

        return change.Mode switch
        {
            NodeChangeMode.Delete => await DeleteAsync(change, resolved, cancellationToken).ConfigureAwait(false),
            NodeChangeMode.Patch => await PatchAsync(change, resolved, cancellationToken).ConfigureAwait(false),
            _ => await UpsertAsync(change, resolved, cancellationToken).ConfigureAwait(false),
        };
    }

    private static ResolvedScope NarrowPlacements(ResolvedScope resolved, IReadOnlyList<string>? placementIds)
    {
        if (placementIds is not { Count: > 0 })
        {
            return resolved;
        }

        var narrowed = resolved.Placements
            .Where(placement => placementIds.Contains(placement.PlacementId))
            .ToArray();
        if (narrowed.Length == 0)
        {
            return ResolvedScope.Failed(
                resolved.Scope,
                NodeDocumentReasons.NoWritablePlacement + ":" + resolved.Scope.Domain);
        }

        return new ResolvedScope(
            resolved.Scope,
            narrowed,
            true,
            resolved.Reasons,
            narrowed[0].Form);
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

        return await _framework.Materializer.MaterializeAsync(
                new NodeMaterializeRequest(
                    change.Scope.KeyContext, json!, change.Scope.Domain, resolved.PlacementIds,
                    Meta: change.RecordMeta),
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
