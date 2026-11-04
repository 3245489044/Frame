namespace DataCenter.Protocol.Dataflow;

/// <summary>
/// 血缘声明键（声明面结构事实的唯一拼写源；禁各处手写字面量）。
/// <para>属性值形态：<c>源字段->目标字段</c>，多项以 <c>;</c> 分隔（同一对端点的字段按序配对）。</para>
/// </summary>
public static class TopologyLineageKeys
{
    /// <summary>拓扑边上声明「本关系派生哪些字段级数据依赖」的属性键。</summary>
    public const string Derives = "derives";

    /// <summary>字段对分隔符。</summary>
    public const char PairSeparator = ';';

    /// <summary>字段对内的源/目标分隔符。</summary>
    public const string FieldSeparator = "->";

    /// <summary>
    /// 解析 <c>derives=</c> 值（**唯一实现**；声明源与血缘生成器共用，禁各自再写切分逻辑）。
    /// </summary>
    /// <param name="raw">属性原文（<c>a-&gt;b;c-&gt;d</c>；空白项跳过）。</param>
    /// <param name="pairs">解析出的字段对（声明序；空 = 无可解析项）。</param>
    /// <param name="errors">形态非法项（空 = 全绿；非空 ⇒ 判据不可判定，调用方须可见化）。</param>
    /// <returns>true = 至少解析出一对且无错误。</returns>
    public static bool TryParsePairs(
        string? raw,
        out IReadOnlyList<(string From, string To)> pairs,
        out IReadOnlyList<string> errors)
    {
        var parsed = new List<(string From, string To)>();
        var problems = new List<string>();
        foreach (var pair in (raw ?? string.Empty).Split(PairSeparator))
        {
            var entry = pair.Trim();
            if (entry.Length == 0) continue;

            var separator = entry.IndexOf(FieldSeparator, StringComparison.Ordinal);
            if (separator <= 0 || separator + FieldSeparator.Length >= entry.Length)
            {
                problems.Add(entry);
                continue;
            }

            var from = entry[..separator].Trim();
            var to = entry[(separator + FieldSeparator.Length)..].Trim();
            if (from.Length == 0 || to.Length == 0)
            {
                problems.Add(entry);
                continue;
            }

            parsed.Add((from, to));
        }

        pairs = parsed;
        errors = problems;
        return parsed.Count > 0 && problems.Count == 0;
    }
}

/// <summary>
/// 拓扑血缘生成结果（<b>总是</b>交回可检视的边集 + 诊断；禁静默丢弃未映射项）。
/// </summary>
/// <param name="Edges">生成的数据依赖边集（确定性排序；空 = 无声明可生成）。</param>
/// <param name="Diagnostics">诊断集（未映射的拓扑边 / 非法声明项；空 = 全绿）。</param>
public sealed record TopologyLineageResult(
    IReadOnlyList<DataDependencyEdge> Edges,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>空结果（未接入任何声明时的零值；不伪造边）。</summary>
    public static TopologyLineageResult Empty { get; } = new([], []);

    /// <summary>是否存在未映射/非法项（true ⇒ 不得作为失效依据，须先显式处置）。</summary>
    public bool HasDiagnostics => Diagnostics.Count > 0;
}

/// <summary>
/// 拓扑图 → 数据流图的血缘生成端口（F5 契约壳；实现随接线批次落地）。
/// <para>
/// 纪律：① <b>纯函数</b>（同 <see cref="DeclaredGraph"/> 两次投影结果一致，无时间/随机/IO）；
/// ② 生成规则来自声明面（产品 <c>via=</c> / 边属性），框架本体不含任何具体关系名；
/// ③ 未映射项进 <see cref="TopologyLineageResult.Diagnostics"/>（fail-closed：不默认放行，
/// 也不静默产出近似边）。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-可追踪数据流图框架-设计与实施.md §3.5。
/// </summary>
public interface ITopologyLineage
{
    /// <summary>按声明面规则生成数据依赖边（边是一等产品，见 <see cref="EdgeProductSpec"/>）。</summary>
    TopologyLineageResult Project(DeclaredGraph graph);
}
