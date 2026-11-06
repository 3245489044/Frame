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

        var effectiveEffects = PropagateEffects(units, headers, callGraph, diagnostics);
        foreach (var id in lowered.Keys.ToList())
        {
            lowered[id] = lowered[id].WithEffects(effectiveEffects[id]);
        }

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

    private static readonly ComputeEffect[] EffectOrder =
    {
        ComputeEffect.KeyRead, ComputeEffect.KeyWrite, ComputeEffect.Emit,
        ComputeEffect.Clock, ComputeEffect.Random
    };

    /// <summary>
    /// effects 沿调用图做不动点传播：调用者必须声明被调方（含间接）的全部效果。
    /// 传播只用于「调用者必须声明」的静态校验；运行时仍按执行帧单元自身的效果集判定。
    /// </summary>
    private static Dictionary<string, IReadOnlyList<ComputeEffect>> PropagateEffects(
        IReadOnlyList<ComputeUnit> units, IReadOnlyDictionary<string, ComputeUnitHeader> headers,
        IReadOnlyDictionary<string, IReadOnlyList<string>> graph, List<ComputeDiagnostic> diagnostics)
    {
        var declared = new Dictionary<string, IReadOnlyList<ComputeEffect>>(StringComparer.Ordinal);
        var effective = new Dictionary<string, IReadOnlyList<ComputeEffect>>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            declared[unit.CalcId] = unit.Header.Effects;
            effective[unit.CalcId] = unit.Header.Effects;
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var unit in units)
            {
                var set = new HashSet<ComputeEffect>(declared[unit.CalcId]);
                foreach (var callee in graph.GetValueOrDefault(unit.CalcId, Array.Empty<string>()))
                {
                    var calleeEffects = effective.GetValueOrDefault(callee);
                    if (calleeEffects is not null) set.UnionWith(calleeEffects);
                }

                var expanded = EffectOrder.Where(set.Contains).ToList();
                if (!expanded.SequenceEqual(effective[unit.CalcId]))
                {
                    effective[unit.CalcId] = expanded;
                    changed = true;
                }
            }
        }

        foreach (var unit in units)
        {
            var missing = effective[unit.CalcId]
                .Where(effect => !declared[unit.CalcId].Contains(effect))
                .ToList();
            if (missing.Count == 0) continue;

            var header = headers[unit.CalcId];
            var text = string.Join(", ", missing.Select(ComputeProgramCodec.EffectText));
            if (header.Purity == ComputePurityPolicy.Strict)
            {
                diagnostics.Add(new ComputeDiagnostic(ComputeCodes.PurityInvalid,
                    $"pure=strict 单元 '{unit.CalcId}' 沿调用图传播了效果 [{text}]（strict 不得有任何效果）",
                    header.SourceFile, header.LineNumber));
            }
            else
            {
                diagnostics.Add(new ComputeDiagnostic(ComputeCodes.EffectUndeclared,
                    $"单元 '{unit.CalcId}' 未声明沿调用图传播的效果 [{text}]（effects 必须覆盖被调单元）",
                    header.SourceFile, header.LineNumber));
            }
        }

        return effective;
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
