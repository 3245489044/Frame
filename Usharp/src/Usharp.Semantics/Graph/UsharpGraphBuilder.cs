using Plate.DslMethod;
using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>引用图构建（规则 R1「引用即边」）：把四类声明与 [CR] 引用一律落为 <see cref="UsharpEdge"/>。</summary>
public static class UsharpGraphBuilder
{
    /// <summary>边类型常量（内建三型；[Ref] 可用 <c>edge=</c> 覆盖为 [Edge] 声明的引用类型）。</summary>
    public const string Over = "Over";

    public const string StepRef = "StepRef";

    public const string Invokes = "Invokes";

    public const string CrRef = "CrRef";

    public const string RefersTo = "RefersTo";

    /// <summary>矩阵行边（行内每个坐标引用也是一条引用边 —— 规则 R1 覆盖矩阵运算面）。</summary>
    public const string RowRef = "RowRef";

    public static IReadOnlyList<UsharpEdge> Build(
        IReadOnlyList<DslDocument> documents,
        IReadOnlyList<UsharpMatrixDecl> matrices,
        IReadOnlyList<UsharpFlowDecl> flows,
        IReadOnlyList<UsharpRefDecl> refs)
    {
        var edges = new List<UsharpEdge>();

        // ① [Ref] → 引用边（src→tgt；无 src/tgt 时以声明名/坐标键自洽）
        foreach (var r in refs)
            edges.Add(new UsharpEdge(
                r.Edge ?? r.Intent ?? RefersTo,
                r.Source ?? r.Name,
                r.Target ?? r.Key,
                string.IsNullOrEmpty(r.SpaceId) ? null : r.SpaceId,
                string.IsNullOrEmpty(r.Key) ? null : r.Key,
                r.SourceFile, r.LineNumber));

        // ② [Matrix] → 空间边 + 行内坐标引用边（行引用即边）
        foreach (var m in matrices)
        {
            edges.Add(new UsharpEdge(Over, m.MatrixId, m.OverSpaceId ?? "", m.OverSpaceId, null,
                m.SourceFile, m.LineNumber));
            foreach (var row in m.Rows)
                foreach (var input in row.InputRefs)
                    edges.Add(new UsharpEdge(RowRef, $"{m.MatrixId}.{row.OutputKey}", input,
                        m.OverSpaceId, input, m.SourceFile, m.LineNumber));
        }

        // ③ [Flow] → 步边（StepRef）与子链边（Invokes；工作流连接链）
        foreach (var f in flows)
            foreach (var step in f.Steps)
            {
                edges.Add(new UsharpEdge(StepRef, f.FlowId, step.Key, f.OverSpaceId, step.Key,
                    f.SourceFile, f.LineNumber));
                if (step.SubFlow is not null)
                    edges.Add(new UsharpEdge(Invokes, f.FlowId, step.SubFlow, null, null,
                        f.SourceFile, f.LineNumber));
            }

        // ④ [CR] 交叉引用 → 符号边（含全部声明种类；目标原文由 DslIndex 解析）
        foreach (var doc in documents)
            foreach (var decl in doc.Declarations)
                foreach (var r in decl.References)
                    edges.Add(new UsharpEdge(CrRef, decl.Name, r.Raw, null, null, decl.SourceFile, decl.LineNumber));

        return edges;
    }
}
