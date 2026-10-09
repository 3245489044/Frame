using Share.LogicComposition.Conversion.Packs;
using Share.LogicComposition.Conversion.Validation;

namespace Usharp.Semantics;

/// <summary>
/// Usharp 领域包：把 Usharp 的身份（<c>usharp</c>）、诊断码段（<c>US</c>）与闸A 规则集
/// 绑定为内核 <see cref="IDomainPack{TModel}"/> 的实例。规则执行序 = 声明序，与既有校验顺序逐字一致。
/// </summary>
public sealed class UsharpDomainPack : IDomainPack<UsharpGateModel>
{
    /// <summary>单例（规则集无状态，可安全复用）。</summary>
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
    };

    private UsharpDomainPack()
    {
    }

    public string Id => "usharp";

    public IReadOnlyList<string> DiagnosticCodePrefixes => CodePrefixes;

    public IReadOnlyList<IGateRule<UsharpGateModel>> GateRules => Rules;

    /// <summary>构造本包的闸A 执行器（规则集有序，产出诊断序稳定）。</summary>
    public GateEngine<UsharpGateModel> CreateEngine() => new GateEngine<UsharpGateModel>(Rules);
}
