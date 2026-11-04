using UniversalWorkflowFramework.Relations;
using UniversalWorkflowFramework.Relations.Kernel;
using Usharp.Semantics;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// Usharp 关系声明面 → Frame 中性 <see cref="RelationPlan"/>（唯一桥接；DataStoreDriven 不引 Usharp）。
/// <para>唯一生产路径 = 同一次编译直接产 DTO（禁读生成 JSON：两套 plan 生产路径会漂移）。</para>
/// </summary>
public static class DeclaredRelationPlan
{
    /// <summary>由编译产物构建计划（无声明 ⇒ <see cref="RelationPlans.Empty"/>，可见化由调用方判定）。</summary>
    public static RelationPlan BuildFromCompilation(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var operations = compilation.Relations
            .Select(r => new RelationOperation(
                r.OperationId, r.Display, r.Edge, r.Source, r.Target, r.Flow, r.Namespace,
                r.Exists, r.Duplicate, r.OnMissing, r.Arity, r.Intent, r.Emit,
                r.Container, r.OnMismatch, r.Provision))
            .ToList();

        var invariants = compilation.Invariants
            .Select(i => new RelationInvariant(
                i.InvariantId, i.Kind, i.Edge, i.OnViolation, i.Scope, i.Pre, i.Post,
                string.IsNullOrWhiteSpace(i.Posture) ? RelationGuardPostures.Require : i.Posture))
            .ToList();

        var derivations = compilation.Derivations
            .Select(d => new RelationDerivation(
                d.DeriveId, d.From, d.To, d.Via, d.Op, d.Normalize, d.Order, d.Empty))
            .ToList();

        return operations.Count == 0 && invariants.Count == 0 && derivations.Count == 0
            ? RelationPlans.Empty
            : new RelationPlan("1.0", operations, invariants, derivations);
    }
}
