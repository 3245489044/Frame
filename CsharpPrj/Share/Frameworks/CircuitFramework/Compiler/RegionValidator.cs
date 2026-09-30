using Share.CircuitFramework.Projection;

namespace Share.CircuitFramework.Compiler;

/// <summary>
/// 区域声明校验（编译期判据，随 <see cref="CircuitCompiler.TryCompile"/> 作为独立编译阶段运行）：
/// 把「区域」的合法性前移到编译期 —— 使「编译绿 ⇒ 运行期不再有区域类错误」，不留运行期炸点。
/// <list type="bullet">
///   <item><c>CFG-019</c>：计次为负（<c>Repeats &lt; 0</c>）；<c>Repeats == 0</c> 合法（零次迭代）。</item>
///   <item><c>CFG-020</c>：条件迭代的判定端口缺失 / 未在节点入端口声明 / 未接线 / 声明类型非布尔。</item>
///   <item><c>CFG-021</c>：子计划不可解析（未声明 / 经唯一编译入口 <c>TryCompile</c> 失败）/ 嵌套超深。</item>
///   <item><c>CFG-022</c>：迭代上限缺失或非法（R1「有界量化」的机器判据）。</item>
///   <item><c>CFG-023</c>：端口映射两端不存在（父入 / 子入口 / 子出口 / 父出）+ 子入口漏喂。</item>
/// </list>
/// <para>
/// 分工：<see cref="RecipeGraphValidator"/> 管配方域产物图（产物唯一 / 缺失产物），
/// <see cref="CircuitValidator"/> 管电路域（环 / 端口解析 / fan-in / 出口 / 声明-执行对账）；
/// 本类只判区域声明面（承载节点 / 映射 / 迭代计划 / 子计划可解析），三者互补不重复。
/// </para>
/// <para>子计划走同一编译入口（R2）：子图也必须有编译期判据，不得裸跑；嵌套区域经 <paramref name="depth"/> 限深。</para>
/// </summary>
public static class RegionValidator
{
    /// <summary>区域嵌套深度上限（防自引用声明导致的无界递归；超限 ⇒ CFG-021）。</summary>
    public const int MaxRegionNestingDepth = 16;

    /// <summary>校验配方的区域声明（顶层入口）。</summary>
    public static IReadOnlyList<CircuitDiagnostic> Validate(CircuitRecipeSpec spec) => Validate(spec, depth: 1);

    /// <summary>校验配方的区域声明；depth = 当前嵌套层（1 基，供递归限深）。</summary>
    internal static IReadOnlyList<CircuitDiagnostic> Validate(CircuitRecipeSpec spec, int depth)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var d = new List<CircuitDiagnostic>();
        if (spec.Regions is not { Count: > 0 }) return d;

        if (depth > MaxRegionNestingDepth)
        {
            d.Add(Err("CFG-021",
                $"区域嵌套深度超过上限 {MaxRegionNestingDepth}（疑似自引用声明）：'{spec.CircuitId}'。", spec.CircuitId));
            return d;
        }

        var steps = new Dictionary<string, CircuitStepSpec>(StringComparer.Ordinal);
        foreach (var step in spec.Steps) steps[step.Id] = step;

        foreach (var region in spec.Regions)
            ValidateRegion(spec, region, steps, depth, d);

        return d;
    }

    private static void ValidateRegion(
        CircuitRecipeSpec spec, CircuitRegionSpec region,
        IReadOnlyDictionary<string, CircuitStepSpec> steps, int depth, List<CircuitDiagnostic> d)
    {
        var nodeId = region.NodeId ?? string.Empty;
        if (!steps.TryGetValue(nodeId, out var step))
        {
            d.Add(Err("CFG-023",
                $"区域承载节点 '{nodeId}' 未在配方 '{spec.CircuitId}' 的步骤中声明：父侧端口两端不可判。", nodeId));
            return;
        }

        CheckIterationPlan(spec, region, step, d);
        CheckPortMapping(region, step, d);

        if (region.Child is null)
        {
            d.Add(Err("CFG-021", $"区域 '{nodeId}' 未声明子计划（Child 缺失）：区域无体可执行。", nodeId));
            return;
        }

        // R2：子计划须经唯一编译入口（子图也须有编译期判据）；失败 ⇒ CFG-021（不得运行期才炸）。
        if (!CircuitCompiler.TryCompile(region.Child, out _, out var childDiagnostics, depth + 1))
            d.Add(Err("CFG-021",
                $"区域 '{nodeId}' 的子计划 '{region.Child.CircuitId}' 不可解析："
                + string.Join("；", childDiagnostics.Select(x => x.Code + " " + x.Message)), nodeId));
    }

    /// <summary>迭代计划判据（CFG-019 / CFG-020 / CFG-022）：一切迭代必须有界，判定载体必须是端口键。</summary>
    private static void CheckIterationPlan(
        CircuitRecipeSpec spec, CircuitRegionSpec region, CircuitStepSpec step, List<CircuitDiagnostic> d)
    {
        var nodeId = region.NodeId;
        if (region.Plan is null)
        {
            d.Add(Err("CFG-022", $"区域 '{nodeId}' 未声明迭代计划（IterationPlan 缺失）：无界迭代禁入。", nodeId));
            return;
        }

        var plan = region.Plan;

        // CFG-019：计次合法性（Repeats == 0 合法 = 零次迭代，作负对照）
        if (plan.Kind == RegionIterationKind.FixedRepeats && plan.Repeats < 0)
            d.Add(Err("CFG-019",
                $"区域 '{nodeId}' 的计次为负（Repeats={plan.Repeats}）：有界重复须为非负整数。", nodeId));

        switch (plan.Kind)
        {
            case RegionIterationKind.Once:
                if (plan.MaxIterations != 1)
                    d.Add(Err("CFG-022",
                        $"区域 '{nodeId}' 的单趟迭代上限须恒为 1（当前 {Describe(plan.MaxIterations)}）。", nodeId));
                break;

            case RegionIterationKind.FixedRepeats:
                if (plan.MaxIterations is null)
                    d.Add(Err("CFG-022", $"区域 '{nodeId}' 未声明迭代上限（MaxIterations 缺失）。", nodeId));
                else if (plan.MaxIterations < plan.Repeats)
                    d.Add(Err("CFG-022",
                        $"区域 '{nodeId}' 的迭代上限 {plan.MaxIterations} 小于声明计次 {plan.Repeats}：上限须覆盖计次。", nodeId));
                break;

            default: // WhileTrue / UntilTrue：判定载体须为端口键（声明文案不参与判定）
                if (string.IsNullOrWhiteSpace(plan.JudgePort))
                {
                    d.Add(Err("CFG-020",
                        $"区域 '{nodeId}' 的条件迭代未声明判定端口：判定载体须为端口键（禁对声明文案求值）。", nodeId));
                    break;
                }

                if (plan.MaxIterations is null || plan.MaxIterations <= 0)
                    d.Add(Err("CFG-022",
                        $"区域 '{nodeId}' 的条件迭代须声明有效上限（MaxIterations > 0，当前 {Describe(plan.MaxIterations)}）。", nodeId));

                if (step.InPorts.All(p => p != plan.JudgePort))
                {
                    d.Add(Err("CFG-020",
                        $"区域 '{nodeId}' 的判定端口 '{plan.JudgePort}' 未在该节点入端口声明（可用：{Join(step.InPorts)}）。", nodeId));
                    break;
                }

                // 供给判据单一来源（RecipeGraphValidator.IsSupplied）；区域判定端口用严格口径：不吃自产
                if (!RecipeGraphValidator.IsSupplied(spec, nodeId, plan.JudgePort, excludeSelfProducer: true))
                    d.Add(Err("CFG-020",
                        $"区域 '{nodeId}' 的判定端口 '{plan.JudgePort}' 未接线（无 Wire 进入、非入口绑定、非常量注入、无人产出）。", nodeId));

                var declared = region.InBindings.FirstOrDefault(b => b.ParentPort == plan.JudgePort)?.DataType;
                if (declared is { Length: > 0 } && !IsBoolType(declared))
                    d.Add(Err("CFG-020",
                        $"区域 '{nodeId}' 的判定端口 '{plan.JudgePort}' 声明类型为 '{declared}'：判定载体须为布尔（bool）。", nodeId));
                break;
        }
    }

    /// <summary>端口映射判据（CFG-023）：两端存在性 + 子入口不漏喂（对齐「禁静默缺席」口径）。</summary>
    private static void CheckPortMapping(CircuitRegionSpec region, CircuitStepSpec step, List<CircuitDiagnostic> d)
    {
        var nodeId = region.NodeId;
        var child = region.Child;

        foreach (var binding in region.InBindings)
        {
            if (step.InPorts.All(p => p != binding.ParentPort))
                d.Add(Err("CFG-023",
                    $"区域 '{nodeId}' 的入映射父端口 '{binding.ParentPort}' 未在节点 '{nodeId}' 入端口声明（可用：{Join(step.InPorts)}）。", nodeId));
            if (child is not null && !child.EntryPorts.Contains(binding.ChildPort, StringComparer.Ordinal))
                d.Add(Err("CFG-023",
                    $"区域 '{nodeId}' 的入映射子端口 '{binding.ChildPort}' 未在子计划 '{child.CircuitId}' 的 EntryPorts 中声明（可用：{Join(child.EntryPorts)}）。", nodeId));
        }

        foreach (var binding in region.OutBindings)
        {
            if (step.OutPorts.All(p => p != binding.ParentPort))
                d.Add(Err("CFG-023",
                    $"区域 '{nodeId}' 的出映射父端口 '{binding.ParentPort}' 未在节点 '{nodeId}' 出端口声明（可用：{Join(step.OutPorts)}）。", nodeId));
            if (child is not null && !child.ExitPorts.Contains(binding.ChildPort, StringComparer.Ordinal))
                d.Add(Err("CFG-023",
                    $"区域 '{nodeId}' 的出映射子端口 '{binding.ChildPort}' 未在子计划 '{child.CircuitId}' 的 ExitPorts 中声明（可用：{Join(child.ExitPorts)}）。", nodeId));
        }

        if (child is not null)
        {
            var mapped = region.InBindings.Select(b => b.ChildPort).ToHashSet(StringComparer.Ordinal);
            var unmapped = child.EntryPorts.Where(p => !mapped.Contains(p)).ToArray();
            if (unmapped.Length > 0)
                d.Add(Err("CFG-023",
                    $"区域 '{nodeId}' 未映射子计划 '{child.CircuitId}' 的入口端口：{string.Join(" | ", unmapped)}（禁静默缺席）。", nodeId));
        }
    }

    private static bool IsBoolType(string declared)
        => declared.Equals("bool", StringComparison.OrdinalIgnoreCase)
           || declared.Equals("boolean", StringComparison.OrdinalIgnoreCase)
           || declared.Equals("System.Boolean", StringComparison.OrdinalIgnoreCase);

    private static CircuitDiagnostic Err(string code, string message, string? nodeId)
        => new(DiagnosticSeverity.Error, code, message, nodeId);

    private static string Join(IReadOnlyList<string> items)
        => items.Count == 0 ? "（无）" : string.Join(" | ", items);

    private static string Describe(int? value) => value?.ToString() ?? "缺失";
}
