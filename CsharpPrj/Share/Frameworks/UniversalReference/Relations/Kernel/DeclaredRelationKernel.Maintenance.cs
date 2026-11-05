namespace UniversalWorkflowFramework.Relations.Kernel;

/// <summary>
/// 维护操作实现段（F3）：move / gc（merge 见同名分部文件）。
/// <para>
/// 全部复用 <see cref="EnsureAsync"/> / <see cref="RemoveAsync"/>（同一写原语）；失败按补偿事务回到执行前状态，
/// 补偿动作逐条进 Diagnostics（禁静默回滚）。
/// </para>
/// </summary>
public sealed partial class DeclaredRelationKernel : IRelationMaintenancePort
{
    /// <inheritdoc />
    public async Task<RelationResult> MoveAsync(RelationMoveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.FromSourceKey) || string.IsNullOrWhiteSpace(request.ToSourceKey))
            return Fail(request.Operation, null, "move 需要 from/to 两个源键");
        if (string.Equals(request.FromSourceKey, request.ToSourceKey, StringComparison.Ordinal))
            return Fail(request.Operation, null, "move 的源与目标父相同（无操作；禁伪成功）");

        if (!TryResolve(request.Operation, out var ensurePlan, out var failure)) return failure!;
        if (ensurePlan.IsRemove())
            return Fail(request.Operation, ensurePlan.Edge, "move 需要 intent=ensure 的 operation");
        if (ResolveRemovePlan(ensurePlan) is not { } removePlan)
            return Fail(request.Operation, ensurePlan.Edge,
                $"remove-operation-undeclared：edge '{ensurePlan.Edge}' 无 intent=remove 声明");

        var attach = await EnsureAsync(
            request.Operation,
            Request(request.ToSourceKey, request.TargetKey, request.Namespace, request.Actor, request.Origin),
            ct).ConfigureAwait(false);
        if (!attach.Ok) return attach;

        var detach = await RemoveAsync(
            removePlan.Id,
            Request(request.FromSourceKey, request.TargetKey, request.Namespace, request.Actor, request.Origin),
            ct).ConfigureAwait(false);
        if (detach.Ok)
        {
            return new RelationResult(true, request.Operation, ensurePlan.Edge, 1,
                Combine(detach.Diagnostics, attach.Diagnostics), RelationOutcome.Applied)
            {
                Revived = attach.Revived,
                ReviveReasons = attach.ReviveReasons,
            };
        }

        var rollback = await RemoveAsync(
            removePlan.Id,
            Request(request.ToSourceKey, request.TargetKey, request.Namespace, request.Actor, request.Origin),
            ct).ConfigureAwait(false);

        var diagnostics = new List<string>();
        diagnostics.AddRange(detach.Diagnostics);
        diagnostics.AddRange(attach.Diagnostics);
        diagnostics.AddRange(rollback.Diagnostics);
        diagnostics.Add($"compensate:move｜detach={detach.Outcome}｜rollback={rollback.Outcome}");

        return new RelationResult(false, request.Operation, ensurePlan.Edge, 0, diagnostics, RelationOutcome.Skipped);
    }

    /// <inheritdoc />
    public async Task<RelationResult> GcAsync(RelationGcRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Origin == RelationOrigin.Runtime)
            return Fail(request.Operation, null, "gc 仅允许 rebuild/admin 角色（业务角色禁止）");
        if (request.TargetKeys is null || request.TargetKeys.Count == 0)
            return Fail(request.Operation, null, "gc 需要显式候选目标集（禁全库扫描）");

        if (!TryResolve(request.Operation, out var ensurePlan, out var failure)) return failure!;
        if (ResolveRemovePlan(ensurePlan) is not { } removePlan)
            return Fail(request.Operation, ensurePlan.Edge,
                $"remove-operation-undeclared：edge '{ensurePlan.Edge}' 无 intent=remove 声明");

        if (!_probeAssembled)
            return Fail(request.Operation, ensurePlan.Edge,
                "probe-unassembled：gc 需要存在性探针（禁按名字猜孤儿）");

        var diagnostics = new List<string>();
        var removed = 0;
        var failed = 0;

        foreach (var raw in request.TargetKeys)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                diagnostics.Add("gc:blank-target（空白目标已忽略，未计入孤儿）");
                continue;
            }

            var target = raw.Trim();

            if (!_probe.IsMapped(ensurePlan.Target))
            {
                diagnostics.Add($"probe-unmapped：{ensurePlan.Target}:{target}（已跳过，禁判孤儿）");
                continue;
            }

            if (await _probe.ExistsAsync(ensurePlan.Target, target, ct).ConfigureAwait(false))
                continue;

            var outcome = await RemoveAsync(
                removePlan.Id,
                Request(request.SourceKey, target, request.Namespace, request.Actor, request.Origin),
                ct).ConfigureAwait(false);
            diagnostics.AddRange(outcome.Diagnostics);
            if (outcome.Ok) removed++;
            else failed++;
        }

        if (removed > 0)
        {
            return new RelationResult(true, request.Operation, ensurePlan.Edge, removed, diagnostics,
                RelationOutcome.Applied);
        }

        // 删除失败不得伪装成「无孤儿」：失败与真正的空集必须可辨。
        if (failed > 0)
        {
            diagnostics.Add($"gc:delete-failed（{failed} 个候选删除失败；不报 no-orphan）");
            return new RelationResult(false, request.Operation, ensurePlan.Edge, 0, diagnostics,
                RelationOutcome.Skipped);
        }

        var empty = new List<string>(diagnostics) { "gc:no-orphan" };
        return new RelationResult(false, request.Operation, ensurePlan.Edge, 0, empty, RelationOutcome.NotFound);
    }

    /// <inheritdoc />
    public async Task<RelationResult> RepairAsync(RelationRepairRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_stampRepairAssembled || _stampRepair is null)
            return Fail(request.Operation, request.Edge,
                "repair-unassembled：盖章回填端口未装配（禁以声明存在冒充可执行）");

        if (string.IsNullOrWhiteSpace(request.Operation))
            return Fail(request.Operation, request.Edge, "repair 需要显式 operation（intent=repair）");

        // 与写路径同源解析（含 [Edge] 声明/废弃检查），禁绕过策略直读目录。
        if (!TryResolve(request.Operation, out var plan, out var failure)) return failure!;
        if (!plan.IsRepair())
            return Fail(request.Operation, plan.Edge,
                $"operation '{request.Operation}' 的 intent={plan.Intent}（repair 需要 intent=repair）");

        if (string.IsNullOrWhiteSpace(request.FallbackFlow))
            return Fail(request.Operation, plan.Edge, "repair 需要 fallbackFlow（禁按时间猜来源）");

        var report = await _stampRepair.RepairEdgeStampsAsync(request, ct).ConfigureAwait(false);
        var diagnostics = new List<string>
        {
            $"repair:total={report.Total}",
            $"repair:missing={report.Missing}",
            $"repair:patched={report.Patched}",
            $"repair:skipped={report.Skipped}",
            $"repair:applied={report.Applied}",
        };

        var settled = report.Missing == 0 || (request.Apply && report.Patched >= report.Missing);
        return new RelationResult(
            settled,
            request.Operation,
            plan.Edge,
            report.Patched,
            diagnostics,
            settled ? RelationOutcome.Applied : RelationOutcome.Skipped);
    }

    private RelationOperation? ResolveRemovePlan(RelationOperation ensurePlan)
        => _catalog.ResolveByEdge(ensurePlan.Edge, remove: true);

    private static RelationRequest Request(
        string sourceKey, string targetKey, string? ns, string actor, RelationOrigin origin)
        => new(sourceKey, TargetKey: targetKey, Namespace: ns, Actor: actor, Origin: origin);

    private static IReadOnlyList<string> Combine(params IReadOnlyList<string>[] parts)
    {
        var result = new List<string>();
        foreach (var part in parts)
        {
            if (part is { Count: > 0 }) result.AddRange(part);
        }

        return result;
    }
}
