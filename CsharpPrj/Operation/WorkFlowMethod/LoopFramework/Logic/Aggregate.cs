namespace WorkFlowMethod.LoopFramework.Logic;

/// <summary>Aggregate 策略（clinerules logic §1）。</summary>
public enum AggregateStrategy
{
    /// <summary>op=argmax_by(key)，空集→fallback。</summary>
    Key,

    /// <summary>op=chain-max，anchor=F(递推函数)。</summary>
    Causal,

    /// <summary>op∈{max,min,sum,count}。</summary>
    Window,

    /// <summary>op=convex-mix(EWMA: v'=α·v+(1-α)·in)。</summary>
    Weight,
}

/// <summary>窗口聚合操作。</summary>
public enum WindowOp
{
    Max,
    Min,
    Sum,
    Count,
}

/// <summary>
/// 高阶算子 Aggregate（clinerules logic §1）：in{Π, op, anchor} out{候选集 v}。
/// 吸收旧法则：Merge / OrderedMerge / SmoothMerge / WindowAggregate / ArgMaxBy / Recurrence / CausalAccumulate。
/// </summary>
public static class Aggregate
{
    /// <summary>key：键升序末位取（argmax_by）；空集→fallback。</summary>
    public static T? KeyBy<T>(IReadOnlyList<T> items, Func<T, IComparable> key, T? fallback = default)
        => items.Count == 0 ? fallback : items.OrderByDescending(key).First();

    /// <summary>causal：链式递推（chain-max 语义），anchor=递推初值。</summary>
    public static TAcc Causal<TAcc>(IEnumerable<TAcc> seq, Func<TAcc, TAcc, TAcc> combine, TAcc anchor)
        => seq.Aggregate(anchor, combine);

    /// <summary>window：max/min/sum/count。</summary>
    public static double Window(IEnumerable<double> seq, WindowOp op)
        => op switch
        {
            WindowOp.Max => seq.Max(),
            WindowOp.Min => seq.Min(),
            WindowOp.Sum => seq.Sum(),
            _ => seq.Count(),
        };

    /// <summary>weight：convex-mix(EWMA: v'=α·v+(1-α)·in)。</summary>
    public static double Ewma(double current, double input, double alpha)
        => (alpha * current) + ((1 - alpha) * input);
}
