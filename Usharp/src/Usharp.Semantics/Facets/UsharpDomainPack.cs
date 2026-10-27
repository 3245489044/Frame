using Plate.DslMethod;
using Share.LogicComposition.Conversion.Packs;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// Usharp 领域包：把 Usharp 的身份（<c>usharp</c>）、诊断码段（<c>US</c>）、**facet provider 集**与闸A 规则集
/// 绑定为可注册单元（实现内核 <see cref="IDomainPack{TModel}"/> + Usharp <see cref="IUsharpDomainPack"/>）。
/// 规则执行序 = 声明序，与既有校验顺序逐字一致；facet provider 序 = 既有编译流序（保诊断归因序）。
/// </summary>
public sealed class UsharpDomainPack : IDomainPack<UsharpGateModel>, IUsharpDomainPack
{
    /// <summary>单例（规则集/facet 集无状态，可安全复用）。</summary>
    public static readonly UsharpDomainPack Instance = new();

    private static readonly IReadOnlyList<string> CodePrefixes = new[] { "US" };

    private static readonly IReadOnlyList<IGateRule<UsharpGateModel>> Rules = new IGateRule<UsharpGateModel>[]
    {
        new RefGateRule(),
        new MatrixGateRule(),
        new FlowGateRule(),
        new HierarchyGateRule(),
        new ProcessBindingGateRule(),
        new ProcessPlanGateRule(),
        new ConnectGateRule(),
        new CrossReferenceGateRule(),
        new ObsGateRule(),
        new DecisionGateRule(),
        new BindingDuplicateGateRule(),
        new DomainGateRule(),
        new GatherGateRule(),
        new KeySpaceGateRule(),
        new CommandGateRule(),
        new ReadModelGateRule(),
        new TableGateRule(),
        new RecordGateRule(),
        new RuleGateRule(),
    };

    /// <summary>默认 facet provider 集（序 = <see cref="UsharpUnitRegistry.Units"/>，保既有诊断归因序与产物序）。</summary>
    private static readonly IReadOnlyList<IUsharpFacetProvider> Providers = UsharpUnitRegistry.Providers;

    /// <summary>
    /// 默认 facet 投影器集（空 = 既有发射器仍由消费方手写调用；本批只落「统一出口」抽象，
    /// 具体发射器实现 <see cref="IUsharpFacetProjector"/> 归后续批次，避免语义漂移）。
    /// </summary>
    private static readonly IReadOnlyList<IUsharpFacetProjector> Projectors = System.Array.Empty<IUsharpFacetProjector>();

    private UsharpDomainPack()
    {
    }

    public string Id => "usharp";

    public IReadOnlyList<string> DiagnosticCodePrefixes => CodePrefixes;

    public IReadOnlyList<IGateRule<UsharpGateModel>> GateRules => Rules;

    public IReadOnlyList<IUsharpFacetProvider> FacetProviders => Providers;

    public IReadOnlyList<IUsharpFacetProjector> FacetProjectors => Projectors;

    /// <summary>构造本包的闸A 执行器（规则集有序，产出诊断序稳定）。</summary>
    public GateEngine<UsharpGateModel> CreateEngine() => new GateEngine<UsharpGateModel>(Rules);
}
