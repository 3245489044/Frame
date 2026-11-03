using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.Relations.Mounting;

/// <summary>
/// 挂载门面（UMF · L3）：Resolve → Validate → Normalize → Coalesce → Arbitrate → Preflight →
/// Provision → Write → Receipt（设计单 §7.1）。
/// <para>
/// **不复制内核逻辑**：写前门 / 不变量 / duplicate / 容器归属 / 派生全部由
/// <see cref="IRelationCommandPort"/> / <see cref="IRelationGatherPoint"/> 承担。
/// </para>
/// </summary>
public sealed class MountGateway : IMountPort
{
    private readonly IRelationPorts _ports;
    private readonly MountBindingCatalog _bindings;
    private readonly EntityDomainRegistry _domains;
    private readonly TriggerArbiter _arbiter;
    private readonly IRelationDiagnostics? _diagnostics;

    public MountGateway(
        IRelationPorts ports,
        MountBindingCatalog bindings,
        EntityDomainRegistry domains,
        TriggerArbiter? arbiter = null,
        IRelationDiagnostics? diagnostics = null)
    {
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _domains = domains ?? throw new ArgumentNullException(nameof(domains));
        _arbiter = arbiter ?? new TriggerArbiter();
        _diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public async Task<MountReceipt> ApplyAsync(MountRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.MountId))
            return MountReceipt.Rejected(request, string.Empty, null, "missing-mount-id");

        var binding = _bindings.Resolve(request.MountId);
        if (binding is null)
            return MountReceipt.Rejected(request, string.Empty, null, "undeclared-mount:" + request.MountId);

        var operation = _ports.Operations.Resolve(binding.RelationOperation);
        if (operation is null)
            return MountReceipt.Missing(request, binding.RelationOperation, null,
                "undeclared-relation:" + binding.RelationOperation);

        if (!string.Equals(request.Scope?.ContainerEntity, operation.Source, StringComparison.Ordinal))
        {
            return MountReceipt.Rejected(request, operation.Id, operation.Edge,
                $"scope-entity-mismatch: expected={operation.Source} actual={request.Scope?.ContainerEntity}");
        }

        if (request.Options?.ExpectedFamily is { Length: > 0 } expectedFamily
            && !string.Equals(expectedFamily, binding.Family, StringComparison.Ordinal))
        {
            return MountReceipt.Rejected(request, operation.Id, operation.Edge,
                $"family-mismatch: expected={expectedFamily} actual={binding.Family}");
        }

        if (!binding.Triggers.Contains(request.Trigger, StringComparer.Ordinal))
        {
            return MountReceipt.Rejected(request, operation.Id, operation.Edge,
                $"trigger-not-allowed: {request.Trigger} ∉ [{string.Join(',', binding.Triggers)}]");
        }

        var sourceDomain = _domains.TryGet(operation.Source);
        if (sourceDomain is null)
            return MountReceipt.Missing(request, operation.Id, operation.Edge,
                "domain-unregistered:" + operation.Source);

        var targetDomain = _domains.TryGet(operation.Target);
        if (targetDomain is null)
            return MountReceipt.Missing(request, operation.Id, operation.Edge,
                "domain-unregistered:" + operation.Target);

        if (!sourceDomain.Keys.TryNormalize(request.Scope.ContainerKey, request.Scope.Namespace,
                out var sourceKey, out var sourceReason))
        {
            return MountReceipt.Rejected(request, operation.Id, operation.Edge,
                "invalid-container-key:" + sourceReason);
        }

        // 3/4：逐目标归一 + 批内去重（保序）；非法目标逐条可见化，不静默丢。
        var keys = new List<string>();
        var results = new List<MountTargetResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in request.Targets ?? Array.Empty<MountTarget>())
        {
            if (target is null) continue;

            if (!string.Equals(target.Entity, operation.Target, StringComparison.Ordinal))
            {
                results.Add(new MountTargetResult(target.Entity, target.Key, MountTargetStatus.Rejected,
                    operation.Edge, $"target-entity-mismatch: expected={operation.Target}"));
                continue;
            }

            if (!targetDomain.Keys.TryNormalize(target.Key, request.Scope.Namespace, out var key, out var reason))
            {
                results.Add(new MountTargetResult(target.Entity, target.Key, MountTargetStatus.Rejected,
                    operation.Edge, "invalid-target-key:" + reason));
                continue;
            }

            if (!seen.Add(key)) continue; // 批内去重（coalesce=edge 的缺省口径）
            keys.Add(key);
        }

        if (keys.Count == 0)
        {
            if (results.Any(r => r.Status == MountTargetStatus.Rejected))
                return MountReceipt.Rejected(request, operation.Id, operation.Edge, "no-valid-targets");

            return MountReceipt.NotApplicable(request, operation.Id, operation.Edge, "no-targets");
        }

        // 5：触发仲裁（删除方向是显式意图，不参与抑制）。
        if (request.Intent != MountIntent.Remove
            && !_arbiter.TryAcquire(request.MountId, request.Scope, request.Trigger, out var winner))
        {
            var suppressed = keys
                .Select(k => new MountTargetResult(operation.Target, k, MountTargetStatus.Suppressed,
                    operation.Edge, "trigger-suppressed:" + winner))
                .ToList();
            return MountReceipt.NotApplicable(request, operation.Id, operation.Edge,
                "trigger-suppressed:" + winner, suppressed);
        }

        // 删除方向：同一 edge 的唯一 remove operation 由声明面反查（禁命名推断）。
        var writeOperation = operation;
        if (request.Intent == MountIntent.Remove)
        {
            writeOperation = _ports.Operations.ResolveByEdge(operation.Edge, remove: true);
            if (writeOperation is null)
                return MountReceipt.Missing(request, operation.Id, operation.Edge,
                    "undeclared-remove-operation:" + operation.Edge);
        }

        var intent = request.Intent == MountIntent.Remove ? RelationIntent.Remove : RelationIntent.Ensure;
        var relationRequest = new RelationRequest(
            sourceKey,
            TargetKeys: keys,
            Namespace: request.Scope.Namespace,
            Actor: request.Actor,
            Origin: MapOrigin(request.Origin));

        RelationResult result;
        var gather = _ports.Gather;
        try
        {
            if (gather is not null)
            {
                result = await gather
                    .InvokeByOperationAsync(writeOperation.Id, intent, relationRequest, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                // 未装配聚拢点：端点 provision 由域镜像补齐（与 Gather 的 provision 语义一致）。
                if (intent == RelationIntent.Ensure)
                {
                    await MirrorAsync(sourceDomain, sourceKey, ct).ConfigureAwait(false);
                    foreach (var key in keys)
                        await MirrorAsync(targetDomain, key, ct).ConfigureAwait(false);
                }

                result = intent == RelationIntent.Remove
                    ? await _ports.Commands.RemoveAsync(writeOperation.Id, relationRequest, ct).ConfigureAwait(false)
                    : await _ports.Commands.EnsureAsync(writeOperation.Id, relationRequest, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _diagnostics?.RelationRejected(writeOperation.Id, writeOperation.Edge, ex.Message);
            return MountReceipt.Failed(request, writeOperation.Id, writeOperation.Edge, ex.Message);
        }

        var (overall, status, ok) = Map(result);
        var edge = result.Edge ?? writeOperation.Edge;
        var reasonText = result.Diagnostics.Count > 0 ? string.Join("; ", result.Diagnostics) : null;

        foreach (var key in keys)
            results.Add(new MountTargetResult(operation.Target, key, status, edge, reasonText));

        return new MountReceipt(
            request.MountId,
            writeOperation.Id,
            edge,
            request.Origin,
            request.Trigger,
            request.CorrelationId ?? request.Scope.CorrelationId,
            results,
            result.Written,
            result.Revived,
            results.Count(r => r.Status is MountTargetStatus.Skipped or MountTargetStatus.Rejected
                or MountTargetStatus.NotFound or MountTargetStatus.Unavailable),
            ok,
            reasonText,
            overall);
    }

    private static async Task MirrorAsync(IEntityDomain domain, string key, CancellationToken ct)
    {
        if (domain.Mirror is null) return;
        await domain.Mirror(key, ct).ConfigureAwait(false);
    }

    private (MountOutcome Overall, MountTargetStatus Status, bool Ok) Map(RelationResult result)
        => RelationOutcomeSemantics.Of(result.Outcome) switch
        {
            OutcomeClass.Ok => (MountOutcome.Committed, MountTargetStatus.Applied, true),
            OutcomeClass.Duplicate => (MountOutcome.Committed, MountTargetStatus.Duplicate, true),
            OutcomeClass.NotApplicable => (MountOutcome.PartiallyApplied, MountTargetStatus.Skipped, false),
            OutcomeClass.Rejected => (MountOutcome.Rejected, MountTargetStatus.Rejected, false),
            OutcomeClass.Missing => (MountOutcome.PartiallyApplied, MountTargetStatus.NotFound, false),
            OutcomeClass.Unavailable => (MountOutcome.Missing, MountTargetStatus.Unavailable, false),
            _ => (MountOutcome.Failed, MountTargetStatus.Rejected, false),
        };

    /// <summary>来源映射（命令 = Admin，显式优先；运行时 = Runtime；重建 = Rebuild）。</summary>
    private static RelationOrigin MapOrigin(MountOrigin origin) => origin switch
    {
        MountOrigin.Command => RelationOrigin.Admin,
        MountOrigin.Rebuild => RelationOrigin.Rebuild,
        MountOrigin.Admin => RelationOrigin.Admin,
        _ => RelationOrigin.Runtime,
    };
}
