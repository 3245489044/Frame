using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.Relations.Kernel;

public sealed partial class DeclaredRelationKernel
{
    public async Task<RelationResult> RemoveAsync(
        string operation, RelationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (TargetConflict(request) is { } conflict) return Fail(operation, null, conflict);

        if (!TryResolve(operation, out var plan, out var failure)) return failure!;
        if (!plan.IsRemove())
            return Fail(operation, plan.Edge, $"operation '{operation}' 声明 intent=ensure；RemoveAsync 不接受");

        var targets = request.Targets();
        if (targets.Count == 0)
            return Fail(operation, plan.Edge, "未提供 target_key(s)");

        var location = ResolveLocation(plan, request);
        var role = RoleOf(request.Origin);
        var source = RelationEndpoints.Create(plan.Source, request.SourceKey, location);
        var diagnostics = new List<string>();
        var removed = 0;

        foreach (var targetKey in targets)
        {
            var target = RelationEndpoints.Create(plan.Target, targetKey, location);
            var outcome = await _store.RemoveReferenceResultAsync(
                source, target, plan.Edge, role, ct).ConfigureAwait(false);
            if (outcome.Status == ReferenceDeleteStatus.Ok)
            {
                removed++;
                continue;
            }

            diagnostics.Add($"未命中：{RelationEndpoints.KeyOf(source)} → {RelationEndpoints.KeyOf(target)} @ {plan.Edge}"
                            + $"（{outcome.Status}：{outcome.Message}）");
        }

        return removed > 0
            ? new RelationResult(true, operation, plan.Edge, removed, diagnostics, RelationOutcome.Applied)
            : new RelationResult(false, operation, plan.Edge, 0, diagnostics, RelationOutcome.NotFound);
    }
}
