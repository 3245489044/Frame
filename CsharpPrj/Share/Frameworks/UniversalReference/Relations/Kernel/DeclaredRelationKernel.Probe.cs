using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.Relations;

public sealed partial class DeclaredRelationKernel
{
    private async Task<string?> ProbeAsync(
        RelationOperation plan, string sourceKey, string targetKey, List<string> diagnostics)
    {
        var probeSource = plan.Probes("src");
        var probeTarget = plan.Probes("tgt");
        if (!probeSource && !probeTarget) return null;

        if (!_probeAssembled)
        {
            const string reason = "probe-unassembled：存在性探针未装配，写前门 fail-closed（禁恒真放行）";
            diagnostics.Add(reason);
            _diagnostics?.RelationProbeUnassembled(plan.Id, plan.Edge, reason);
            return reason;
        }

        var unmapped = new List<string>();
        var missing = new List<string>();

        if (probeSource)
            await CheckEndpointAsync(plan.Source, sourceKey, unmapped, missing).ConfigureAwait(false);
        if (probeTarget)
            await CheckEndpointAsync(plan.Target, targetKey, unmapped, missing).ConfigureAwait(false);

        if (unmapped.Count > 0)
        {
            var endpoints = string.Join(",", unmapped);
            var reason = $"probe_unmapped：{endpoints}（实体类型未映射存在性谓词）";
            diagnostics.Add(reason);
            _diagnostics?.RelationProbeUnmapped(plan.Id, plan.Edge, endpoints);
            return reason;
        }

        if (missing.Count == 0) return null;

        _diagnostics?.RelationMissingEndpoint(plan.Id, plan.Edge, string.Join(",", missing));

        if (string.Equals(plan.OnMissing, "report", StringComparison.Ordinal))
        {
            diagnostics.Add($"端点不存在：{string.Join(",", missing)}");
            return null;
        }

        return $"端点缺失：{string.Join(",", missing)}（on_missing=reject）";
    }

    private async Task CheckEndpointAsync(
        string entityType, string id, List<string> unmapped, List<string> missing)
    {
        if (!_probe.IsMapped(entityType))
        {
            unmapped.Add($"{entityType}:{id}");
            return;
        }

        if (!await _probe.ExistsAsync(entityType, id).ConfigureAwait(false))
            missing.Add($"{entityType}:{id}");
    }

    private async Task<string?> FirstRejectingViolationAsync(
        RelationOperation plan,
        ResourceIdentifier source,
        ResourceIdentifier target,
        List<string> diagnostics,
        CancellationToken ct)
    {
        foreach (var invariant in _catalog.InvariantsFor(plan.Edge))
        {
            var violations = await _invariants.CheckAsync(invariant, source, target, _probe, ct);
            foreach (var violation in violations)
            {
                _diagnostics?.RelationInvariantViolation(plan.Id, violation.InvariantId, violation.Reason);
                if (violation.Kind == RelationViolationKind.BudgetExceeded
                    || string.Equals(invariant.OnViolation, "report", StringComparison.Ordinal))
                {
                    diagnostics.Add(violation.Reason);
                    continue;
                }

                return violation.Reason;
            }
        }

        return null;
    }

    private async Task<bool> EdgeExistsAsync(
        ResourceIdentifier source, ResourceIdentifier target, string edge, CancellationToken ct)
    {
        var outgoing = await _store.GetReferencesAsync(source, includeIncoming: false, ct);
        return outgoing.Any(r => !r.IsIncoming
                                 && string.Equals(r.RefType, edge, StringComparison.Ordinal)
                                 && RelationEndpoints.SameEntity(r.Target, target));
    }
}
