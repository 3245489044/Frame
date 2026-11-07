using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicMethod.Declarative.Ledger;

/// <summary>
/// 核对面（声明面 / 派发面 / 运行期装配面…）的**事实输入**。
/// <para>
/// <c>Loaded=false</c> 表示「该面未装载/不可读」——它不是空集，更不是「无需对账」；
/// 对账内核把它判为 <see cref="FaceCoverage.Unknown"/>（红）。
/// </para>
/// </summary>
/// <param name="Name">面名（同一次对账内唯一，Ordinal）。</param>
/// <param name="Items">该面实际存在的键/项（已抽取事实；去重由内核完成，保登记序）。</param>
/// <param name="Loaded">该面是否成功装载/解析。</param>
/// <param name="Source">事实来源（文件/产线/日志），仅用于诊断。</param>
public sealed record ReconcileFace(
    string Name,
    IReadOnlyList<string> Items,
    bool Loaded = true,
    string? Source = null);

/// <summary>面对齐边（有向）：<c>From</c> 相对 <c>To</c> 的差异（missing = From 有而 To 无；extra 反之）。</summary>
public sealed record ReconcileEdge(string From, string To);

/// <summary>覆盖等级（**只有 Complete 可绿**；Empty 不是「无需对账」）。</summary>
public enum FaceCoverage
{
    /// <summary>全部面已装载、非空，且所有边无缺失/多余。</summary>
    Complete,

    /// <summary>全部面已装载，但存在缺失/多余项。</summary>
    Partial,

    /// <summary>至少一个面为空（**空面 = 无标的 = 红**，不是「无需对账」）。</summary>
    Empty,

    /// <summary>至少一个面未装载/不可解析（不可判定 ⇒ 红）。</summary>
    Unknown,
}

/// <summary>面摘要（每面实际数量 + 装载/空集事实）。</summary>
public sealed record FaceSummary(
    string Name,
    int Count,
    bool Loaded,
    bool IsEmpty,
    string? Source);

/// <summary>单边差异（缺项/多项均按 Ordinal 去重，保 From/To 登记序）。</summary>
public sealed record FaceEdgeDifference(
    string From,
    string To,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Extra);

/// <summary>
/// 多面对账报告（**事实 + 覆盖等级 + 判定**）。
/// <para>纪律：<c>Ok=true</c> 当且仅当 <see cref="FaceCoverage.Complete"/>；任何空面/未装载/缺项一律不绿。</para>
/// </summary>
/// <param name="Ok">是否可绿（仅 Complete 为 true）。</param>
/// <param name="Coverage">覆盖等级。</param>
/// <param name="Faces">逐面摘要（数量/装载/空集）。</param>
/// <param name="Edges">逐边差异。</param>
/// <param name="TerminalMissing">首面相对末面的缺项（声明面 → 装配面的端到端缺口）。</param>
/// <param name="Reasons">不可绿的全部原因（红/不可判定文案；绿时为空）。</param>
public sealed record FaceReconciliationReport(
    bool Ok,
    FaceCoverage Coverage,
    IReadOnlyList<FaceSummary> Faces,
    IReadOnlyList<FaceEdgeDifference> Edges,
    IReadOnlyList<string> TerminalMissing,
    IReadOnlyList<string> Reasons)
{
    /// <summary>是否可绿（与 <see cref="Ok"/> 同义，供门禁直读）。</summary>
    public bool IsGreen => Ok;

    /// <summary>按面名取摘要（未登记 ⇒ null，调用方须自行可见化）。</summary>
    public FaceSummary? Find(string name)
        => Faces.FirstOrDefault(face => string.Equals(face.Name, name, StringComparison.Ordinal));
}

/// <summary>
/// 多面对账内核（**唯一集合比较语义 = <see cref="LedgerKeyClosure"/> 的 Ordinal 口径**）。
/// <para>
/// 动机（节点装配事故）：声明面 / 派发面 / 运行期装配面三面各自「有数据」不等于一致；
/// 旧实现把空集求和当作 0 错误 ⇒ 全绿。<b>空面必须显式判 <see cref="FaceCoverage.Empty"/>（红）</b>。
/// </para>
/// <para>
/// 通用性：面名不内置领域词（<see cref="Declared"/> / <see cref="Dispatched"/> / <see cref="Assembled"/>
/// 只是节点装配的惯用常量）；任意 N 面 + 任意边（缺省按输入序串成链）。
/// </para>
/// </summary>
public static class FaceReconciler
{
    /// <summary>声明面（惯用名）。</summary>
    public const string Declared = "declared";

    /// <summary>派发面（惯用名）。</summary>
    public const string Dispatched = "dispatched";

    /// <summary>运行期装配面（惯用名）。</summary>
    public const string Assembled = "assembled";

    /// <summary>
    /// 对账（缺省三面链：输入序 = 声明 → 派发 → 装配；末面由 <paramref name="terminalTo"/> 指定）。
    /// </summary>
    /// <param name="faces">核对面（≥2；同面重复 ⇒ 红）。</param>
    /// <param name="edges">显式边（缺省按输入序串成链；端点必须已登记）。</param>
    /// <param name="terminalFrom">端到端起点面（缺省 = 首面）。</param>
    /// <param name="terminalTo">端到端终点面（缺省 = 末面）。</param>
    public static FaceReconciliationReport Reconcile(
        IEnumerable<ReconcileFace> faces,
        IEnumerable<ReconcileEdge>? edges = null,
        string? terminalFrom = null,
        string? terminalTo = null)
    {
        ArgumentNullException.ThrowIfNull(faces);

        var materialized = new List<ReconcileFace>();
        var reasons = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var face in faces)
        {
            if (face is null)
            {
                reasons.Add("对账输入含 null 面（不可判定 ⇒ 红）");
                continue;
            }

            var name = face.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                reasons.Add("对账面名为空（不可定位 ⇒ 红）");
                continue;
            }

            if (!seenNames.Add(name))
            {
                reasons.Add($"面名重复 '{name}'（同一面对账内须唯一）");
                continue;
            }

            materialized.Add(face with { Name = name });
        }

        if (reasons.Count > 0)
            return Red(FaceCoverage.Unknown, materialized, [], [], reasons);

        if (materialized.Count < 2)
        {
            return Red(FaceCoverage.Empty, materialized, [], [],
                [$"核对面不足（实得 {materialized.Count} 面）：至少两面才构成对账，空输入不是「无需对账」"]);
        }

        // 事实摘要：装载/空集先于任何集合比较。
        var summaries = materialized
            .Select(face => new FaceSummary(
                face.Name,
                Normalize(face.Items).Count,
                face.Loaded,
                face.Loaded && Normalize(face.Items).Count == 0,
                face.Source))
            .ToArray();

        var unloaded = summaries.Where(summary => !summary.Loaded).Select(summary => summary.Name).ToArray();
        if (unloaded.Length > 0)
            reasons.Add($"面未装载/不可解析：{string.Join("、", unloaded)}（不可判定 ⇒ 红，禁按空集求绿）");

        var empty = summaries.Where(summary => summary.IsEmpty).Select(summary => summary.Name).ToArray();
        if (empty.Length > 0)
            reasons.Add($"面为空：{string.Join("、", empty)}（空面 = 无标的，不是「无需对账」）");

        // 边：缺省链式；显式边须端点已登记。
        var edgeResolution = ResolveEdges(materialized, edges);
        var resolvedEdges = edgeResolution.Edges;
        reasons.AddRange(edgeResolution.Reasons);

        var facesByName = materialized.ToDictionary(face => face.Name, StringComparer.Ordinal);
        var differences = new List<FaceEdgeDifference>();
        foreach (var edge in resolvedEdges)
        {
            var from = Normalize(facesByName[edge.From].Items);
            var to = Normalize(facesByName[edge.To].Items);
            differences.Add(Difference(edge, from, to));
        }

        var terminalMissing = new List<string>();
        var terminalResolution = ResolveTerminal(materialized, facesByName, terminalFrom, terminalTo);
        terminalMissing.AddRange(terminalResolution.Missing);
        reasons.AddRange(terminalResolution.Reasons);

        foreach (var difference in differences)
        {
            if (difference.Missing.Count > 0)
                reasons.Add($"边 {difference.From} → {difference.To} 缺项 {difference.Missing.Count}：{string.Join(", ", difference.Missing)}");
            if (difference.Extra.Count > 0)
                reasons.Add($"边 {difference.From} → {difference.To} 多项 {difference.Extra.Count}：{string.Join(", ", difference.Extra)}");
        }

        var structural = edgeResolution.Structural || terminalResolution.Structural;
        var coverage = unloaded.Length > 0 || structural
            ? FaceCoverage.Unknown
            : empty.Length > 0
                ? FaceCoverage.Empty
                : reasons.Count > 0
                    ? FaceCoverage.Partial
                    : FaceCoverage.Complete;

        return new FaceReconciliationReport(
            Ok: coverage == FaceCoverage.Complete,
            Coverage: coverage,
            Faces: summaries,
            Edges: differences,
            TerminalMissing: terminalMissing,
            Reasons: reasons);
    }

    private sealed record EdgeResolution(
        IReadOnlyList<ReconcileEdge> Edges,
        IReadOnlyList<string> Reasons,
        bool Structural);

    private static EdgeResolution ResolveEdges(
        IReadOnlyList<ReconcileFace> faces,
        IEnumerable<ReconcileEdge>? edges)
    {
        if (edges is null)
        {
            var chained = new List<ReconcileEdge>(faces.Count - 1);
            for (var index = 0; index + 1 < faces.Count; index++)
                chained.Add(new ReconcileEdge(faces[index].Name, faces[index + 1].Name));
            return new EdgeResolution(chained, [], false);
        }

        var known = new HashSet<string>(faces.Select(face => face.Name), StringComparer.Ordinal);
        var resolved = new List<ReconcileEdge>();
        var reasons = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var structural = false;
        foreach (var edge in edges)
        {
            if (edge is null)
            {
                reasons.Add("对账边含 null（不可判定 ⇒ 红）");
                structural = true;
                continue;
            }

            var key = $"{edge.From}\u0001{edge.To}";
            if (!seen.Add(key))
            {
                reasons.Add($"对账边重复 {edge.From} → {edge.To}");
                structural = true;
                continue;
            }

            if (!known.Contains(edge.From) || !known.Contains(edge.To))
            {
                reasons.Add($"对账边端点未登记 {edge.From} → {edge.To}");
                structural = true;
                continue;
            }

            resolved.Add(edge);
        }

        return new EdgeResolution(resolved, reasons, structural);
    }

    private sealed record TerminalResolution(
        IReadOnlyList<string> Missing,
        IReadOnlyList<string> Reasons,
        bool Structural);

    private static TerminalResolution ResolveTerminal(
        IReadOnlyList<ReconcileFace> faces,
        IReadOnlyDictionary<string, ReconcileFace> facesByName,
        string? terminalFrom,
        string? terminalTo)
    {
        var fromName = string.IsNullOrWhiteSpace(terminalFrom) ? faces[0].Name : terminalFrom.Trim();
        var toName = string.IsNullOrWhiteSpace(terminalTo) ? faces[^1].Name : terminalTo.Trim();
        if (!facesByName.ContainsKey(fromName) || !facesByName.ContainsKey(toName))
        {
            return new TerminalResolution([], [$"端到端面对未登记：{fromName} → {toName}（不可判定 ⇒ 红）"], true);
        }

        var from = Normalize(facesByName[fromName].Items);
        var to = new HashSet<string>(Normalize(facesByName[toName].Items), StringComparer.Ordinal);
        var missing = from.Where(item => !to.Contains(item)).ToArray();
        return new TerminalResolution(
            missing,
            missing.Length > 0 ? [$"端到端缺项 {missing.Length}（{fromName} → {toName}）：{string.Join(", ", missing)}"] : [],
            false);
    }

    private static FaceEdgeDifference Difference(
        ReconcileEdge edge,
        IReadOnlyList<string> from,
        IReadOnlyList<string> to)
    {
        var fromSet = new HashSet<string>(from, StringComparer.Ordinal);
        var toSet = new HashSet<string>(to, StringComparer.Ordinal);
        return new FaceEdgeDifference(
            edge.From,
            edge.To,
            from.Where(item => !toSet.Contains(item)).ToArray(),
            to.Where(item => !fromSet.Contains(item)).ToArray());
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? items)
    {
        if (items is null || items.Count == 0)
            return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item))
                continue;
            var key = item.Trim();
            if (seen.Add(key))
                normalized.Add(key);
        }

        return normalized;
    }

    private static FaceReconciliationReport Red(
        FaceCoverage coverage,
        IReadOnlyList<ReconcileFace> faces,
        IReadOnlyList<FaceEdgeDifference> edges,
        IReadOnlyList<string> terminalMissing,
        IReadOnlyList<string> reasons)
    {
        var summaries = faces
            .Select(face => new FaceSummary(
                face.Name,
                Normalize(face.Items).Count,
                face.Loaded,
                face.Loaded && Normalize(face.Items).Count == 0,
                face.Source))
            .ToArray();

        return new FaceReconciliationReport(false, coverage, summaries, edges, terminalMissing, reasons);
    }
}
