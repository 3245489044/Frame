using Plate.DslMethod;
using Plate.DslMethod.Logic;
using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// Usharp 编译门面：文本 → 语法模型 → 空间/引用图 → 闸A 诊断。装载体，不执行任何语义
/// （矩阵/流的运行语义由既有框架承担：ParameterMatrix / ChainSignalResolver / FoldEngine / ReferenceGraphEngine）。
/// <para>
/// <b>去硬编码（H1/H2/H3/H5 消除）</b>：本类只持有**泛型声明事实**（documents / 声明索引 / 符号索引 / 边 / 诊断）
/// 与 facet 注册表；一切领域视图由可注册的 <see cref="IUsharpFacetProvider"/> 惰性投影，经 <see cref="Facet{T}"/> 读取。
/// 旧属性保留为**薄访问器**（<c>=&gt; Facet&lt;X&gt;()</c>），签名与语义逐字不变（零回归）。
/// </para>
/// </summary>
public sealed class UsharpCompilation
{
    private readonly UsharpFacetRegistry _facets;

    private UsharpCompilation(
        IReadOnlyList<DslDocument> documents,
        UsharpDeclarationIndex declarations,
        DslIndex index,
        IReadOnlyList<UsharpEdge> edges,
        UsharpFacetRegistry facets,
        IReadOnlyList<UsharpDiagnostic> diagnostics)
    {
        Documents = documents;
        Declarations = declarations;
        Index = index;
        Edges = edges;
        _facets = facets;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<DslDocument> Documents { get; }

    /// <summary>声明索引（按 Kind 分组一次遍历；规则 / 发射 / 落库共用的单点）。</summary>
    public UsharpDeclarationIndex Declarations { get; }

    public DslIndex Index { get; }

    /// <summary>引用即边（<c>[Ref]</c>/<c>[CR]</c> 的统一图观）。</summary>
    public IReadOnlyList<UsharpEdge> Edges { get; }

    // ── 通用 facet 读取（替代逐字段属性）──────────────────────────────────────

    /// <summary>取 facet 视图（未注册 → 抛，不静默给空）。</summary>
    public T Facet<T>() where T : class => _facets.Facet<T>();

    /// <summary>取 facet 视图（未注册 → false）。</summary>
    public bool TryGetFacet<T>(out T facet) where T : class => _facets.TryGetFacet(out facet);

    // ── 薄访问器（签名与语义逐字不变：实现改为 Facet&lt;X&gt;()）──────────────

    public SpaceRegistry Spaces => Facet<SpaceRegistry>();
    public IReadOnlyList<UsharpMatrixDecl> Matrices => Facet<IReadOnlyList<UsharpMatrixDecl>>();
    public IReadOnlyList<UsharpFlowDecl> Flows => Facet<IReadOnlyList<UsharpFlowDecl>>();
    public IReadOnlyList<UsharpRefDecl> Refs => Facet<IReadOnlyList<UsharpRefDecl>>();

    /// <summary><c>[Domain]</c> 域定义声明（节点域定义面；U1；声明序）。</summary>
    public IReadOnlyList<UsharpDomainDecl> Domains => Facet<IReadOnlyList<UsharpDomainDecl>>();

    /// <summary><c>[Gather]</c> 聚拢声明（收集端口枚举；U2；声明序）。</summary>
    public IReadOnlyList<UsharpGatherDecl> Gathers => Facet<IReadOnlyList<UsharpGatherDecl>>();

    /// <summary><c>[Table]</c> 表结构声明（数据层：数据库表由声明派生；声明序）。</summary>
    public IReadOnlyList<UsharpTableDecl> Tables => Facet<IReadOnlyList<UsharpTableDecl>>();

    /// <summary><c>[Record]</c> 记录载荷声明（数据层：行/文档 USharp 化；声明序）。</summary>
    public IReadOnlyList<UsharpRecordDecl> Records => Facet<IReadOnlyList<UsharpRecordDecl>>();

    /// <summary><c>[KeySpace]</c> 键空间声明（键族/面/轴/端口/策略/回退链；声明序）。</summary>
    public IReadOnlyList<UsharpKeySpaceDecl> KeySpaces => Facet<IReadOnlyList<UsharpKeySpaceDecl>>();

    /// <summary><c>[Command]</c> 连接点命令声明（action/op 路由 + 键配方；声明序）。</summary>
    public IReadOnlyList<UsharpCommandDecl> Commands => Facet<IReadOnlyList<UsharpCommandDecl>>();

    /// <summary><c>[Config]</c> 配置声明（配置面：配置项由声明派生；声明序）。</summary>
    public IReadOnlyList<UsharpConfigDecl> Configs => Facet<IReadOnlyList<UsharpConfigDecl>>();

    /// <summary><c>[Rule]</c> 规则声明（规则面：条件 → 动作；声明序）。</summary>
    public IReadOnlyList<UsharpRuleDecl> Rules => Facet<IReadOnlyList<UsharpRuleDecl>>();

    /// <summary>观测声明注册中心（<c>[Obs]</c>：作用域 / 实体 / 事件；第五语言单元）。</summary>
    public ObsRegistry Obs => Facet<ObsRegistry>();

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
    private static int _compileCount;

    /// <summary>进程级编译计数（J1 可观测判据：启动期声明编译总次数；唯一编译点 = DeclaredBundleBuilder）。</summary>
    public static int CompileCount => System.Threading.Volatile.Read(ref _compileCount);

    /// <summary>清零编译计数（测试 / 验收取数前调用）。</summary>
    public static void ResetCompileCount() => System.Threading.Volatile.Write(ref _compileCount, 0);

    /// <summary>编译源集（便捷重载；每文件 lib = 指定 lib）。</summary>
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
        => CompileSources(sources, null);

    /// <summary>
    /// 编译源集（**去硬编码编译流**）：解析 → 泛型索引 → 符号索引 → facet 投影（packs）→ 引用即边 → 闸A。
    /// <para><paramref name="packs"/> 为 null/空 ⇒ 默认 <c>new[] { UsharpDomainPack.Instance }</c>（单包零回归）。</para>
    /// </summary>
    public static UsharpCompilation CompileSources(IReadOnlyList<UsharpSourceFile> sources, IReadOnlyList<IUsharpDomainPack>? packs)
    {
        ArgumentNullException.ThrowIfNull(sources);
        System.Threading.Interlocked.Increment(ref _compileCount);

        // ① 解析 + ③ 符号索引
        var documents = new List<DslDocument>();
        var index = new DslIndex();
        foreach (var source in sources)
        {
            var doc = UsharpParser.Parse(source.Text ?? "", source.Path ?? "(memory)", source.Lib ?? UsharpParser.DefaultLib);
            documents.Add(doc);
            index.AddDocument(doc);
        }

        // ② 泛型索引（kind 分组）
        var declarations = UsharpDeclarationIndex.Build(documents);

        var effectivePacks = packs is { Count: > 0 }
            ? packs
            : new IUsharpDomainPack[] { UsharpDomainPack.Instance };

        // ⑤ facet 投影（序 = 包声明序 × provider 序；诊断按注册序汇集，保既有 spaces → obs 归因序）
        var ctx = new UsharpFacetContext(documents, declarations, index);
        var facets = new UsharpFacetRegistry();
        foreach (var pack in effectivePacks)
            foreach (var provider in pack.FacetProviders)
            {
                var facet = provider.Project(ctx);
                facets.Register(provider.FacetType, facet);
                if (facet is IUsharpDiagnosticCarrier carrier)
                    facets.AddDiagnostics(carrier.Diagnostics);
            }

        // ④ 引用即边
        var edges = UsharpGraphBuilder.Build(
            documents,
            facets.Facet<IReadOnlyList<UsharpMatrixDecl>>(),
            facets.Facet<IReadOnlyList<UsharpFlowDecl>>(),
            facets.Facet<IReadOnlyList<UsharpRefDecl>>());

        // ⑥ 闸A + ⑦ 诊断投影
        var diagnostics = new List<UsharpDiagnostic>();
        diagnostics.AddRange(facets.Diagnostics);
        diagnostics.AddRange(UsharpValidator.Validate(documents, declarations, index, facets, effectivePacks));

        return new UsharpCompilation(documents, declarations, index, edges, facets, diagnostics);
    }

    /// <summary>本编译的规模快照（验收取数：空间坐标数 = 闸A 拦截面）。</summary>
    public (int Spaces, int Coordinates, int Matrices, int Flows, int Refs, int Edges, int Errors, int Warnings) Snapshot()
        => (Spaces.SpaceCount, Spaces.TotalCoordinates, Matrices.Count, Flows.Count, Refs.Count,
            Edges.Count, Errors.Count, Warnings.Count);

    /// <summary>诊断的单行报表（渲染单源 = <see cref="UsharpDiagnosticRenderer"/>）。</summary>
    public string Report() => UsharpDiagnosticRenderer.Render(Diagnostics);
}
