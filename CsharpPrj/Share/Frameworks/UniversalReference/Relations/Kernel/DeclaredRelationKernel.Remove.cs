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
            // 先确认边确实存在：部分存储实现在未命中时仍返回 Ok，直接以 Ok 计数会虚报删除条数。
            var existed = await EdgeExistsAsync(source, target, plan.Edge, ct).ConfigureAwait(false);
            var outcome = await _store.RemoveReferenceResultAsync(
                source, target, plan.Edge, role, ct).ConfigureAwait(false);
            if (outcome.Status == ReferenceDeleteStatus.Ok && existed)
            {
                removed++;
                continue;
            }

            diagnostics.Add($"未命中：{RelationEndpoints.KeyOf(source)} → {RelationEndpoints.KeyOf(target)} @ {plan.Edge}"
                            + $"（{outcome.Status}：{outcome.Message}{(existed ? string.Empty : "；删除前该边不存在")}）");
        }

        var total = targets.Count;
        if (removed == total)
            return new RelationResult(true, operation, plan.Edge, removed, diagnostics, RelationOutcome.Applied);

        if (removed > 0)
        {
            // 部分删除不得报 Applied：按实际删除条数返回，并显式标注未全量完成。
            diagnostics.Add($"部分成功：已删除 {removed}/{total} 条边；其余目标未删除（不报 Applied）");
            return new RelationResult(false, operation, plan.Edge, removed, diagnostics, RelationOutcome.Skipped);
        }

        return new RelationResult(false, operation, plan.Edge, 0, diagnostics, RelationOutcome.NotFound);
    }
}
