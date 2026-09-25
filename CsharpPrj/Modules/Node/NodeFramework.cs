using Modules.Node.Carriers;
using Modules.Node.Diagnostics;
using Modules.Node.Governance;
using Modules.Node.Identity;
using Modules.Node.Materialization;
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

    public NodeFormCodec? FormCodec { get; init; }

    public SpecDefaults? Defaults { get; init; }

    public TimeSpan? WriteGateTimeout { get; init; }

    public INodeDiagnosticsSink? Diagnostics { get; init; }
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
        Selections = opt.Selections ?? new SelectionSet(Diagnostics);
        FormCodec = opt.FormCodec ?? new NodeFormCodec();
        Defaults = opt.Defaults ?? new SpecDefaults();
        PlacementResolver = opt.PlacementResolver ?? new NullPlacementResolver(Diagnostics);

        WriteGate = new NodeWriteGate($"{FrameworkId}:gate", opt.WriteGateTimeout ?? TimeSpan.Zero, Diagnostics);
        Materializer = new NodeMaterializer(
            Identity, Placements, Carriers, WriteGate, FormCodec, Defaults, Diagnostics, RunScope, RequireRunScope,
            Domain);
        Projection = new NodeReadProjection(Placements, Carriers, Selections, FormCodec, Diagnostics);
        Reconciler = new NodeReconciler(Placements, Carriers, Diagnostics);
        Compensation = new NodeCompensation(Carriers, Diagnostics);
        Validator = new NodeSpecValidator(Carriers, Diagnostics);
        ReferenceIndexImpl = new NodeReferenceIndex(ReferenceIndex.ReferenceFields, Diagnostics);
        ChainComposer = new NodeChainComposer(ReferenceIndexImpl, Diagnostics);
        Directives = new NodeDirectiveSet(Diagnostics);
        PortBinding = new NodePortBinding(Diagnostics);
    }

    public string FrameworkId { get; }

    /// <summary>本框架所属域键（未按声明装配 ⇒ <c>null</c>；运行域校验退回写请求给定的域）。</summary>
    public string? Domain { get; }

    public INodeDiagnosticsSink Diagnostics { get; }

    /// <summary>内建诊断收集器（未显式注入 sink 时可用；否则为 null）。</summary>
    public NodeFrameworkDiagnostics? Collector => _collector;

    public IdentitySpec Identity { get; }

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
        bool requireRunScope = false)
    {
        var options = new NodeFrameworkOptions
        {
            FrameworkId = $"node-framework:{spec.Domain}",
            Domain = spec.Domain,
            Identity = spec.Identity,
            Placements = spec.Placements,
            Carriers = carriers,
            Selections = spec.Selections,
            Defaults = spec.Defaults,
            Diagnostics = diagnostics,
            PlacementResolver = spec.CreateResolver(diagnostics),
            ReferenceIndex = spec.CreateReferenceIndex(diagnostics),
            SequenceAuthority = sequenceAuthority,
            RunScope = runScopeSource,
            RequireRunScope = requireRunScope,
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
