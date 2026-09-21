using Plate.DslMethod;
using Plate.DslMethod.Logic;
using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// Usharp 编译门面：文本 → 语法模型 → 空间/引用图 → 闸A 诊断。装载体，不执行任何语义
/// （矩阵/流的运行语义由既有框架承担：ParameterMatrix / ChainSignalResolver / FoldEngine / ReferenceGraphEngine）。
/// </summary>
public sealed class UsharpCompilation
{
    private UsharpCompilation(
        IReadOnlyList<DslDocument> documents,
        DslIndex index,
        SpaceRegistry spaces,
        IReadOnlyList<UsharpMatrixDecl> matrices,
        IReadOnlyList<UsharpFlowDecl> flows,
        IReadOnlyList<UsharpRefDecl> refs,
        IReadOnlyList<UsharpEdge> edges,
        ObsRegistry obs,
        IReadOnlyList<UsharpDiagnostic> diagnostics)
    {
        Documents = documents;
        Index = index;
        Spaces = spaces;
        Matrices = matrices;
        Flows = flows;
        Refs = refs;
        Edges = edges;
        Obs = obs;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<DslDocument> Documents { get; }
    public DslIndex Index { get; }
    public SpaceRegistry Spaces { get; }
    public IReadOnlyList<UsharpMatrixDecl> Matrices { get; }
    public IReadOnlyList<UsharpFlowDecl> Flows { get; }
    public IReadOnlyList<UsharpRefDecl> Refs { get; }
    public IReadOnlyList<UsharpEdge> Edges { get; }

    /// <summary>观测声明注册中心（<c>[Obs]</c>：作用域 / 实体 / 事件；第五语言单元）。</summary>
    public ObsRegistry Obs { get; }

    /// <summary><c>[Obs]</c> 声明（声明序；manifest 产出与书写面同序）。</summary>
    public IReadOnlyList<UsharpObsDecl> ObsDecls => Obs.Declarations;

    public IReadOnlyList<UsharpDiagnostic> Diagnostics { get; }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == UsharpSeverity.Error);

    public IReadOnlyList<UsharpDiagnostic> Errors
        => Diagnostics.Where(d => d.Severity == UsharpSeverity.Error).ToList();

    public IReadOnlyList<UsharpDiagnostic> Warnings
        => Diagnostics.Where(d => d.Severity == UsharpSeverity.Warning).ToList();

    /// <summary>编译一组内存源（path 仅用于诊断归因与 lib 归属）。</summary>
    public static UsharpCompilation Compile(IEnumerable<(string Path, string Text)> sources, string lib = UsharpParser.DefaultLib)
    {
        var documents = new List<DslDocument>();
        var index = new DslIndex();
        var diagnostics = new List<UsharpDiagnostic>();

        foreach (var (path, text) in sources)
        {
            var doc = UsharpParser.Parse(text ?? "", path ?? "(memory)", lib);
            documents.Add(doc);
            index.AddDocument(doc);
        }

        var spaces = new SpaceRegistry();
        var obs = new ObsRegistry();
        var matrices = new List<UsharpMatrixDecl>();
        var flows = new List<UsharpFlowDecl>();
        var refs = new List<UsharpRefDecl>();

        // 两遍：先登记全部空间与观测声明（闸A 判据），再投影其余声明。
        foreach (var doc in documents)
            foreach (var s in UsharpSyntaxProjector.Spaces(doc))
                spaces.Add(s);
        diagnostics.AddRange(spaces.Diagnostics);

        foreach (var doc in documents)
            foreach (var o in UsharpSyntaxProjector.Obs(doc))
                obs.Add(o);
        diagnostics.AddRange(obs.Diagnostics);

        foreach (var doc in documents)
        {
            matrices.AddRange(UsharpSyntaxProjector.Matrices(doc));
            flows.AddRange(UsharpSyntaxProjector.Flows(doc));
            refs.AddRange(UsharpSyntaxProjector.Refs(doc));
        }

        var edges = UsharpGraphBuilder.Build(documents, matrices, flows, refs);
        diagnostics.AddRange(UsharpValidator.Validate(documents, index, spaces, matrices, flows, refs, obs));

        return new UsharpCompilation(documents, index, spaces, matrices, flows, refs, edges, obs, diagnostics);
    }

    /// <summary>编译一组 Usharp 文件（缺省 lib = <see cref="UsharpParser.DefaultLib"/>；lib 相同者可互相解析 [CR]）。</summary>
    public static UsharpCompilation CompileFiles(IEnumerable<(string Path, string? Lib)> files)
        => Compile(files.Select(f => (f.Path, File.ReadAllText(f.Path))));

    /// <summary>闸A 查询：坐标 (spaceId, key) 是否已声明。</summary>
    public bool TryResolveCoordinate(string spaceId, string key, out UsharpAxis axis)
        => Spaces.HasCoordinate(spaceId, key, out axis);

    /// <summary>本编译的规模快照（验收取数：空间坐标数 = 闸A 拦截面）。</summary>
    public (int Spaces, int Coordinates, int Matrices, int Flows, int Refs, int Edges, int Errors, int Warnings) Snapshot()
        => (Spaces.SpaceCount, Spaces.TotalCoordinates, Matrices.Count, Flows.Count, Refs.Count,
            Edges.Count, Errors.Count, Warnings.Count);

    /// <summary>诊断的单行报表（对齐 linter 输出风格）。</summary>
    public string Report()
        => Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join(Environment.NewLine, Diagnostics.Select(d => d.ToString()));
}
