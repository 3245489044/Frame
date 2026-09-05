namespace WorkFlowMethod.LoopFramework.Logic;

/// <summary>Assess 策略（clinerules logic §1）。</summary>
public enum AssessStrategy
{
    /// <summary>D=∧SUBᵢ（合取；⊥ 按 Scott 域吸收律）。</summary>
    Conjunction,

    /// <summary>D=Map∘J（投影判定；J=判定映射/Judge）。</summary>
    Projection,

    /// <summary>真值映射/重赋值（如 ⊥:=T，StateValidity 聚合例外）。</summary>
    TruthMap,
}

/// <summary>
/// 高阶算子 Assess（clinerules logic §1）：in{SUB*≥2, Map} out{φ∈{T,F,⊥}}。
/// 吸收旧法则：Judge / AndGate / FormEquivalence / Project / TruthProjection。
/// </summary>
public static class Assess
{
    /// <summary>conjunction：合取判定。</summary>
    public static TruthValue Conjunction(IReadOnlyList<TruthValue> subs)
        => ThreeValuedLogic.All(subs);

    /// <summary>projection：Map∘J 投影判定。</summary>
    public static TruthValue Projection<T>(T input, Func<T, TruthValue> judge)
        => judge(input);

    /// <summary>truth_map：真值映射/重赋值。</summary>
    public static TruthValue TruthMap(TruthValue value, Func<TruthValue, TruthValue> map)
        => map(value);

    /// <summary>统一入口：先合取，再按策略投影/映射。</summary>
    public static TruthValue Evaluate<T>(
        AssessStrategy strategy,
        IReadOnlyList<TruthValue> subs,
        T input,
        Func<T, TruthValue>? judge = null,
        Func<TruthValue, TruthValue>? map = null)
    {
        return strategy switch
        {
            AssessStrategy.Projection when judge != null => Projection(input, judge),
            AssessStrategy.TruthMap when map != null => TruthMap(Conjunction(subs), map),
            _ => Conjunction(subs),
        };
    }
}
