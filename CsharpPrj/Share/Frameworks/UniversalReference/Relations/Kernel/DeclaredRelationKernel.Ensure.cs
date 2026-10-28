using UniversalWorkflowFramework.Core.Models;

namespace Operation.DataStoreDriven.Relations;

public sealed partial class DeclaredRelationKernel
{
    public async Task<RelationResult> EnsureAsync(
        string operation, RelationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (TargetConflict(request) is { } conflict) return Fail(operation, null, conflict);

        if (!TryResolve(operation, out var plan, out var failure)) return failure!;
        if (plan.IsRemove)
            return Fail(operation, plan.Edge, $"operation '{operation}' 声明 intent=remove；EnsureAsync 不接受");

        var targets = request.Targets;
        if (targets.Count == 0)
            return Fail(operation, plan.Edge, "未提供 target_key(s)");

        var location = ResolveLocation(plan, request);
        var role = RoleOf(request.Origin);
        var source = RelationEndpoints.Create(plan.Source, request.SourceKey, location);
        var diagnostics = new List<string>();
        var written = 0;
        var revived = 0;
        var reviveReasons = new List<string>();

        foreach (var targetKey in targets)
        {
            var target = RelationEndpoints.Create(plan.Target, targetKey, location);

            if (await ProbeAsync(plan, request.SourceKey, targetKey, diagnostics).ConfigureAwait(false)
                is { } missingReason)
                return Fail(operation, plan.Edge, missingReason);

            if (await CheckContainerAsync(plan, request.SourceKey, targetKey, diagnostics, ct).ConfigureAwait(false)
                is { } containerReason)
                return Fail(operation, plan.Edge, containerReason);

            var violation = await FirstRejectingViolationAsync(plan, source, target, diagnostics, ct).ConfigureAwait(false);
            if (violation is not null)
                return Fail(operation, plan.Edge, violation);

            if (string.Equals(plan.Duplicate, "reject", StringComparison.Ordinal)
                && await EdgeExistsAsync(source, target, plan.Edge, ct).ConfigureAwait(false))
            {
                var reason = $"重复边（duplicate=reject）：{RelationEndpoints.KeyOf(source)} → {RelationEndpoints.KeyOf(target)} @ {plan.Edge}";
                _diagnostics?.RelationDuplicate(operation, plan.Edge, reason);
                return new RelationResult(false, operation, plan.Edge, written,
                    new[] { reason }, RelationOutcome.Duplicate);
            }

            var writeOutcome = await _store
                .AddReferenceResultAsync(source, target, plan.Edge, role, 1.0, ct).ConfigureAwait(false);
            if (!writeOutcome.Wrote)
            {
                var skippedReason =
                    $"写边未落库（{writeOutcome.Verdict}）：{RelationEndpoints.KeyOf(source)} → {RelationEndpoints.KeyOf(target)}"
                    + $" @ {plan.Edge}｜{writeOutcome.Reason}";
                diagnostics.Add(skippedReason);
                _diagnostics?.RelationWriteSkipped(
                    operation, plan.Edge, writeOutcome.Verdict.ToString(), role, RelationEndpoints.KeyOf(target));
                return new RelationResult(false, operation, plan.Edge, written, diagnostics, RelationOutcome.Skipped)
                {
                    Revived = revived,
                    ReviveReasons = reviveReasons,
                };
            }

            if (writeOutcome.TombstoneCleared)
            {
                revived++;
                var reviveReason =
                    $"{RelationEndpoints.KeyOf(source)} → {RelationEndpoints.KeyOf(target)} @ {plan.Edge}"
                    + $"｜tombstoneReason={writeOutcome.Reason ?? "(none)"}";
                reviveReasons.Add(reviveReason);
                _diagnostics?.RelationRevived(
                    plan.Edge, RelationEndpoints.KeyOf(source), RelationEndpoints.KeyOf(target), writeOutcome.Reason);
            }

            written++;
        }

        return new RelationResult(true, operation, plan.Edge, written, diagnostics, RelationOutcome.Applied)
        {
            Revived = revived,
            ReviveReasons = reviveReasons,
        };
    }
}
