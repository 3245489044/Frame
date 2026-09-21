namespace Usharp.Semantics;

/// <summary>
/// 图能力门面：把编译产出的引用图（<see cref="UsharpEdgeSelection.IsGraphEdge"/> 子集）交给装配的
/// <see cref="IUsharpGraphBackend"/>，产出环检测（<see cref="UsharpCodes.CircularReference"/>）与流完整性
/// （<see cref="UsharpCodes.FlowIntegrityViolation"/>）诊断，并提供可达（影响域）/ 路径查询。
/// <para>未装配后端（默认 <see cref="NullGraphBackend"/>）时，图类判据**跳过并显式可见化**
/// （<see cref="UsharpCodes.GraphBackendUnavailable"/>），绝不静默"假装通过"。</para>
/// </summary>
public sealed class UsharpGraphService
{
    public UsharpGraphService(IUsharpGraphBackend? backend = null)
        => Backend = backend ?? NullGraphBackend.Instance;

    /// <summary>装配的图后端（默认空后端）。</summary>
    public IUsharpGraphBackend Backend { get; }

    /// <summary>是否已装配真实图能力。</summary>
    public bool HasBackend => Backend.IsAvailable;

    /// <summary>图类判据全量分析（环检测 + 每个 <c>[Flow]</c> 的流完整性）。</summary>
    public IReadOnlyList<UsharpDiagnostic> Analyze(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var diagnostics = new List<UsharpDiagnostic>();
        if (!HasBackend)
        {
            diagnostics.Add(UsharpDiagnostic.Info(UsharpCodes.GraphBackendUnavailable,
                "未装配图后端（NullGraphBackend）：环检测 / 影响域 / 流完整性判据跳过；闸A、引用即边与结构化诊断不受影响",
                compilation.Documents.FirstOrDefault()?.FilePath ?? "(memory)", 0));
            return diagnostics;
        }

        var edges = UsharpEdgeSelection.GraphEdges(compilation);
        var nodes = UsharpEdgeSelection.Nodes(edges);
        var outgoing = UsharpEdgeSelection.Adjacency(edges);

        foreach (var cycle in Backend.FindCycles(nodes, outgoing))
        {
            if (cycle.Count == 0) continue;
            var (file, line) = AttributeCycle(edges, cycle);
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.CircularReference,
                $"引用图成环：{string.Join(" → ", cycle)} → {cycle[0]}", file, line));
        }

        foreach (var flow in compilation.Flows)
        {
            var steps = flow.Steps.Select(s => s.Key).ToList();
            if (steps.Count == 0) continue;

            var pairs = new List<(string Source, string Target)>(Math.Max(0, steps.Count - 1));
            for (var i = 0; i + 1 < steps.Count; i++) pairs.Add((steps[i], steps[i + 1]));

            var integrity = Backend.ValidateFlowIntegrity(
                flow.FlowId, steps, pairs,
                isEntry: node => string.Equals(node, steps[0], StringComparison.Ordinal),
                isExit: node => string.Equals(node, steps[^1], StringComparison.Ordinal));

            // 归因统一由门面填写（后端只给判据结果，不承担 Usharp 侧的文件/行号知识）
            foreach (var d in integrity)
                diagnostics.Add(d with { SourceFile = flow.SourceFile, LineNumber = flow.LineNumber });
        }

        return diagnostics;
    }

    /// <summary>可达集（影响域）：<paramref name="start"/> 经引用图可达的全部节点（含自身）。</summary>
    public IReadOnlySet<string> Reachable(UsharpCompilation compilation, string start)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return Backend.Reachable(start, UsharpEdgeSelection.Adjacency(UsharpEdgeSelection.GraphEdges(compilation)));
    }

    /// <summary>两点是否可达。</summary>
    public bool HasPath(UsharpCompilation compilation, string source, string target)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return Backend.HasPath(source, target, UsharpEdgeSelection.Adjacency(UsharpEdgeSelection.GraphEdges(compilation)));
    }

    /// <summary>诊断报表渲染（后端单一入口）。</summary>
    public string RenderReport(IReadOnlyList<UsharpDiagnostic> diagnostics)
        => Backend.RenderReport(diagnostics);

    /// <summary>把环归因到参与该环的第一条边的位置（禁止无源报错）。</summary>
    private static (string File, int Line) AttributeCycle(IReadOnlyList<UsharpEdge> edges, IReadOnlyList<string> cycle)
    {
        foreach (var e in edges)
        {
            if (!cycle.Contains(e.Source, StringComparer.Ordinal) && !cycle.Contains(e.Target, StringComparer.Ordinal))
                continue;
            return (e.SourceFile, e.LineNumber);
        }
        return ("(graph)", 0);
    }
}
