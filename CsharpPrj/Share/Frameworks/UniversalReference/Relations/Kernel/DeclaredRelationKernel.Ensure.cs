using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.Relations.Kernel;

public sealed partial class DeclaredRelationKernel
{
    public async Task<RelationResult> EnsureAsync(
        string operation, RelationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (TargetConflict(request) is { } conflict) return Fail(operation, null, conflict);

        if (!TryResolve(operation, out var plan, out var failure)) return failure!;
        if (plan.IsRemove())
            return Fail(operation, plan.Edge, $"operation '{operation}' 声明 intent=remove；EnsureAsync 不接受");

        var targets = request.Targets();
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
                return PartialFailure(operation, plan.Edge, missingReason, written, targets.Count, diagnostics);

            if (await CheckContainerAsync(plan, request.SourceKey, targetKey, diagnostics, ct).ConfigureAwait(false)
                is { } containerReason)
                return PartialFailure(operation, plan.Edge, containerReason, written, targets.Count, diagnostics);

            if (await EvaluateGuardPreAsync(plan, source, target, ct).ConfigureAwait(false) is { } guardReason)
                return PartialFailure(operation, plan.Edge, guardReason, written, targets.Count, diagnostics);

            var violation = await FirstRejectingViolationAsync(plan, source, target, diagnostics, ct).ConfigureAwait(false);
            if (violation is not null)
                return PartialFailure(operation, plan.Edge, violation, written, targets.Count, diagnostics);

            if (string.Equals(plan.Duplicate, "reject", StringComparison.Ordinal)
                && await EdgeExistsAsync(source, target, plan.Edge, ct).ConfigureAwait(false))
            {
                var reason = $"重复边（duplicate=reject）：{RelationEndpoints.KeyOf(source)} → {RelationEndpoints.KeyOf(target)} @ {plan.Edge}";
                _diagnostics?.RelationDuplicate(operation, plan.Edge, reason);
                return PartialFailure(operation, plan.Edge, reason, written, targets.Count, diagnostics,
                    RelationOutcome.Duplicate);
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
                return PartialFailure(operation, plan.Edge, skippedReason, written, targets.Count, diagnostics,
                    RelationOutcome.Skipped, revived, reviveReasons);
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
            diagnostics.AddRange(await EvaluateGuardPostAsync(plan, source, target, ct).ConfigureAwait(false));
        }

        return new RelationResult(true, operation, plan.Edge, written, diagnostics, RelationOutcome.Applied)
        {
            Revived = revived,
            ReviveReasons = reviveReasons,
        };
    }

    /// <summary>
    /// 中止时的显式部分成功：保留已写入条数与全部诊断，并追加「已写 N/M，后续目标未处理」，
    /// 不再以 Written=0 的纯拒绝掩盖已落库的边。
    /// </summary>
    private RelationResult PartialFailure(
        string operation, string? edge, string reason, int written, int total,
        List<string> diagnostics, RelationOutcome outcome = RelationOutcome.Rejected,
        int revived = 0, IReadOnlyList<string>? reviveReasons = null)
    {
        if (written > 0)
            diagnostics.Add($"部分成功：已写入 {written}/{total} 条边后中止；后续目标未处理（{reason}）");
        else
            diagnostics.Add(reason);

        _diagnostics?.RelationRejected(operation, edge, reason);
        return new RelationResult(false, operation, edge, written, diagnostics, outcome)
        {
            Revived = revived,
            ReviveReasons = reviveReasons ?? Array.Empty<string>(),
        };
    }
}
