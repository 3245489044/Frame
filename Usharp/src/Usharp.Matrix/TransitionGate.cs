using Usharp.Semantics;

namespace Usharp.Matrix;

/// <summary>
/// 转移表**状态门**（通用）：把 <c>kind=keys</c> 空间的 <c>[Matrix]</c> 转移表变成运行期可执行的状态门 ——
/// 非法跃迁在声明面即不可表达，运行期由本类按表拒绝（替代各处 <c>if</c> 各自记忆合法后继的旧形态）。
/// <para>行方向硬性：<c>Transitions[当前态] = 下一步态</c>；终态（表中无该键）无后继。</para>
/// <para>不激活语义：转移表缺失 ⇒ 工厂返回 <c>null</c>，调用方**放行**（零行为变化）+ 可见化。</para>
/// </summary>
public sealed class TransitionGate
{
    private readonly IReadOnlyDictionary<string, string> _transitions;

    private TransitionGate(IReadOnlyList<string> states, IReadOnlyDictionary<string, string> transitions)
    {
        States = states;
        _transitions = transitions;
    }

    /// <summary>已声明的坐标序（<c>[Space] kind=keys</c> 声明序）。</summary>
    public IReadOnlyList<string> States { get; }

    /// <summary>转移表（键 = 当前态 → 值 = 下一步态）。</summary>
    public IReadOnlyDictionary<string, string> Transitions => _transitions;

    /// <summary>初始态（声明序首位）；无坐标声明 ⇒ 空串。</summary>
    public string InitialState => States.Count > 0 ? States[0] : string.Empty;

    /// <summary>
    /// 由声明面坐标序 + 转移表创建状态门（坐标空 / 转移表空 ⇒ null，不激活）。
    /// 消费方自持「矩阵号 / 空间号 → 转移表」的映射（本类不做声明面解析）。
    /// </summary>
    /// <param name="states">坐标序（<c>[Space]</c> 声明序）。</param>
    /// <param name="transitions">转移表（当前态 → 下一步态）。</param>
    public static TransitionGate? From(
        IReadOnlyList<string>? states,
        IReadOnlyDictionary<string, string>? transitions)
        => states is not { Count: > 0 } || transitions is not { Count: > 0 }
            ? null
            : new TransitionGate(states, transitions);

    /// <summary>
    /// 按矩阵号读转移表（唯一映射点：矩阵号 → 转移表），并回报装配命中的矩阵数与
    /// <c>over=</c> 空间号（歧义 = 同空间多张转移矩阵，由调用方可见化）。
    /// </summary>
    /// <param name="emit">一次矩阵装配的批产物。</param>
    /// <param name="matrixId">转移矩阵号。</param>
    public static TransitionTableReadResult ReadTransitions(UsharpMatrixEmitResult? emit, string matrixId)
    {
        if (emit is null || string.IsNullOrWhiteSpace(matrixId)) return TransitionTableReadResult.None;

        IReadOnlyDictionary<string, string>? table = null;
        var matched = 0;
        string? overSpaceId = null;
        foreach (var binding in emit.Bindings)
        {
            if (binding.Kind != UsharpMatrixKind.Transition) continue;
            if (!string.Equals(binding.MatrixId, matrixId, StringComparison.Ordinal)) continue;

            matched++;
            overSpaceId ??= binding.OverSpaceId;
            if (binding.TransitionTable is { Count: > 0 } candidate) table ??= candidate;
        }

        return new TransitionTableReadResult(table, matched, overSpaceId);
    }

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

/// <summary>
/// 转移表读取结果：<see cref="Table"/>（未装配 ⇒ null）、<see cref="Matched"/>（命中矩阵数）、
/// <see cref="OverSpaceId"/>（<c>over=</c> 空间号）。
/// </summary>
public sealed record TransitionTableReadResult(
    IReadOnlyDictionary<string, string>? Table,
    int Matched,
    string? OverSpaceId)
{
    /// <summary>空结果（未提供装配结果 / 矩阵号空白）。</summary>
    public static TransitionTableReadResult None { get; } = new(null, 0, null);

    /// <summary>同一矩阵号命中多张转移表（歧义：须可见化，取首个为权威）。</summary>
    public bool Ambiguous => Matched > 1;
}
