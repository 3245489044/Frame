namespace Share.StateManagement;

/// <summary>
/// 声明转移表**查询核**（纯查询值对象，零副作用、零声明面解析）：
/// 由声明态序 + 转移表裁决终态 / 合法后继 / 跃迁合法性。
/// <para>行方向硬性：<c>Transitions[当前态] = 下一步态</c>；终态（声明态但表中无该键）无后继。</para>
/// <para>不激活语义：态序为空或转移表为空 ⇒ 工厂返回 <c>null</c>，调用方**放行**（零行为变化）+ 可见化。</para>
/// <para>消费方自持「矩阵号 / 空间号 → 转移表」的映射；本类不做 Usharp 读法（读表留在语言适配层）。</para>
/// </summary>
public sealed class DeclaredTransitionGate
{
    private readonly IReadOnlyDictionary<string, string> _transitions;

    private DeclaredTransitionGate(IReadOnlyList<string> states, IReadOnlyDictionary<string, string> transitions)
    {
        States = states;
        _transitions = transitions;
    }

    /// <summary>已声明的坐标序（声明序）。</summary>
    public IReadOnlyList<string> States { get; }

    /// <summary>转移表（键 = 当前态 → 值 = 下一步态）。</summary>
    public IReadOnlyDictionary<string, string> Transitions => _transitions;

    /// <summary>初始态（声明序首位）；无坐标声明 ⇒ 空串。</summary>
    public string InitialState => States.Count > 0 ? States[0] : string.Empty;

    /// <summary>由声明态序 + 转移表创建状态门（态序空 / 转移表空 ⇒ null，不激活）。</summary>
    /// <param name="states">态序（声明序）。</param>
    /// <param name="transitions">转移表（当前态 → 下一步态）。</param>
    public static DeclaredTransitionGate? From(
        IReadOnlyList<string>? states,
        IReadOnlyDictionary<string, string>? transitions)
        => states is not { Count: > 0 } || transitions is not { Count: > 0 }
            ? null
            : new DeclaredTransitionGate(states, transitions);

    /// <summary>终态（无后继）：该态已声明且转移表无该键。</summary>
    /// <param name="state">状态键。</param>
    public bool IsTerminal(string state)
        => States.Contains(state, StringComparer.Ordinal) && !_transitions.ContainsKey(state);

    /// <summary>取该态的合法后继（终态 / 未声明 ⇒ null）。</summary>
    /// <param name="state">状态键。</param>
    public string? NextOf(string state)
        => _transitions.TryGetValue(state ?? string.Empty, out var next) ? next : null;

    /// <summary>跃迁是否合法：原态不动（no-op）恒合法；否则须与声明表逐字一致。</summary>
    /// <param name="from">当前态。</param>
    /// <param name="to">目标态。</param>
    public bool IsLegal(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.Ordinal)) return true;
        return _transitions.TryGetValue(from ?? string.Empty, out var next)
            && string.Equals(next, to, StringComparison.Ordinal);
    }
}
