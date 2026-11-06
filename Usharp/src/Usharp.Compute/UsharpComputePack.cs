using Plate.DslMethod;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Semantics;

namespace Usharp.Compute;

/// <summary>
/// 计算层领域包：唯一新增语法前缀 <c>[Compute]</c> + 闸A 规则 <see cref="ComputeGateRule"/>。
/// <para>
/// <c>[Compute]</c> 复用 <see cref="DslSymbolKind.Unknown"/>（哨兵种类，不新增枚举成员）：
/// 计算单元按 <c>Prefix</c> 识别，其它领域规则不会因新种类而分支。
/// 未装载本包时 <c>[Compute]</c> 不进编译作用域，按既有 <c>US0013</c> 报未注册前缀。
/// </para>
/// <para>本包不提供 facet provider：计算面不进入既有 facet 声明面（诊断与产物只走独立计算清单）。</para>
/// </summary>
public sealed class UsharpComputePack : UsharpDomainPackSpec
{
    public const string PackId = "usharp.compute";

    public UsharpComputePack()
        : base(PackId,
            Array.Empty<IUsharpFacetProvider>(),
            new IGateRule<UsharpGateModel>[] { new ComputeGateRule() },
            new[]
            {
                new DslPrefixDef(UsharpComputeClassifier.ComputePrefix, DslSymbolKind.Unknown, "Compute")
            })
    {
    }
}
