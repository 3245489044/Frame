using System.Runtime.CompilerServices;
using Usharp.Syntax;

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

    /// <summary>每编译一份图视图（环检测 / 可达 / 路径共用；避免同一编译重复重建邻接表）。</summary>
    private static readonly ConditionalWeakTable<UsharpCompilation, UsharpGraphView> Views = new();

    /// <summary>取（或构建）编译产物的图视图。</summary>
    public static UsharpGraphView View(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return Views.GetValue(compilation, UsharpGraphView.Build);
    }

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

        var view = View(compilation);

        foreach (var cycle in Backend.FindCycles(view.Nodes, view.Adjacency))
        {
            if (cycle.Count == 0) continue;
            var (file, line) = AttributeCycle(view.Edges, cycle);
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.CircularReference,
                $"引用图成环：{string.Join(" → ", cycle)} → {cycle[0]}", file, line));
        }

        foreach (var flow in compilation.Flows)
        {
            var steps = UsharpStepSequence.Keys(flow);
            if (steps.Count == 0) continue;

            var pairs = UsharpStepSequence.Pairs(steps);

            // 入口/终点按「无入边 / 无出边」判定：重复步骤键（循环流）不得使判定函数命中 >1。
            var hasIncoming = new HashSet<string>(StringComparer.Ordinal);
            var hasOutgoing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (source, target) in pairs)
            {
                hasOutgoing.Add(source);
                hasIncoming.Add(target);
            }

            var integrity = Backend.ValidateFlowIntegrity(
                flow.FlowId, steps, pairs,
                isEntry: node => !hasIncoming.Contains(node),
                isExit: node => !hasOutgoing.Contains(node));

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
        return Backend.Reachable(start, View(compilation).Adjacency);
    }

    /// <summary>两点是否可达。</summary>
    public bool HasPath(UsharpCompilation compilation, string source, string target)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return Backend.HasPath(source, target, View(compilation).Adjacency);
    }

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
