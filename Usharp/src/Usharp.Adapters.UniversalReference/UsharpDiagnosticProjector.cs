using UniversalWorkflowFramework.Core.Diagnostics;
using Usharp.Semantics;

namespace Usharp.Adapters.UniversalReference;

/// <summary>
/// 诊断投影（**单向**）：<see cref="UsharpDiagnostic"/>（结构化、含 <c>文件:行</c>）→ UR <see cref="DiagnosticResult"/>（字符串报表）。
/// <para>禁反向：<c>DiagnosticResult</c> 无可解析结构，不得作为闸A 判据，也不得再解析回结构化。</para>
/// </summary>
public static class UsharpDiagnosticProjector
{
    /// <summary>逐条投影（计数由调用方给出：节点数 / 边数 / 环数）。</summary>
    public static DiagnosticResult ToDiagnosticResult(
        IEnumerable<UsharpDiagnostic> diagnostics,
        int totalItems = 0,
        int totalReferences = 0,
        int circularReferences = 0)
    {
        var result = new DiagnosticResult
        {
            TotalItems = totalItems,
            TotalReferences = totalReferences,
            CircularReferences = circularReferences,
        };

        foreach (var d in diagnostics ?? Enumerable.Empty<UsharpDiagnostic>()) Add(result, d);
        return result;
    }

    /// <summary>把编译诊断与图诊断合并为一份对外报表（计数取编译产物取数）。</summary>
    public static DiagnosticResult Project(
        UsharpCompilation compilation, IEnumerable<UsharpDiagnostic>? graphDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var graph = graphDiagnostics?.ToList() ?? new List<UsharpDiagnostic>();
        var edges = UsharpEdgeSelection.GraphEdges(compilation);

        return ToDiagnosticResult(
            compilation.Diagnostics.Concat(graph),
            totalItems: UsharpEdgeSelection.Nodes(edges).Count,
            totalReferences: edges.Count,
            circularReferences: graph.Count(d => d.Code == UsharpCodes.CircularReference));
    }

    /// <summary>单条投影（Error/Warning/Info → 对应列表，文案含 <c>文件:行</c>）。</summary>
    public static void Add(DiagnosticResult target, UsharpDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(target);

        var text = $"{diagnostic.Code} {diagnostic.Message} ({diagnostic.SourceFile}:{diagnostic.LineNumber})";
        switch (diagnostic.Severity)
        {
            case UsharpSeverity.Error:
                target.AddError(text);
                break;
            case UsharpSeverity.Warning:
                target.AddWarning(text);
                break;
            default:
                target.AddInfo(text);
                break;
        }
    }
}
