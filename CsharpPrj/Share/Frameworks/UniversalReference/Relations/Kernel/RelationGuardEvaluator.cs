namespace UniversalWorkflowFramework.Relations.Kernel;

/// <summary>
/// 守卫求值器（通用件，唯一实现）—— 谓词 → 结构事实的查表判定。
/// <para>
/// 纪律：① 未登记谓词 = 不可判定（<c>Satisfied=false</c> 且恒 <c>Blocks=true</c>，禁默认放行）；
/// ② 姿态只影响「是否阻断」，不改变违反清单（可见化不得被姿态吞掉）；③ 纯函数，同输入同输出。
/// </para>
/// </summary>
public sealed class RelationGuardEvaluator : IRelationGuardEvaluator
{
    /// <summary>进程级默认实例（无状态，可安全共享）。</summary>
    public static RelationGuardEvaluator Instance { get; } = new();

    /// <inheritdoc />
    public RelationGuardResult EvaluatePre(RelationGuard guard, RelationGuardContext context)
    {
        ArgumentNullException.ThrowIfNull(guard);
        return Evaluate(guard, guard.Pre, context);
    }

    /// <inheritdoc />
    public RelationGuardResult EvaluatePost(RelationGuard guard, RelationGuardContext context)
    {
        ArgumentNullException.ThrowIfNull(guard);
        return Evaluate(guard, guard.Post, context);
    }

    private static RelationGuardResult Evaluate(
        RelationGuard guard, IReadOnlyList<string>? predicates, RelationGuardContext? context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var violations = new List<string>();
        var undecidable = false;

        foreach (var predicate in predicates ?? Array.Empty<string>())
        {
            switch (predicate)
            {
                case RelationGuardPredicates.SourceExists:
                    if (!context.SourceExists) violations.Add($"source_exists: 源 '{context.SourceKey}' 不存在");
                    break;
                case RelationGuardPredicates.TargetExists:
                    if (!context.TargetExists) violations.Add($"target_exists: 目标 '{context.TargetKey}' 不存在");
                    break;
                case RelationGuardPredicates.TargetSingleParent:
                    if (context.ExistingTargetParents > 0)
                        violations.Add($"target_single_parent: 目标 '{context.TargetKey}' 已有 {context.ExistingTargetParents} 个父");
                    break;
                case RelationGuardPredicates.SourceSingleTarget:
                    if (context.ExistingSourceTargets > 0)
                        violations.Add($"source_single_target: 源 '{context.SourceKey}' 已有 {context.ExistingSourceTargets} 个目标");
                    break;
                case RelationGuardPredicates.Acyclic:
                    if (context.CreatesCycle)
                        violations.Add($"acyclic: '{context.SourceKey}' → '{context.TargetKey}' 将成环");
                    break;
                case RelationGuardPredicates.EdgeAbsent:
                    if (context.EdgeExists)
                        violations.Add($"edge_absent: 边已存在（{context.SourceKey} → {context.TargetKey}）");
                    break;
                case RelationGuardPredicates.ManyToMany:
                    // 共享合法：无违规；共享计数由 RelationInvariantChecker.CountParentsAsync 单独提供。
                    break;
                default:
                    undecidable = true;
                    violations.Add($"predicate_undeclared: '{predicate}' 未登记（不可判定，禁放行）");
                    break;
            }
        }

        var posture = string.IsNullOrWhiteSpace(guard.Posture) ? RelationGuardPostures.Require : guard.Posture;
        var blocks = violations.Count > 0
                     && (undecidable || string.Equals(posture, RelationGuardPostures.Require, StringComparison.Ordinal));

        return new RelationGuardResult(violations.Count == 0, blocks, violations, posture);
    }
}
