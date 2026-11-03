using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.Relations.Kernel;

public sealed partial class DeclaredRelationKernel
{
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

    /// <summary>写来源 → 写边角色（单点映射；禁内核各处硬编码）。</summary>
    private static string RoleOf(RelationOrigin origin) => origin switch
    {
        RelationOrigin.Rebuild => RelationWriteRoles.Reconcile,
        RelationOrigin.Admin => RelationWriteRoles.Declare,
        _ => RelationWriteRoles.Business,
    };

    private RelationResult Fail(string operation, string? edge, string reason)
    {
        _diagnostics?.RelationRejected(operation, edge, reason);
        return RelationResults.Rejected(operation, edge, reason);
    }
}
