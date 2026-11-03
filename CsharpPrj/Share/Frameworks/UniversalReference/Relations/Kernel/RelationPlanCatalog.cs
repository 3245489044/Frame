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
            if (invariant.HasGuard()) _guardsByEdge.TryAdd(invariant.Edge, invariant);
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
            .Where(o => string.Equals(o.Edge, edge, StringComparison.Ordinal) && o.IsRemove() == remove)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
