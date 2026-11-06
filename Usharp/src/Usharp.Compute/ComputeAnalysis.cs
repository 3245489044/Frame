using System.Globalization;
using Plate.DslMethod.Model;

namespace Usharp.Compute;

/// <summary>一个已通过头/体解析的计算单元（公共只读模型）。</summary>
public sealed class ComputeUnit
{
    public ComputeUnit(ComputeUnitHeader header, IReadOnlyList<ComputeStatement> body,
        DslDeclaration declaration)
    {
        Header = header;
        Body = body;
        Declaration = declaration;
    }

    public ComputeUnitHeader Header { get; }
    public IReadOnlyList<ComputeStatement> Body { get; }
    public DslDeclaration Declaration { get; }
    public string CalcId => Header.CalcId;
}

public sealed class ComputeAnalysis
{
    public ComputeAnalysis(IReadOnlyList<ComputeUnit> units, IReadOnlyList<ComputeDiagnostic> diagnostics,
        IReadOnlyList<ComputeProgram> programs, IReadOnlyDictionary<string, IReadOnlyList<string>> callGraph)
    {
        Units = units;
        Diagnostics = diagnostics;
        Programs = programs;
        CallGraph = callGraph;
    }

    public IReadOnlyList<ComputeUnit> Units { get; }
    public IReadOnlyList<ComputeDiagnostic> Diagnostics { get; }
    public IReadOnlyList<ComputeProgram> Programs { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> CallGraph { get; }

    public ComputeProgram? FindProgram(string calcId)
        => Programs.FirstOrDefault(p => string.Equals(p.RootId, calcId, StringComparison.Ordinal));
}

public static class ComputeAnalysisRunner
{
    public static ComputeAnalysis Run(IReadOnlyList<DslDocument> documents,
        Func<string, string, bool>? coordinateExists = null)
    {
        var diagnostics = new List<ComputeDiagnostic>();
        var units = new List<ComputeUnit>();
        var headers = new Dictionary<string, ComputeUnitHeader>(StringComparer.Ordinal);

        foreach (var document in documents)
        {
            foreach (var declaration in document.Declarations)
            {
                if (!UsharpComputeClassifier.IsComputeUnit(declaration)) continue;

                if (!ComputeHeaderParser.TryParse(declaration, out var header, out var headerErrors))
                {
                    diagnostics.AddRange(headerErrors);
                    continue;
                }

                if (!ComputeBodyParser.TryParse(document.FilePath, declaration.Continuation,
                        declaration.LineNumber + 1, out var body, out var bodyErrors))
                {
                    diagnostics.AddRange(bodyErrors);
                    continue;
                }

                if (headers.ContainsKey(header!.CalcId))
                {
                    diagnostics.Add(new ComputeDiagnostic(ComputeCodes.BindingDuplicate,
                        $"计算单元 '{header.CalcId}' 重复声明", header.SourceFile, header.LineNumber));
                    continue;
                }

                headers[header.CalcId] = header;
                units.Add(new ComputeUnit(header, body, declaration));
            }
        }

        if (units.Count == 0)
        {
            return new ComputeAnalysis(Array.Empty<ComputeUnit>(), diagnostics, Array.Empty<ComputeProgram>(),
                new Dictionary<string, IReadOnlyList<string>>());
        }

        var callGraph = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var lowered = new Dictionary<string, ComputeUnitIr>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            var result = new ComputeLowering(unit, headers, coordinateExists).Run();
            diagnostics.AddRange(result.Diagnostics);
            callGraph[unit.CalcId] = result.Callees;
            if (result.Program is not null) lowered[unit.CalcId] = result.Program;
        }

        ReportCycles(callGraph, headers, diagnostics);
        ReportBudget(units, callGraph, diagnostics);
        ReportShare(documents, units, diagnostics);

        var programs = new List<ComputeProgram>();
        if (diagnostics.Count == 0)
        {
            foreach (var unit in units)
            {
                programs.Add(BuildProgram(unit.CalcId, callGraph, lowered));
            }
        }

        return new ComputeAnalysis(units, diagnostics, programs, callGraph);
    }

    private static void ReportCycles(IReadOnlyDictionary<string, IReadOnlyList<string>> graph,
        IReadOnlyDictionary<string, ComputeUnitHeader> headers, List<ComputeDiagnostic> diagnostics)
    {
        var inCycle = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var root in graph.Keys)
        {
            Visit(root);
        }

        foreach (var calcId in inCycle.OrderBy(id => headers[id].LineNumber))
        {
            var header = headers[calcId];
            diagnostics.Add(new ComputeDiagnostic(ComputeCodes.RecursionNotEnabled,
                $"调用图存在环，{calcId} 属于递归强连通分量（递归属第 4 步，本阶段未启用）",
                header.SourceFile, header.LineNumber));
        }

        return;

        void Visit(string node)
        {
            if (visited.Contains(node)) return;
            if (!visiting.Add(node))
            {
                var index = path.IndexOf(node);
                if (index >= 0)
                {
                    foreach (var item in path.Skip(index)) inCycle.Add(item);
                }

                return;
            }

            path.Add(node);
            foreach (var next in graph.GetValueOrDefault(node, Array.Empty<string>()))
            {
                if (graph.ContainsKey(next)) Visit(next);
            }

            path.RemoveAt(path.Count - 1);
            visiting.Remove(node);
            visited.Add(node);
        }
    }

    private static void ReportBudget(IReadOnlyList<ComputeUnit> units,
        IReadOnlyDictionary<string, IReadOnlyList<string>> graph, List<ComputeDiagnostic> diagnostics)
    {
        foreach (var unit in units)
        {
            var hasLoop = ContainsLoop(unit.Body);
            var cyclic = graph.TryGetValue(unit.CalcId, out var callees)
                         && callees.Contains(unit.CalcId, StringComparer.Ordinal);
            if (!hasLoop && !cyclic) continue;
            if (unit.Header.Budget.HasValue) continue;
            diagnostics.Add(new ComputeDiagnostic(ComputeCodes.BudgetMissing,
                $"含 loop 或调用图成环的单元 '{unit.CalcId}' 必须声明 budget", unit.Header.SourceFile,
                unit.Header.LineNumber));
        }
    }

    private static bool ContainsLoop(IReadOnlyList<ComputeStatement> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case ComputeLoopStatement:
                    return true;
                case ComputeIfStatement branch:
                    if (ContainsLoop(branch.Then)) return true;
                    if (branch.Else is not null && ContainsLoop(branch.Else)) return true;
                    break;
            }
        }

        return false;
    }

    private static void ReportShare(IReadOnlyList<DslDocument> documents, IReadOnlyList<ComputeUnit> units,
        List<ComputeDiagnostic> diagnostics)
    {
        var total = documents.Sum(d => d.Declarations.Count);
        if (total == 0) return;
        var share = (decimal)units.Count / total;
        foreach (var unit in units)
        {
            if (!unit.Header.ShareMax.HasValue) continue;
            if (share <= unit.Header.ShareMax.Value) continue;
            diagnostics.Add(new ComputeDiagnostic(ComputeCodes.ShareExceeded,
                $"计算单元占比 {share:0.####} 超过 share_max={unit.Header.ShareMax.Value}",
                unit.Header.SourceFile, unit.Header.LineNumber));
        }
    }

    private static ComputeProgram BuildProgram(string rootId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> graph,
        IReadOnlyDictionary<string, ComputeUnitIr> lowered)
    {
        var closure = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!closure.Add(current)) continue;
            foreach (var next in graph.GetValueOrDefault(current, Array.Empty<string>()))
            {
                if (lowered.ContainsKey(next)) queue.Enqueue(next);
            }
        }

        var units = closure.Where(lowered.ContainsKey).Select(id => lowered[id])
            .OrderBy(u => u.CalcId, StringComparer.Ordinal).ToList();
        var program = new ComputeProgram(rootId, units, "");
        return new ComputeProgram(rootId, units, ComputeProgramCodec.ComputeHash(program));
    }
}

public sealed class ComputeCompileResult
{
    public ComputeCompileResult(IReadOnlyList<ComputeUnit> units, IReadOnlyList<ComputeDiagnostic> diagnostics,
        IReadOnlyList<ComputeProgram> programs)
    {
        Units = units;
        Diagnostics = diagnostics;
        Programs = programs;
    }

    public IReadOnlyList<ComputeUnit> Units { get; }
    public IReadOnlyList<ComputeDiagnostic> Diagnostics { get; }
    public IReadOnlyList<ComputeProgram> Programs { get; }
    public bool Succeeded => Diagnostics.Count == 0;

    public ComputeProgram? FindProgram(string calcId)
        => Programs.FirstOrDefault(p => string.Equals(p.RootId, calcId, StringComparison.Ordinal));
}

public static class UsharpComputeCompiler
{
    /// <summary>编译闭包内的全部 [Compute] 单元；有错误 ⇒ 不产出任何程序（不留半成品 JSON）。</summary>
    public static ComputeCompileResult Compile(IReadOnlyList<DslDocument> documents,
        Func<string, string, bool>? coordinateExists = null)
    {
        var analysis = ComputeAnalysisRunner.Run(documents, coordinateExists);
        var diagnostics = analysis.Diagnostics.ToList();
        if (diagnostics.Count == 0 && analysis.Programs.Count == 0 && analysis.Units.Count > 0)
        {
            diagnostics.Add(new ComputeDiagnostic(ComputeCodes.ProgramNotProducible,
                "计算单元存在但无法产出任何程序", analysis.Units[0].Header.SourceFile,
                analysis.Units[0].Header.LineNumber));
        }

        return new ComputeCompileResult(analysis.Units, diagnostics,
            diagnostics.Count == 0 ? analysis.Programs : Array.Empty<ComputeProgram>());
    }
}
