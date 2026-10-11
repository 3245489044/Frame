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
        UsharpDeclarationIndex declarations,
        DslIndex index,
        SpaceRegistry spaces,
        IReadOnlyList<UsharpMatrixDecl> matrices,
        IReadOnlyList<UsharpFlowDecl> flows,
        IReadOnlyList<UsharpRefDecl> refs,
        IReadOnlyList<UsharpEdge> edges,
        ObsRegistry obs,
        IReadOnlyList<UsharpDomainDecl> domains,
        IReadOnlyList<UsharpGatherDecl> gathers,
        IReadOnlyList<UsharpTableDecl> tables,
        IReadOnlyList<UsharpRecordDecl> records,
        IReadOnlyList<UsharpConfigDecl> configs,
        IReadOnlyList<UsharpRuleDecl> rules,
        IReadOnlyList<UsharpDiagnostic> diagnostics)
    {
        Documents = documents;
        Declarations = declarations;
        Index = index;
        Spaces = spaces;
        Matrices = matrices;
        Flows = flows;
        Refs = refs;
        Edges = edges;
        Obs = obs;
        Domains = domains;
        Gathers = gathers;
        Tables = tables;
        Records = records;
        Configs = configs;
        Rules = rules;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<DslDocument> Documents { get; }

    /// <summary>声明索引（按 Kind 分组一次遍历；规则 / 发射 / 落库共用的单点）。</summary>
    public UsharpDeclarationIndex Declarations { get; }

    public DslIndex Index { get; }
    public SpaceRegistry Spaces { get; }
    public IReadOnlyList<UsharpMatrixDecl> Matrices { get; }
    public IReadOnlyList<UsharpFlowDecl> Flows { get; }
    public IReadOnlyList<UsharpRefDecl> Refs { get; }
    public IReadOnlyList<UsharpEdge> Edges { get; }

    /// <summary><c>[Domain]</c> 域定义声明（节点域定义面；U1；声明序）。</summary>
    public IReadOnlyList<UsharpDomainDecl> Domains { get; }

    /// <summary><c>[Gather]</c> 聚拢声明（收集端口枚举；U2；声明序）。</summary>
    public IReadOnlyList<UsharpGatherDecl> Gathers { get; }

    /// <summary><c>[Table]</c> 表结构声明（数据层：数据库表由声明派生；声明序）。</summary>
    public IReadOnlyList<UsharpTableDecl> Tables { get; }

    /// <summary><c>[Record]</c> 记录载荷声明（数据层：行/文档 USharp 化；声明序）。</summary>
    public IReadOnlyList<UsharpRecordDecl> Records { get; }

    /// <summary><c>[Config]</c> 配置声明（配置面：配置项由声明派生；声明序）。</summary>
    public IReadOnlyList<UsharpConfigDecl> Configs { get; }

    /// <summary><c>[Rule]</c> 规则声明（规则面：条件 → 动作；声明序）。</summary>
    public IReadOnlyList<UsharpRuleDecl> Rules { get; }

    /// <summary>观测声明注册中心（<c>[Obs]</c>：作用域 / 实体 / 事件；第五语言单元）。</summary>
    public ObsRegistry Obs { get; }

    /// <summary><c>[Obs]</c> 声明（声明序；manifest 产出与书写面同序）。</summary>
    public IReadOnlyList<UsharpObsDecl> ObsDecls => Obs.Declarations;

    public IReadOnlyList<UsharpDiagnostic> Diagnostics { get; }

    /// <summary>是否含 error 诊断（判据单源 = <see cref="UsharpDiagnostics"/>）。</summary>
    public bool HasErrors => UsharpDiagnostics.HasError(Diagnostics);

    /// <summary>error 诊断（声明序；判据单源 = <see cref="UsharpDiagnostics"/>）。</summary>
    public IReadOnlyList<UsharpDiagnostic> Errors => UsharpDiagnostics.Errors(Diagnostics);

    /// <summary>警告诊断（声明序；判据单源 = <see cref="UsharpDiagnostics"/>）。</summary>
    public IReadOnlyList<UsharpDiagnostic> Warnings => UsharpDiagnostics.Warnings(Diagnostics);

    /// <summary>编译一组内存源（同一 lib；path 仅用于诊断归因）。</summary>
    public static UsharpCompilation Compile(IEnumerable<(string Path, string Text)> sources, string lib = UsharpParser.DefaultLib)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return CompileSources(sources.Select(s => new UsharpSourceFile(s.Path, s.Text ?? "", lib)).ToList());
    }

    /// <summary>
    /// 编译源集（**每文件 lib 独立**，跨 lib 的 <c>[CR]</c> 解析口径与加载面一致）。
    /// 加载一律经 <see cref="UsharpSourceLoader"/>（路径 → 源集的单一实现）。
    /// </summary>
    public static UsharpCompilation CompileSources(IReadOnlyList<UsharpSourceFile> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var documents = new List<DslDocument>();
        var index = new DslIndex();
        var diagnostics = new List<UsharpDiagnostic>();

        foreach (var source in sources)
        {
            var doc = UsharpParser.Parse(source.Text ?? "", source.Path ?? "(memory)", source.Lib ?? UsharpParser.DefaultLib);
            documents.Add(doc);
            index.AddDocument(doc);
        }

        var declarations = UsharpDeclarationIndex.Build(documents);

        var spaces = new SpaceRegistry();
        var obs = new ObsRegistry();
        var matrices = new List<UsharpMatrixDecl>();
        var flows = new List<UsharpFlowDecl>();
        var refs = new List<UsharpRefDecl>();
        var domains = new List<UsharpDomainDecl>();
        var gathers = new List<UsharpGatherDecl>();
        var tables = new List<UsharpTableDecl>();
        var records = new List<UsharpRecordDecl>();
        var configs = new List<UsharpConfigDecl>();
        var rules = new List<UsharpRuleDecl>();

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
            domains.AddRange(UsharpSyntaxProjector.Domains(doc));
            gathers.AddRange(UsharpSyntaxProjector.Gathers(doc));
            tables.AddRange(UsharpSyntaxProjector.Tables(doc));
            records.AddRange(UsharpSyntaxProjector.Records(doc));
            configs.AddRange(UsharpSyntaxProjector.Configs(doc));
            rules.AddRange(UsharpSyntaxProjector.Rules(doc));
        }

        var edges = UsharpGraphBuilder.Build(documents, matrices, flows, refs);
        diagnostics.AddRange(UsharpValidator.Validate(documents, declarations, index, spaces, matrices, flows, refs, obs, domains, gathers));

        return new UsharpCompilation(documents, declarations, index, spaces, matrices, flows, refs, edges, obs, domains, gathers, tables, records, configs, rules, diagnostics);
    }

    /// <summary>本编译的规模快照（验收取数：空间坐标数 = 闸A 拦截面）。</summary>
    public (int Spaces, int Coordinates, int Matrices, int Flows, int Refs, int Edges, int Errors, int Warnings) Snapshot()
        => (Spaces.SpaceCount, Spaces.TotalCoordinates, Matrices.Count, Flows.Count, Refs.Count,
            Edges.Count, Errors.Count, Warnings.Count);

    /// <summary>诊断的单行报表（渲染单源 = <see cref="UsharpDiagnosticRenderer"/>）。</summary>
    public string Report() => UsharpDiagnosticRenderer.Render(Diagnostics);
}
