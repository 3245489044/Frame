using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.Relations.Kernel;

/// <summary>
/// 守卫求值段（写前 / 写后）：结构化事实装配 + 声明面 guard.pre/post 求值。
/// <para>纪律：判据载体 = 存储结构事实（计数/存在性），执行者叙述不参与；未登记谓词 ⇒ 不可判定 ⇒ 拒绝。</para>
/// </summary>
public sealed partial class DeclaredRelationKernel
{
    /// <summary>写前求值：返回拒绝原因（null = 通过或未声明守卫）。</summary>
    private async Task<string?> EvaluateGuardPreAsync(
        RelationOperation plan, ResourceIdentifier source, ResourceIdentifier target, CancellationToken ct)
    {
        if (_guards is null) return null;
        var guard = _catalog.GuardFor(plan.Edge);
        if (guard is null || guard.Pre.Count == 0) return null;

        var context = await BuildGuardContextAsync(plan, source, target, ct).ConfigureAwait(false);
        var result = _guards.EvaluatePre(guard, context);
        return result.Blocks ? "guard.pre:" + string.Join(";", result.Violations) : null;
    }

    /// <summary>写后求值：违反项进诊断（不反写存储；姿态只影响是否阻断后续批次）。</summary>
    private async Task<IReadOnlyList<string>> EvaluateGuardPostAsync(
        RelationOperation plan, ResourceIdentifier source, ResourceIdentifier target, CancellationToken ct)
    {
        if (_guards is null) return Array.Empty<string>();
        var guard = _catalog.GuardFor(plan.Edge);
        if (guard is null || guard.Post.Count == 0) return Array.Empty<string>();

        var context = await BuildGuardContextAsync(plan, source, target, ct).ConfigureAwait(false);
        var result = _guards.EvaluatePost(guard, context);
        foreach (var violation in result.Violations)
            _diagnostics?.RelationInvariantViolation(plan.Id, guard.Id, violation);
        return result.Violations;
    }

    /// <summary>装配守卫上下文（有界：只读两端邻接；环检测由不变量承担，避免双重遍历）。</summary>
    private async Task<RelationGuardContext> BuildGuardContextAsync(
        RelationOperation plan, ResourceIdentifier source, ResourceIdentifier target, CancellationToken ct)
    {
        var targetKey = RelationEndpoints.KeyOf(target);
        var sourceKey = RelationEndpoints.KeyOf(source);

        var incoming = await _store.GetReferencesAsync(target, includeIncoming: true, ct).ConfigureAwait(false);
        var parents = incoming
            .Where(r => r.IsIncoming
                        && string.Equals(r.RefType, plan.Edge, StringComparison.Ordinal)
                        && !RelationEndpoints.SameEntity(r.Source, source))
            .Select(r => RelationEndpoints.KeyOf(r.Source))
            .Distinct(StringComparer.Ordinal)
            .Count();

        var outgoing = await _store.GetReferencesAsync(source, includeIncoming: false, ct).ConfigureAwait(false);
        var others = outgoing
            .Where(r => !r.IsIncoming
                        && string.Equals(r.RefType, plan.Edge, StringComparison.Ordinal)
                        && !RelationEndpoints.SameEntity(r.Target, target))
            .Select(r => RelationEndpoints.KeyOf(r.Target))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var edgeExists = outgoing.Any(r => !r.IsIncoming
                                           && string.Equals(r.RefType, plan.Edge, StringComparison.Ordinal)
                                           && RelationEndpoints.SameEntity(r.Target, target));

        var sourceExists = true;
        var targetExists = true;
        if (_probeAssembled)
        {
            if (_probe.IsMapped(plan.Source))
                sourceExists = await _probe.ExistsAsync(plan.Source, sourceKey, ct).ConfigureAwait(false);
            if (_probe.IsMapped(plan.Target))
                targetExists = await _probe.ExistsAsync(plan.Target, targetKey, ct).ConfigureAwait(false);
        }

        return new RelationGuardContext(
            sourceKey, targetKey,
            SourceExists: sourceExists,
            TargetExists: targetExists,
            CreatesCycle: false,
            EdgeExists: edgeExists,
            ExistingTargetParents: parents,
            ExistingSourceTargets: others);
    }
}