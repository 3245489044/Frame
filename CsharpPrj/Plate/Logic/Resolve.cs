namespace Plate.Logic;

/// <summary>Resolve 策略（clinerules logic §1）。</summary>
public enum ResolveStrategy
{
    /// <summary>R=RouteTable(表驱动)，default=Block/否。</summary>
    Table,

    /// <summary>Ch(优先级链 desc-order)，default。</summary>
    Chain,

    /// <summary>δ=TransTable, S₀, Halt。</summary>
    Fsm,

    /// <summary>N=状态字段重赋值表，default=S 原值。</summary>
    StateAssign,
}

/// <summary>表驱动路由项。</summary>
public sealed record RouteItem<TCond, TAction>(TCond Condition, TAction Action);

/// <summary>
/// 高阶算子 Resolve（clinerules logic §1）：in{X, R, default} out{分支动作/决策}。
/// 末路完备：default≠∅ ∨ Halt≠∅，禁悬空裁决（P5）。
/// 吸收旧法则：Gate / ViolationNorm / Route / OrderedResolve / StateTransition / Normalize。
/// </summary>
public static class Resolve
{
    /// <summary>table：自上而下短路首命中；末路走 default（P5：default 不可为空）。</summary>
    public static TAction Table<TCond, TAction>(
        IReadOnlyList<RouteItem<TCond, TAction>> route,
        TCond input,
        TAction @default)
    {
        foreach (var item in route)
        {
            if (EqualityComparer<TCond>.Default.Equals(item.Condition, input))
                return item.Action;
        }
        return @default;
    }

    /// <summary>chain：优先级链 desc-order 首命中。</summary>
    public static T Chain<T>(IEnumerable<T> candidates, Func<T, bool> predicate, T @default)
        => candidates.FirstOrDefault(predicate) is { } hit ? hit : @default;

    /// <summary>fsm：状态转移表驱动；无转移或命中 haltState → Halt。</summary>
    public static TState Fsm<TState, TInput>(
        TState current,
        TInput input,
        IReadOnlyDictionary<(TState, TInput), TState> transitions,
        TState haltState)
    {
        return transitions.TryGetValue((current, input), out var next)
            ? (EqualityComparer<TState>.Default.Equals(next, haltState) ? haltState : next)
            : haltState;
    }

    /// <summary>state_assign：字段重赋值；未命中字段保持原值。</summary>
    public static TState StateAssign<TState>(
        TState current,
        IReadOnlyDictionary<string, object> assignments,
        Func<TState, string, object?> getField,
        Action<TState, string, object> setField)
    {
        foreach (var kv in assignments)
            setField(current, kv.Key, kv.Value);
        return current;
    }
}
