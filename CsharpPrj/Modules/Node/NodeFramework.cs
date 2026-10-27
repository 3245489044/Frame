using Modules.Node.Carriers;
using Modules.Node.Diagnostics;
using Modules.Node.Governance;
using Modules.Node.Identity;
using Modules.Node.Instances;
using Modules.Node.Materialization;
using Modules.Node.Materialization.WritePlan;
using Modules.Node.Placement;
using Modules.Node.Ports;
using Modules.Node.Projection;
using Modules.Node.Spec;

namespace Modules.Node;

/// <summary>组合根选项：未给出的端口一律回退 Null 实现 + 可见化诊断（不静默、不假装通过）。</summary>
public sealed record NodeFrameworkOptions
{
    public string FrameworkId { get; init; } = "node-framework";

    /// <summary>本框架所属域键（按声明装配时给出）：运行域校验的身份域来源（免调用方在写请求里重复给域）。</summary>
    public string? Domain { get; init; }

    public IdentitySpec? Identity { get; init; }

    public PlacementRegistry? Placements { get; init; }

    public ICarrierRegistry? Carriers { get; init; }

    public IPlacementResolver? PlacementResolver { get; init; }

    public ISequenceAuthority? SequenceAuthority { get; init; }

    /// <summary>运行域供体（运行态身份的唯一来源；未提供 ⇒ Null 实现 ⇒ 不校验身份，行为与既有逐字一致）。</summary>
    public INodeRunScopeSource? RunScope { get; init; }

    /// <summary>是否要求运行域存在（默认 false：缺失不拒写，仅"有则校验"；由运行态面域按需开启）。</summary>
    public bool RequireRunScope { get; init; }

    public ISpecSource? SpecSource { get; init; }

    public IOrientationAuthority? OrientationAuthority { get; init; }

    public IReferenceIndexPort? ReferenceIndex { get; init; }

    public IDefinitionDiagnostics? DefinitionDiagnostics { get; init; }

    public SelectionSet? Selections { get; init; }

    /// <summary>词表端口（U3）：<c>all</c> 选择名来源；未提供 ⇒ Null（回退既有字面量）。</summary>
    public ISelectionVocabulary? SelectionVocabulary { get; init; }

    public NodeFormCodec? FormCodec { get; init; }

    public SpecDefaults? Defaults { get; init; }

    public TimeSpan? WriteGateTimeout { get; init; }

    public INodeDiagnosticsSink? Diagnostics { get; init; }

    /// <summary>读侧选择/预算声明（缺省 = 声明面 <see cref="NodeFrameworkSpec.Selection"/>）。</summary>
    public NodeSelectionSpec? Selection { get; init; }

    /// <summary>token 估算端口（未提供 ⇒ 预算只执行条数上限 + 可见化）。</summary>
    public ITokenEstimator? TokenEstimator { get; init; }

    /// <summary>写介质处理器集（未提供 ⇒ <see cref="NodeFramework.WritePlans"/> 为 null）。</summary>
    public IEnumerable<IWriteMediumHandler>? WriteMedia { get; init; }

    /// <summary>容器声明集（缺省 = 空注册表）。</summary>
    public IEnumerable<NodeContainerSpec>? Containers { get; init; }

    /// <summary>执行声明集（只声明不执行）。</summary>
    public IEnumerable<NodeExecutionBinding>? ExecutionBindings { get; init; }

    /// <summary>标签词表注册表（未提供 ⇒ 空实例注册表）。</summary>
    public ITagVocabularyRegistry? TagVocabularies { get; init; }

    /// <summary>身份坐标绑定（缺省 = 空：未声明 coordinates 的域零回归）。</summary>
    public IReadOnlyList<ContextCoordinateSpec>? Coordinates { get; init; }
}

/// <summary>
/// 节点框架组合根：装配身份 / 位置 / 载体 / 声明 / 物化 / 读投影 / 对账，并把未装配端口显式化。
/// 框架内唯一写入口 = <see cref="Materializer"/>；本类不做业务语义、不直连库、无进程静态状态。
/// </summary>
public sealed class NodeFramework
{
    private readonly NodeFrameworkDiagnostics? _collector;

    public NodeFramework(NodeFrameworkOptions? options = null)
    {
        var opt = options ?? new NodeFrameworkOptions();
        FrameworkId = opt.FrameworkId;
        Domain = opt.Domain;

        _collector = opt.Diagnostics is null ? new NodeFrameworkDiagnostics() : null;
        Diagnostics = opt.Diagnostics ?? _collector!;

        Identity = opt.Identity ?? IdentitySpec.Create("{key}");
        Coordinates = opt.Coordinates ?? Array.Empty<ContextCoordinateSpec>();
        if (opt.Identity is null)
        {
            Diagnostics.Report(new NodeDiagnostic(
                "identity.default",
                NodeDiagnosticSeverity.Info,
                "未提供身份规范：回退为 {key}（正式装配须来自声明）",
                FrameworkId));
        }

        Placements = opt.Placements ?? new PlacementRegistry();
        Carriers = opt.Carriers ?? new CarrierRegistry();
        SequenceAuthority = opt.SequenceAuthority ?? new NullSequenceAuthority(Diagnostics);
        RunScope = opt.RunScope ?? NullNodeRunScopeSource.Instance;
        RequireRunScope = opt.RequireRunScope;
        SpecSource = opt.SpecSource ?? new NullSpecSource(Diagnostics);
        OrientationAuthority = opt.OrientationAuthority ?? NullOrientationAuthority.Instance;
        ReferenceIndex = opt.ReferenceIndex ?? new NullReferenceIndexPort(Diagnostics);
        DefinitionDiagnostics = opt.DefinitionDiagnostics ?? NullDefinitionDiagnostics.Instance;
        Selections = opt.Selections ?? new SelectionSet(Diagnostics, opt.SelectionVocabulary);
        SelectionVocabulary = opt.SelectionVocabulary ?? NullSelectionVocabulary.Instance;
        FormCodec = opt.FormCodec ?? new NodeFormCodec();
        Defaults = opt.Defaults ?? new SpecDefaults();
        PlacementResolver = opt.PlacementResolver ?? new NullPlacementResolver(Diagnostics);

        WriteGate = new NodeWriteGate($"{FrameworkId}:gate", opt.WriteGateTimeout ?? TimeSpan.Zero, Diagnostics);
        Materializer = new NodeMaterializer(
            Identity, Placements, Carriers, WriteGate, FormCodec, Defaults, Diagnostics, RunScope, RequireRunScope,
            Domain);
        Projection = new NodeReadProjection(
            Placements, Carriers, Selections, FormCodec, Diagnostics, PlacementResolver, SelectionVocabulary);
        Reconciler = new NodeReconciler(Placements, Carriers, Diagnostics);
        Compensation = new NodeCompensation(Carriers, Diagnostics);
        Validator = new NodeSpecValidator(Carriers, Diagnostics);
        ReferenceIndexImpl = new NodeReferenceIndex(ReferenceIndex.ReferenceFields, Diagnostics);
        ChainComposer = new NodeChainComposer(ReferenceIndexImpl, Diagnostics);
        Directives = new NodeDirectiveSet(Diagnostics);
        PortBinding = new NodePortBinding(Diagnostics);

        SelectionSpec = opt.Selection;
        TokenEstimator = opt.TokenEstimator;
        Selection = new NodeSelectionService(Projection, opt.Selection, opt.TokenEstimator, Diagnostics);
        WritePlans = opt.WriteMedia is null
            ? null
            : new NodeWritePlanExecutor(opt.WriteMedia, WriteGate, diagnostics: Diagnostics);
        Containers = new NodeContainerRegistry(opt.Containers, Diagnostics);
        Artifacts = new NodeArtifactAssembler(diagnostics: Diagnostics);
        ExecutionBindings = (opt.ExecutionBindings ?? Array.Empty<NodeExecutionBinding>()).ToArray();
        AttributeInheritance = new AttributeInheritance(Diagnostics);
        TagVocabularies = opt.TagVocabularies ?? new TagVocabularyRegistry();
    }

    public string FrameworkId { get; }

    /// <summary>本框架所属域键（未按声明装配 ⇒ <c>null</c>；运行域校验退回写请求给定的域）。</summary>
    public string? Domain { get; }

    public INodeDiagnosticsSink Diagnostics { get; }

    /// <summary>内建诊断收集器（未显式注入 sink 时可用；否则为 null）。</summary>
    public NodeFrameworkDiagnostics? Collector => _collector;

    public IdentitySpec Identity { get; }

    /// <summary>身份坐标绑定（声明面 <c>[Domain] coordinates</c>；空 = 未声明，零回归）。</summary>
    public IReadOnlyList<ContextCoordinateSpec> Coordinates { get; }

    public PlacementRegistry Placements { get; }

    public ICarrierRegistry Carriers { get; }

    public IPlacementResolver PlacementResolver { get; }

    public ISequenceAuthority SequenceAuthority { get; }

    /// <summary>运行域供体（未装配 ⇒ <see cref="NullNodeRunScopeSource"/>：身份校验退化为 no-op）。</summary>
    public INodeRunScopeSource RunScope { get; }

    /// <summary>是否要求运行域存在（由宿主按域开启；默认 false）。</summary>
    public bool RequireRunScope { get; }

    public ISpecSource SpecSource { get; }

    public IOrientationAuthority OrientationAuthority { get; }

    public IReferenceIndexPort ReferenceIndex { get; }

    public IDefinitionDiagnostics DefinitionDiagnostics { get; }

    public SelectionSet Selections { get; }

    /// <summary>词表端口（U3）：未装配 ⇒ <see cref="NullSelectionVocabulary"/>（回退既有字面量）。</summary>
    public ISelectionVocabulary SelectionVocabulary { get; }

    public NodeFormCodec FormCodec { get; }

    public SpecDefaults Defaults { get; }

    public NodeWriteGate WriteGate { get; }

    public NodeMaterializer Materializer { get; }

    public NodeReadProjection Projection { get; }

    public NodeReconciler Reconciler { get; }

    public NodeCompensation Compensation { get; }

    public NodeSpecValidator Validator { get; }

    public NodeChainComposer ChainComposer { get; }

    public NodeReferenceIndex ReferenceIndexImpl { get; }

    public NodeDirectiveSet Directives { get; }

    public NodePortBinding PortBinding { get; }

    /// <summary>读侧选择/预算声明（null = 声明未给出）。</summary>
    public NodeSelectionSpec? SelectionSpec { get; }

    /// <summary>token 估算端口（null = 未装配）。</summary>
    public ITokenEstimator? TokenEstimator { get; }

    /// <summary>读侧选择服务（唯一编排：selection → signal → limit → budget）。</summary>
    public NodeSelectionService Selection { get; }

    /// <summary>写计划执行器（介质端口未装配 ⇒ null；显式可空，不伪造成功路径）。</summary>
    public NodeWritePlanExecutor? WritePlans { get; }

    /// <summary>容器声明注册表（缺省空注册表）。</summary>
    public NodeContainerRegistry Containers { get; }

    /// <summary>轮产物装配器（终答按到达序归因）。</summary>
    public NodeArtifactAssembler Artifacts { get; }

    /// <summary>执行声明集（只声明，不执行）。</summary>
    public IReadOnlyList<NodeExecutionBinding> ExecutionBindings { get; }

    /// <summary>执行声明沿边继承解析器。</summary>
    public AttributeInheritance AttributeInheritance { get; }

    /// <summary>轴化标签词表注册表（实例注入）。</summary>
    public ITagVocabularyRegistry TagVocabularies { get; }

    /// <summary>全 Null 装配（无语言、无载体、无库）：用于纯内存宿主与"未装配即可见化"验证。</summary>
    public static NodeFramework CreateDefault() => new();

    /// <summary>
    /// 按声明装配：身份 / 位置 / 选择 / 缺省 / 指令 / 引用字段 / 反解表全部来自声明
    /// （<see cref="NodeFrameworkSpec.Placements"/> 与 <see cref="NodeFrameworkSpec.LocToPlacement"/> 经
    /// <see cref="NodeFrameworkSpec.CreateResolver"/>、<see cref="NodeFrameworkSpec.ReferenceFields"/> 经
    /// <see cref="NodeFrameworkSpec.CreateReferenceIndex"/> 接入；原实现漏接后二者 ⇒ 反解恒 ∅、改名不级联）。
    /// 身份序数权威仍由宿主注入（<paramref name="sequenceAuthority"/>，未给 ⇒ Null 实现 + 可见化诊断）。
    /// 运行域供体由宿主注入（<paramref name="runScopeSource"/>）：仅运行态面域需要，
    /// 未给 ⇒ <see cref="NullNodeRunScopeSource"/>（身份校验退化 no-op，行为与既有逐字一致）。
    /// </summary>
    public static NodeFramework FromSpec(
        NodeFrameworkSpec spec,
        ICarrierRegistry? carriers = null,
        INodeDiagnosticsSink? diagnostics = null,
        ISequenceAuthority? sequenceAuthority = null,
        INodeRunScopeSource? runScopeSource = null,
        bool requireRunScope = false,
        ISpecSource? specSource = null,
        IOrientationAuthority? orientationAuthority = null,
        IDefinitionDiagnostics? definitionDiagnostics = null,
        ISelectionVocabulary? selectionVocabulary = null,
        NodeSelectionSpec? selection = null,
        ITokenEstimator? tokenEstimator = null,
        IEnumerable<IWriteMediumHandler>? writeMedia = null,
        IEnumerable<NodeContainerSpec>? containers = null,
        IEnumerable<NodeExecutionBinding>? executionBindings = null,
        ITagVocabularyRegistry? tagVocabularies = null)
    {
        var options = new NodeFrameworkOptions
        {
            FrameworkId = $"node-framework:{spec.Domain}",
            Domain = spec.Domain,
            Identity = spec.Identity,
            Placements = spec.Placements,
            Carriers = carriers,
            Selections = spec.Selections,
            SelectionVocabulary = selectionVocabulary,
            Defaults = spec.Defaults,
            Diagnostics = diagnostics,
            PlacementResolver = spec.CreateResolver(diagnostics),
            ReferenceIndex = spec.CreateReferenceIndex(diagnostics),
            SequenceAuthority = sequenceAuthority,
            RunScope = runScopeSource,
            RequireRunScope = requireRunScope,
            // 定义来源/诊断端口（N1）：未装配 ⇒ 框架内 Null 实现 + 可见化（不假装通过，对齐 US9001 范式）
            SpecSource = specSource,
            OrientationAuthority = orientationAuthority,
            DefinitionDiagnostics = definitionDiagnostics,
            Selection = selection ?? spec.Selection,
            TokenEstimator = tokenEstimator,
            WriteMedia = writeMedia,
            Containers = containers,
            ExecutionBindings = executionBindings,
            TagVocabularies = tagVocabularies,
            Coordinates = spec.Coordinates,
        };

        var framework = new NodeFramework(options);

        foreach (var directive in spec.Directives.All)
        {
            framework.Directives.Add(directive);
        }

        return framework;
    }

    /// <summary>端口键空间按运行单元创建（键连接不外溢：只用键与字符串值）。</summary>
    public PortKeySpace CreatePortKeySpace() => new();
}
