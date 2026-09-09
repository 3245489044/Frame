namespace WorkFlowMethod.LoopFramework.Axioms;

/// <summary>
/// 元级指导标注（clinerules axioms §8）。不可判定公理（LogicCorrectness / SingleImplementation /
/// RedundancyElimination）降级为元级指导——不强制引擎执行，避免停机问题污染状态机。
/// 标记类/方法"仅为元级指导，非引擎强制裁决"。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Interface)]
public sealed class StrategyAttribute : Attribute
{
    public string Axiom { get; }

    public StrategyAttribute(string axiom) => Axiom = axiom;
}

/// <summary>不可判定公理 → 元级指导注册表（Strategy 标注权威）。</summary>
public static class AxiomStrategyRegistry
{
    public static readonly IReadOnlyList<AxiomStrategy> Entries = new[]
    {
        new AxiomStrategy(
            "SingleImplementation",
            "∀p∈ProblemSpace:∃!i∈Implementations:implement(p,i)",
            "duplication(p)→locate_canonical(p)→verify_alignment(p)→replace_with_canonical(p)"),
        new AxiomStrategy(
            "RedundancyElimination",
            "¬∃caller:reachable(caller,c)→redundant(c)",
            "redundant(c)→confirm_no_reference(c)→remove(c)"),
        new AxiomStrategy(
            "LogicCorrectness",
            "semantic(i)=intended(i)→correct(i)",
            "semantic(i)≠intended(i)→erroneous(i)→detect_diverge→fix_to_canonical"),
    };

    public static string? TryGetGuidance(string axiom)
        => Entries.FirstOrDefault(e => e.Name == axiom)?.Guidance;
}

/// <summary>一条元级指导。</summary>
public sealed record AxiomStrategy(string Name, string Formula, string Guidance);
