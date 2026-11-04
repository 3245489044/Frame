namespace UniversalWorkflowFramework.Relations.Kernel;

/// <summary>
/// 批量改挂（merge）：attach 全成 → detach 全成；任一步失败按补偿事务回到执行前逐边状态。
/// </summary>
public sealed partial class DeclaredRelationKernel
{
    /// <inheritdoc />
    public async Task<RelationResult> MergeAsync(RelationMergeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TargetKeys is null || request.TargetKeys.Count == 0)
            return Fail(request.Operation, null, "merge 需要至少一个目标键");
        if (string.Equals(request.FromSourceKey, request.ToSourceKey, StringComparison.Ordinal))
            return Fail(request.Operation, null, "merge 的源与目标父相同（无操作；禁伪成功）");

        if (!TryResolve(request.Operation, out var ensurePlan, out var failure)) return failure!;
        if (ensurePlan.IsRemove())
            return Fail(request.Operation, ensurePlan.Edge, "merge 需要 intent=ensure 的 operation");
        if (ResolveRemovePlan(ensurePlan) is not { } removePlan)
            return Fail(request.Operation, ensurePlan.Edge,
                $"remove-operation-undeclared：edge '{ensurePlan.Edge}' 无 intent=remove 声明");

        var targets = request.TargetKeys
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (targets.Length == 0)
            return Fail(request.Operation, ensurePlan.Edge, "merge 目标键全为空");

        var diagnostics = new List<string>();
        var attached = new List<string>();
        var newlyAttached = new List<string>();
        var preExistingAtTo = new HashSet<string>(StringComparer.Ordinal);
        var preExistingAtFrom = new HashSet<string>(StringComparer.Ordinal);

        // 记录执行前逐边状态：补偿只能撤销「本次新增」/恢复「本次删除」，不得误删原本就存在的边。
        var locationRequest = new RelationRequest(request.ToSourceKey, Namespace: request.Namespace);
        var toLocation = ResolveLocation(ensurePlan, locationRequest);
        var fromLocation = ResolveLocation(removePlan, locationRequest);
        var toSource = RelationEndpoints.Create(ensurePlan.Source, request.ToSourceKey, toLocation);
        var fromSource = RelationEndpoints.Create(removePlan.Source, request.FromSourceKey, fromLocation);

        foreach (var target in targets)
        {
            if (await EdgeExistsAsync(
                    toSource, RelationEndpoints.Create(ensurePlan.Target, target, toLocation),
                    ensurePlan.Edge, ct).ConfigureAwait(false))
                preExistingAtTo.Add(target);

            if (await EdgeExistsAsync(
                    fromSource, RelationEndpoints.Create(removePlan.Target, target, fromLocation),
                    removePlan.Edge, ct).ConfigureAwait(false))
                preExistingAtFrom.Add(target);
        }

        foreach (var target in targets)
        {
            var attach = await EnsureAsync(
                request.Operation,
                Request(request.ToSourceKey, target, request.Namespace, request.Actor, request.Origin),
                ct).ConfigureAwait(false);
            diagnostics.AddRange(attach.Diagnostics);

            if (!attach.Ok)
            {
                diagnostics.AddRange(await CompensateAttachAsync(removePlan.Id, request, newlyAttached, ct)
                    .ConfigureAwait(false));
                diagnostics.Add($"compensate:merge｜attach-failed@{target}｜{attach.Outcome}");
                return new RelationResult(false, request.Operation, ensurePlan.Edge, 0, diagnostics,
                    RelationOutcome.Skipped);
            }

            attached.Add(target);
            if (!preExistingAtTo.Contains(target))
                newlyAttached.Add(target);
        }

        var detached = new List<string>();
        foreach (var target in attached)
        {
            var detach = await RemoveAsync(
                removePlan.Id,
                Request(request.FromSourceKey, target, request.Namespace, request.Actor, request.Origin),
                ct).ConfigureAwait(false);
            diagnostics.AddRange(detach.Diagnostics);

            if (!detach.Ok)
            {
                diagnostics.AddRange(await CompensateMergeAsync(
                    ensurePlan.Id, removePlan.Id, request, newlyAttached,
                    detached.Where(preExistingAtFrom.Contains), ct).ConfigureAwait(false));
                diagnostics.Add($"compensate:merge｜detach-failed@{target}｜{detach.Outcome}");
                return new RelationResult(false, request.Operation, ensurePlan.Edge, 0, diagnostics,
                    RelationOutcome.Skipped);
            }

            detached.Add(target);
        }

        return new RelationResult(true, request.Operation, ensurePlan.Edge, detached.Count, diagnostics,
            RelationOutcome.Applied);
    }

    private async Task<IReadOnlyList<string>> CompensateAttachAsync(
        string removeOperation, RelationMergeRequest request, IEnumerable<string> attached, CancellationToken ct)
    {
        var diagnostics = new List<string>();
        foreach (var target in attached)
        {
            var rollback = await RemoveAsync(
                removeOperation,
                Request(request.ToSourceKey, target, request.Namespace, request.Actor, request.Origin),
                ct).ConfigureAwait(false);
            diagnostics.AddRange(rollback.Diagnostics);
            if (!rollback.Ok) diagnostics.Add($"compensate:merge｜rollback-attach-failed@{target}");
        }

        return diagnostics;
    }

    private async Task<IReadOnlyList<string>> CompensateMergeAsync(
        string ensureOperation,
        string removeOperation,
        RelationMergeRequest request,
        IEnumerable<string> attached,
        IEnumerable<string> detached,
        CancellationToken ct)
    {
        var diagnostics = new List<string>();

        foreach (var target in detached)
        {
            var restore = await EnsureAsync(
                ensureOperation,
                Request(request.FromSourceKey, target, request.Namespace, request.Actor, request.Origin),
                ct).ConfigureAwait(false);
            diagnostics.AddRange(restore.Diagnostics);
            if (!restore.Ok) diagnostics.Add($"compensate:merge｜restore-detach-failed@{target}");
        }

        foreach (var target in attached)
        {
            var rollback = await RemoveAsync(
                removeOperation,
                Request(request.ToSourceKey, target, request.Namespace, request.Actor, request.Origin),
                ct).ConfigureAwait(false);
            diagnostics.AddRange(rollback.Diagnostics);
            if (!rollback.Ok) diagnostics.Add($"compensate:merge｜rollback-attach-failed@{target}");
        }

        return diagnostics;
    }
}
