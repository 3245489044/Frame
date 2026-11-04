using System.Linq;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.Relations.Kernel;

/// <summary>
/// 守卫求值段（写前 / 写后）：结构化事实装配 + 声明面 guard.pre/post 求值。
/// <para>纪律：判据载体 = 存储结构事实（计数/存在性/环）；未登记谓词 ⇒ 不可判定 ⇒ 拒绝。
/// 结构事实未取得（探针未装配 / 存储无 incoming 能力 / 预算超限）⇒ 三态 <c>null</c> ⇒ 不可判定 ⇒ 拒绝（禁默认放行）。</para>
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

        var context = await BuildGuardContextAsync(plan, guard, source, target, ct).ConfigureAwait(false);
        var result = _guards.EvaluatePre(guard, context);
        return result.Blocks ? "guard.pre:" + string.Join(";", result.Violations) : null;
    }

    /// <summary>写后求值：违反项进诊断（不反写存储；posture 只作用于 pre，post 永不断批）。</summary>
    private async Task<IReadOnlyList<string>> EvaluateGuardPostAsync(
        RelationOperation plan, ResourceIdentifier source, ResourceIdentifier target, CancellationToken ct)
    {
        if (_guards is null) return Array.Empty<string>();
        var guard = _catalog.GuardFor(plan.Edge);
        if (guard is null || guard.Post.Count == 0) return Array.Empty<string>();

        var context = await BuildGuardContextAsync(plan, guard, source, target, ct).ConfigureAwait(false);
        var result = _guards.EvaluatePost(guard, context);
        foreach (var violation in result.Violations)
            _diagnostics?.RelationInvariantViolation(plan.Id, guard.Id, violation);
        return result.Violations;
    }

    /// <summary>
    /// 装配守卫上下文（有界：只读两端邻接；按声明谓词需求求值 <c>pass-1</c>，未用到的结构事实保持 <c>null</c> = 不可判定）。
    /// </summary>
    private async Task<RelationGuardContext> BuildGuardContextAsync(
        RelationOperation plan, RelationGuard guard, ResourceIdentifier source, ResourceIdentifier target,
        CancellationToken ct)
    {
        var predicates = (guard.Pre ?? Array.Empty<string>())
            .Concat(guard.Post ?? Array.Empty<string>())
            .ToList();

        bool Has(string predicate) => predicates.Contains(predicate, StringComparer.Ordinal);

        // 域谓词可读取任意结构事实字段 ⇒ 存在域谓词时保守装配（禁让域判断读到 null 而误判）。
        var hasDomainPredicate = predicates.Any(static p => !RelationGuardVocabulary.IsBuiltIn(p));
        var needsCounts = hasDomainPredicate
                          || Has(RelationGuardPredicates.TargetSingleParent)
                          || Has(RelationGuardPredicates.SourceSingleTarget);
        var needsEdge = Has(RelationGuardPredicates.EdgeAbsent);
        var needsCycle = Has(RelationGuardPredicates.Acyclic);

        var targetKey = RelationEndpoints.KeyOf(target);
        var sourceKey = RelationEndpoints.KeyOf(source);

        int? parents = null;
        int? others = null;
        var edgeExists = false;

        var canReadIncoming = _store is not IReferenceIncomingCapability capability || capability.SupportsIncoming;

        if (needsCounts && canReadIncoming)
        {
            var incoming = await _store.GetReferencesAsync(target, includeIncoming: true, ct).ConfigureAwait(false);
            parents = incoming
                .Where(r => r.IsIncoming
                            && string.Equals(r.RefType, plan.Edge, StringComparison.Ordinal)
                            && !RelationEndpoints.SameEntity(r.Source, source))
                .Select(r => RelationEndpoints.KeyOf(r.Source))
                .Distinct(StringComparer.Ordinal)
                .Count();
        }

        if (needsCounts || needsEdge)
        {
            var outgoing = await _store.GetReferencesAsync(source, includeIncoming: false, ct).ConfigureAwait(false);

            if (needsCounts)
            {
                others = outgoing
                    .Where(r => !r.IsIncoming
                                && string.Equals(r.RefType, plan.Edge, StringComparison.Ordinal)
                                && !RelationEndpoints.SameEntity(r.Target, target))
                    .Select(r => RelationEndpoints.KeyOf(r.Target))
                    .Distinct(StringComparer.Ordinal)
                    .Count();
            }

            if (needsEdge)
            {
                edgeExists = outgoing.Any(r => !r.IsIncoming
                                               && string.Equals(r.RefType, plan.Edge, StringComparison.Ordinal)
                                               && RelationEndpoints.SameEntity(r.Target, target));
            }
        }

        // 存在性：未装配探针 / 类型未映射 ⇒ 不可判定（null），不填 true（ERR-02）。
        bool? sourceExists = null;
        bool? targetExists = null;
        if (_probeAssembled)
        {
            if (_probe.IsMapped(plan.Source))
                sourceExists = await _probe.ExistsAsync(plan.Source, sourceKey, ct).ConfigureAwait(false);
            if (_probe.IsMapped(plan.Target))
                targetExists = await _probe.ExistsAsync(plan.Target, targetKey, ct).ConfigureAwait(false);
        }

        // 环检测：唯一实现复用不变量检查器（超预算 ⇒ null = 不可判定，不冒充「成环」）。
        bool? createsCycle = null;
        if (needsCycle)
        {
            createsCycle = await _invariants
                .DetectCycleAsync(plan.Edge, source, target, ct)
                .ConfigureAwait(false);
        }

        return new RelationGuardContext(
            sourceKey, targetKey,
            SourceExists: sourceExists,
            TargetExists: targetExists,
            CreatesCycle: createsCycle,
            EdgeExists: edgeExists,
            ExistingTargetParents: parents,
            ExistingSourceTargets: others);
    }
}