using Operation.DataStoreDriven.Graph;
using UniversalWorkflowFramework.Core.Models;

namespace Operation.DataStoreDriven.Relations;

/// <summary>
/// 声明驱动的关系内核：<b>唯一写入口</b>（<see cref="IRelationCommandPort"/>）。
/// <para>
/// 算法（对齐方案 §5.2）：plan 解析 → 边声明/废弃检查 → 端点构造 → 存在性探针 → 不变量 →
/// duplicate 处置 → <c>ReferenceGraphCoordinator.AddReferenceAsync</c>（唯一图写）。
/// </para>
/// <para>失败一律返回结构化诊断（禁静默 no-op）；未装配计划 ⇒ <see cref="RelationOutcome.NotAssembled"/>。</para>
/// </summary>
public sealed class DeclaredRelationKernel : IRelationCommandPort
{
    private readonly ReferenceGraphCoordinator _graph;
    private readonly RelationPlanCatalog _catalog;
    private readonly IRelationPolicy _policy;
    private readonly IWorkflowNamespace _namespaces;
    private readonly IEntityExistenceProbe _probe;
    private readonly bool _probeAssembled;
    private readonly RelationInvariantChecker _invariants;
    private readonly IRelationDiagnostics? _diagnostics;

    public DeclaredRelationKernel(
        ReferenceGraphCoordinator graph,
        RelationPlanCatalog catalog,
        IRelationPolicy policy,
        IWorkflowNamespace namespaces,
        IEntityExistenceProbe? probe = null,
        IRelationDiagnostics? diagnostics = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _probeAssembled = probe is not null;
        _probe = probe ?? UnassembledEntityExistenceProbe.Instance;
        _diagnostics = diagnostics;
        _invariants = new RelationInvariantChecker(_graph);
    }

    /// <inheritdoc />
    public RelationOperation? Describe(string operation) => _catalog.Resolve(operation);

    /// <inheritdoc />
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

        foreach (var targetKey in targets)
        {
            var target = RelationEndpoints.Create(plan.Target, targetKey, location);

            if (!await ProbeAsync(plan, request.SourceKey, targetKey, diagnostics).ConfigureAwait(false))
                return Fail(operation, plan.Edge,
                    $"端点缺失：{plan.Source}:{request.SourceKey} → {plan.Target}:{targetKey}（on_missing=reject）");

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

            await _graph.AddReferenceAsync(source, target, plan.Edge, role, 1.0, ct).ConfigureAwait(false);
            written++;
        }

        return new RelationResult(true, operation, plan.Edge, written, diagnostics, RelationOutcome.Applied);
    }

    /// <inheritdoc />
    public async Task<RelationResult> RemoveAsync(
        string operation, RelationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (TargetConflict(request) is { } conflict) return Fail(operation, null, conflict);

        if (!TryResolve(operation, out var plan, out var failure)) return failure!;
        if (!plan.IsRemove)
            return Fail(operation, plan.Edge, $"operation '{operation}' 声明 intent=ensure；RemoveAsync 不接受");

        var targets = request.Targets;
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
            var outcome = await _graph.RemoveReferenceResultAsync(
                source, target, plan.Edge, role, ct).ConfigureAwait(false);
            if (outcome.Status == GraphDeleteStatus.Ok)
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

    // ── 内部：解析 / 作用域 / 探针 / 不变量 ────────────────────────────

    private bool TryResolve(string operation, out RelationOperation plan, out RelationResult? failure)
    {
        plan = null!;
        failure = null;

        if (_catalog.IsEmpty)
        {
            _diagnostics?.RelationPlanMissing("relation plan 为空：声明面缺失或未装配");
            failure = new RelationResult(false, operation, null, 0,
                new[] { "关系计划未装配（Config/relations 声明缺失、为空或编译失败）" },
                RelationOutcome.NotAssembled);
            return false;
        }

        var resolved = _catalog.Resolve(operation);
        if (resolved is null)
        {
            failure = Fail(operation, null, $"operation '{operation}' 未在 [Relation] 声明");
            return false;
        }

        if (!_policy.IsDeclared(resolved.Edge))
        {
            failure = Fail(operation, resolved.Edge, $"edge '{resolved.Edge}' 未在 [Edge] 声明");
            return false;
        }

        if (_policy.IsDeprecated(resolved.Edge))
        {
            failure = Fail(operation, resolved.Edge, $"edge '{resolved.Edge}' 已废弃（dep）");
            return false;
        }

        plan = resolved;
        return true;
    }

    private string? ResolveLocation(RelationOperation plan, RelationRequest request)
    {
        if (string.Equals(plan.Namespace, "none", StringComparison.Ordinal)) return null;
        if (string.Equals(plan.Namespace, "workflow_id", StringComparison.Ordinal))
            return _namespaces.Resolve(request.Namespace, request.SourceKey);
        return string.IsNullOrWhiteSpace(plan.Namespace) ? null : plan.Namespace;
    }

    private static string? TargetConflict(RelationRequest request)
        => request.TargetKey is { Length: > 0 } && request.TargetKeys is { Count: > 0 }
            ? "target_key 与 target_keys 不可同时提供（目标静默丢弃被禁止）"
            : null;

    /// <summary>写来源 → 图写角色（单点映射；禁内核各处硬编码 Business）。</summary>
    private static GraphMountRole RoleOf(RelationOrigin origin) => origin switch
    {
        RelationOrigin.Rebuild => GraphMountRole.Reconcile,
        RelationOrigin.Admin => GraphMountRole.Declare,
        _ => GraphMountRole.Business,
    };

    private async Task<bool> ProbeAsync(
        RelationOperation plan, string sourceKey, string targetKey, List<string> diagnostics)
    {
        var probeSource = plan.Probes("src");
        var probeTarget = plan.Probes("tgt");
        if (!probeSource && !probeTarget) return true;

        if (!_probeAssembled)
        {
            const string reason = "probe-unassembled：存在性探针未装配，写前门 fail-closed（禁恒真放行）";
            diagnostics.Add(reason);
            _diagnostics?.RelationMissingEndpoint(plan.Id, plan.Edge, reason);
            return false;
        }

        var missing = new List<string>();

        if (probeSource && !await _probe.ExistsAsync(plan.Source, sourceKey).ConfigureAwait(false))
            missing.Add($"{plan.Source}:{sourceKey}");
        if (probeTarget && !await _probe.ExistsAsync(plan.Target, targetKey).ConfigureAwait(false))
            missing.Add($"{plan.Target}:{targetKey}");

        if (missing.Count == 0) return true;

        _diagnostics?.RelationMissingEndpoint(plan.Id, plan.Edge, string.Join(",", missing));

        // on_missing=report ⇒ 仅可见化并继续；默认 reject。
        if (string.Equals(plan.OnMissing, "report", StringComparison.Ordinal))
        {
            diagnostics.Add($"端点不存在：{string.Join(",", missing)}");
            return true;
        }

        return false;
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
                // 预算超限≠成环：只可见化，不拒绝（E9）；其余按声明 on_violation 裁决。
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
        var outgoing = await _graph.GetReferencesAsync(source, includeIncoming: false, ct);
        return outgoing.Any(r => !r.IsIncoming
                                 && string.Equals(r.RefType, edge, StringComparison.Ordinal)
                                 && RelationEndpoints.SameEntity(r.Target, target));
    }

    private RelationResult Fail(string operation, string? edge, string reason)
    {
        _diagnostics?.RelationRejected(operation, edge, reason);
        return RelationResult.Rejected(operation, edge, reason);
    }
}
