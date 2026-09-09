namespace Plate.Logic;

/// <summary>
/// 三值真值域（clinerules AX-CORE.TruthValueDomainLogic）：Judgment∈{T,F,⊥}。
/// ⊥ 为 Scott 吸收元：⊥∧x=⊥、⊥∨x=x、¬⊥=⊥（聚合例外 ⊥:=T 由 Assess.TruthMap 承载）。
/// </summary>
public enum TruthValue
{
    False,
    True,
    Bottom,
}

public static class ThreeValuedLogic
{
    public static TruthValue Not(TruthValue a)
        => a switch
        {
            TruthValue.True => TruthValue.False,
            TruthValue.False => TruthValue.True,
            _ => TruthValue.Bottom, // ¬⊥=⊥
        };

    /// <summary>合取（Scott 语义）：⊥ 为吸收元 → 任一 ⊥ 即 ⊥；否则经典合取。</summary>
    public static TruthValue And(TruthValue a, TruthValue b)
    {
        if (a == TruthValue.Bottom || b == TruthValue.Bottom)
            return TruthValue.Bottom; // ⊥∧x=⊥
        return (a, b) == (TruthValue.True, TruthValue.True) ? TruthValue.True : TruthValue.False;
    }

    /// <summary>析取（Scott 语义）：⊥ 为恒等元 → 任一 ⊥ 返回另一操作数；否则经典析取。</summary>
    public static TruthValue Or(TruthValue a, TruthValue b)
    {
        if (a == TruthValue.Bottom) return b; // ⊥∨x=x
        if (b == TruthValue.Bottom) return a;
        return (a == TruthValue.True || b == TruthValue.True) ? TruthValue.True : TruthValue.False;
    }

    public static TruthValue All(IEnumerable<TruthValue> values)
        => values.Aggregate(TruthValue.True, And);

    public static TruthValue Any(IEnumerable<TruthValue> values)
        => values.Aggregate(TruthValue.False, Or);
}
