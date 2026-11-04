using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalWorkflowFramework.Relations.Kernel;

/// <summary>关系计划目录（operation / derive / binding 的唯一解析点）。</summary>
public sealed class RelationPlanCatalog
{
    private readonly Dictionary<string, RelationOperation> _operations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RelationDerivation> _derivations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RelationBinding> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RelationInvariant> _guardsByEdge = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<RelationInvariant>> _invariantsByEdge;

    public RelationPlanCatalog(RelationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;

        foreach (var operation in plan.Operations)
            _operations.TryAdd(operation.Id, operation);
        foreach (var derivation in plan.Derivations)
            _derivations.TryAdd(derivation.Id, derivation);
        foreach (var binding in plan.Bindings ?? Array.Empty<RelationBinding>())
            _bindings.TryAdd(binding.Target, binding);

        _invariantsByEdge = plan.Invariants
            .GroupBy(i => i.Edge, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RelationInvariant>)g.ToList(), StringComparer.Ordinal);

        foreach (var invariant in plan.Invariants)
        {
            if (!invariant.HasGuard()) continue;
            if (_guardsByEdge.ContainsKey(invariant.Edge))
            {
                throw new InvalidOperationException(
                    $"guard-duplicated：边 '{invariant.Edge}' 存在多条带 guard.pre/post 的 [Invariant]"
                    + $"（{_guardsByEdge[invariant.Edge].Id} / {invariant.Id}）；禁静默取首条（DUP-14）");
            }

            _guardsByEdge.Add(invariant.Edge, invariant);
        }

        EnsureNoDoubleEvaluation(plan.Invariants);
        GuardPredicates = CollectGuardPredicates(plan.Invariants);
    }

    /// <summary>声明面 guard.pre/post 引用的全部谓词 id（装配期谓词预检的唯一输入）。</summary>
    public IReadOnlyCollection<string> GuardPredicates { get; }

    private static IReadOnlyCollection<string> CollectGuardPredicates(IReadOnlyList<RelationInvariant> invariants)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var invariant in invariants)
        {
            foreach (var predicate in invariant.Pre ?? Array.Empty<string>()) ids.Add(predicate);
            foreach (var predicate in invariant.Post ?? Array.Empty<string>()) ids.Add(predicate);
        }

        return ids;
    }

    /// <summary>
    /// 同义双声明检测（DUP-13）：同一约束既以 <c>kind=</c> 又以 <c>guard.pre/post=</c> 表达 ⇒ 写路径双次求值、两份诊断。
    /// 装配期拒绝（声明面保留其一；guard 只承担不变量表达不了的结构谓词）。
    /// </summary>
    private static void EnsureNoDoubleEvaluation(IReadOnlyList<RelationInvariant> invariants)
    {
        foreach (var invariant in invariants)
        {
            var predicates = (invariant.Pre ?? Array.Empty<string>())
                .Concat(invariant.Post ?? Array.Empty<string>());

            foreach (var predicate in predicates)
            {
                var duplicated = invariants.Any(other =>
                    string.Equals(other.Edge, invariant.Edge, StringComparison.Ordinal)
                    && string.Equals(other.Kind, predicate, StringComparison.Ordinal));

                if (!duplicated) continue;

                throw new InvalidOperationException(
                    $"guard-duplicated-expression：边 '{invariant.Edge}' 的约束 '{predicate}' 同时以 kind= 与 guard.pre/post= 声明"
                    + "（同义双求值）；声明面保留其一（DUP-13）");
            }
        }
    }

    public RelationPlan Plan { get; }

    public bool IsEmpty => _operations.Count == 0;

    public RelationOperation? Resolve(string operation)
        => !string.IsNullOrWhiteSpace(operation) && _operations.TryGetValue(operation, out var found) ? found : null;

    public RelationDerivation? ResolveDerivation(string deriveId)
        => !string.IsNullOrWhiteSpace(deriveId) && _derivations.TryGetValue(deriveId, out var found) ? found : null;

    /// <summary>按端点类型取容器归属声明（未声明 ⇒ null）。</summary>
    public RelationBinding? BindingFor(string target)
        => !string.IsNullOrWhiteSpace(target) && _bindings.TryGetValue(target, out var found) ? found : null;

    public IReadOnlyList<RelationInvariant> InvariantsFor(string edge)
        => _invariantsByEdge.TryGetValue(edge, out var found) ? found : Array.Empty<RelationInvariant>();

    /// <summary>取该边声明的守卫（未声明 pre/post ⇒ null；同边多声明取首条，禁隐式合并）。</summary>
    public RelationGuard? GuardFor(string edge)
        => !string.IsNullOrWhiteSpace(edge)
           && _guardsByEdge.TryGetValue(edge, out var invariant)
            ? invariant.ToGuard()
            : null;

    public RelationOperation? ResolveByEdge(string edge, bool remove)
    {
        if (string.IsNullOrWhiteSpace(edge)) return null;
        var matches = _operations.Values
            .Where(o => string.Equals(o.Edge, edge, StringComparison.Ordinal)
                        && (remove ? o.IsRemove() : o.IsEnsure()))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>取维护意图 operation（intent=repair 恰 1 条；0/多义 ⇒ null，禁隐式取首条）。</summary>
    public RelationOperation? ResolveRepair()
    {
        var matches = _operations.Values.Where(o => o.IsRepair()).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
