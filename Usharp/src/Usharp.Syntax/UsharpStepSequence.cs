namespace Usharp.Syntax;

/// <summary>
/// 步骤序列的**唯一**派生入口：<c>[Flow]</c> 的有序步骤键 → 相邻步骤对 / 转移表。
/// <para>
/// 消费方：流完整性判据（<c>UsharpGraphService</c>）与链转移表构造（<c>UsharpTransitionTableBuilder</c>）——
/// 尾步约定（无后继 = 链终点）在此单源，禁止各处再手写「相邻对」循环（J-类重复的收敛点）。
/// </para>
/// </summary>
public static class UsharpStepSequence
{
    /// <summary>步骤键序列（声明序）。</summary>
    public static IReadOnlyList<string> Keys(UsharpFlowDecl flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        return flow.Steps.Select(s => s.Key).ToList();
    }

    /// <summary>相邻步骤对（流完整性判据输入；步骤数 &lt; 2 → 空表）。</summary>
    public static IReadOnlyList<(string Source, string Target)> Pairs(IReadOnlyList<string> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var pairs = new List<(string Source, string Target)>(Math.Max(0, steps.Count - 1));
        for (var i = 0; i + 1 < steps.Count; i++) pairs.Add((steps[i], steps[i + 1]));
        return pairs;
    }

    /// <summary>
    /// 转移表：已完成步骤键 → 下一步骤键（重复键保留首个后继；尾步不在表内 = 链终点，
    /// 对齐既有链语义「键不在表内 → 无下一步 → run_to_end 收敛」）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ToTransitionTable(IReadOnlyList<string> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (source, target) in Pairs(steps)) table.TryAdd(source, target);
        return table;
    }
}
