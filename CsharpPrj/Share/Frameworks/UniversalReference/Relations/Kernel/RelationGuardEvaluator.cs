namespace UniversalWorkflowFramework.Relations.Kernel;

/// <summary>
/// 守卫求值器（通用件，唯一实现）—— 谓词 → 结构事实的查表判定。
/// <para>
/// 纪律：① 未登记谓词 = 不可判定（<c>Satisfied=false</c> 且恒 <c>Blocks=true</c>，禁默认放行）；
/// ② 结构事实为 <c>null</c>（探针未装配 / 存储无 incoming 能力 / 遍历超预算）= 不可判定（同上，禁以默认值冒充确定值）；
/// ③ 姿态只影响 <c>pre</c> 的「是否阻断」，<c>post</c> 违规只进诊断（不反写存储、不阻断）；
/// ④ 装配期校验声明面引用的域谓词已注册，缺失即 fail-fast 点名（禁运行期静默全拒）；⑤ 纯函数，同输入同输出。
/// </para>
/// </summary>
public sealed class RelationGuardEvaluator : IRelationGuardEvaluator
{
    private readonly IReadOnlyDictionary<string, Func<RelationGuardContext, bool?>> _extensions;

    /// <summary>默认实例（无域谓词扩展；未登记谓词仍不可判定）。</summary>
    public static RelationGuardEvaluator Instance { get; } = new();

    /// <summary>默认构造（无扩展谓词）。</summary>
    public RelationGuardEvaluator() : this(null)
    {
    }

    /// <summary>装配实例：<paramref name="extensions"/> 为域侧注入的结构事实谓词（禁域词进框架）。</summary>
    public RelationGuardEvaluator(
        IReadOnlyDictionary<string, Func<RelationGuardContext, bool?>>? extensions)
        : this(extensions, null)
    {
    }

    /// <summary>
    /// 装配实例 + 声明面谓词预检：<paramref name="requiredPredicates"/> 为声明面 guard.pre/post 引用的
    /// 全部谓词 id；框架内建以外必须由 <paramref name="extensions"/> 提供，缺失 ⇒ 装配期 fail-fast 点名。
    /// </summary>
    public RelationGuardEvaluator(
        IReadOnlyDictionary<string, Func<RelationGuardContext, bool?>>? extensions,
        IReadOnlyCollection<string>? requiredPredicates)
    {
        _extensions = extensions ?? new Dictionary<string, Func<RelationGuardContext, bool?>>(StringComparer.Ordinal);
        EnsurePredicatesResolvable(requiredPredicates);
    }

    private void EnsurePredicatesResolvable(IReadOnlyCollection<string>? required)
    {
        if (required is null || required.Count == 0) return;

        var missing = new List<string>();
        foreach (var id in required)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (RelationGuardVocabulary.IsBuiltIn(id)) continue;
            if (_extensions.ContainsKey(id)) continue;
            if (!missing.Contains(id)) missing.Add(id);
        }

        if (missing.Count == 0) return;

        throw new InvalidOperationException(
            "guard-predicate-unassembled：声明面 guard.pre/post 引用了未注册谓词 "
            + string.Join(",", missing)
            + "（装配期 fail-fast；禁运行期静默全拒）");
    }

    /// <inheritdoc />
    public RelationGuardResult EvaluatePre(RelationGuard guard, RelationGuardContext context)
    {
        ArgumentNullException.ThrowIfNull(guard);
        return Evaluate(guard, guard.Pre, context, enforcePosture: true);
    }

    /// <inheritdoc />
    public RelationGuardResult EvaluatePost(RelationGuard guard, RelationGuardContext context)
    {
        ArgumentNullException.ThrowIfNull(guard);
        // post 语义：违规只进诊断（写已提交，不回滚）；posture 只作用于 pre（ERR-08）。
        var result = Evaluate(guard, guard.Post, context, enforcePosture: false);
        return result with { Blocks = false, Posture = guard.Posture };
    }

    private RelationGuardResult Evaluate(
        RelationGuard guard, IReadOnlyList<string>? predicates, RelationGuardContext? context, bool enforcePosture)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var violations = new List<string>();
        var undecidable = false;

        foreach (var predicate in predicates ?? Array.Empty<string>())
        {
            switch (predicate)
            {
                case RelationGuardPredicates.SourceExists:
                    CheckBoolean(ref undecidable, violations, predicate, context.SourceExists,
                        $"source_exists: 源 '{context.SourceKey}' 不存在");
                    break;
                case RelationGuardPredicates.TargetExists:
                    CheckBoolean(ref undecidable, violations, predicate, context.TargetExists,
                        $"target_exists: 目标 '{context.TargetKey}' 不存在");
                    break;
                case RelationGuardPredicates.TargetSingleParent:
                    CheckCount(ref undecidable, violations, predicate, context.ExistingTargetParents,
                        count => $"target_single_parent: 目标 '{context.TargetKey}' 已有 {count} 个父");
                    break;
                case RelationGuardPredicates.SourceSingleTarget:
                    CheckCount(ref undecidable, violations, predicate, context.ExistingSourceTargets,
                        count => $"source_single_target: 源 '{context.SourceKey}' 已有 {count} 个目标");
                    break;
                case RelationGuardPredicates.Acyclic:
                    // 语义反转：true = 将成环 = 违反；null = 不可判定（禁放行）。
                    if (context.CreatesCycle is null)
                    {
                        undecidable = true;
                        violations.Add(
                            $"predicate_undecidable: '{RelationGuardPredicates.Acyclic}' 结构事实不可判定（禁放行）");
                    }
                    else if (context.CreatesCycle.Value)
                    {
                        violations.Add($"acyclic: '{context.SourceKey}' → '{context.TargetKey}' 将成环");
                    }

                    break;
                case RelationGuardPredicates.EdgeAbsent:
                    if (context.EdgeExists)
                        violations.Add($"edge_absent: 边已存在（{context.SourceKey} → {context.TargetKey}）");
                    break;
                case RelationGuardPredicates.ManyToMany:
                    // 共享合法：无违规；共享计数由 RelationInvariantChecker.CountParentsAsync 单独提供。
                    break;
                default:
                    if (_extensions.TryGetValue(predicate, out var extension))
                    {
                        var satisfied = extension(context);
                        if (satisfied is null)
                        {
                            undecidable = true;
                            violations.Add($"predicate_undecidable: '{predicate}' 域谓词无法判定（禁放行）");
                        }
                        else if (!satisfied.Value)
                        {
                            violations.Add($"{predicate}: 结构事实不满足（{context.SourceKey} → {context.TargetKey}）");
                        }

                        break;
                    }

                    undecidable = true;
                    violations.Add($"predicate_undeclared: '{predicate}' 未登记（不可判定，禁放行）");
                    break;
            }
        }

        var posture = string.IsNullOrWhiteSpace(guard.Posture) ? RelationGuardPostures.Require : guard.Posture;
        var blocks = violations.Count > 0
                     && (undecidable
                         || (enforcePosture && string.Equals(posture, RelationGuardPostures.Require, StringComparison.Ordinal)));

        return new RelationGuardResult(violations.Count == 0, blocks, violations, posture)
        {
            Undecidable = undecidable,
        };
    }

    /// <summary>布尔结构事实三态判定：<c>null</c> ⇒ 不可判定；<c>false</c> ⇒ 违反。</summary>
    private static void CheckBoolean(
        ref bool undecidable, List<string> violations, string predicate, bool? value, string violation)
    {
        if (value is null)
        {
            undecidable = true;
            violations.Add($"predicate_undecidable: '{predicate}' 结构事实不可判定（禁放行）");
        }
        else if (!value.Value)
        {
            violations.Add(violation);
        }
    }

    /// <summary>计数结构事实三态判定：<c>null</c> ⇒ 不可判定；<c>&gt;0</c> ⇒ 违反。</summary>
    private static void CheckCount(
        ref bool undecidable, List<string> violations, string predicate, int? value, Func<int, string> violation)
    {
        if (value is null)
        {
            undecidable = true;
            violations.Add($"predicate_undecidable: '{predicate}' 结构事实不可判定（禁放行）");
        }
        else if (value.Value > 0)
        {
            violations.Add(violation(value.Value));
        }
    }
}

/// <summary>守卫谓词词表判定（框架内建谓词的唯一识别点；域谓词一律经装配期注册）。</summary>
public static class RelationGuardVocabulary
{
    /// <summary>是否框架内建谓词（<see cref="RelationGuardPredicates"/> 全量）。</summary>
    public static bool IsBuiltIn(string predicate)
        => predicate switch
        {
            RelationGuardPredicates.SourceExists => true,
            RelationGuardPredicates.TargetExists => true,
            RelationGuardPredicates.TargetSingleParent => true,
            RelationGuardPredicates.SourceSingleTarget => true,
            RelationGuardPredicates.Acyclic => true,
            RelationGuardPredicates.EdgeAbsent => true,
            RelationGuardPredicates.ManyToMany => true,
            _ => false,
        };
}
