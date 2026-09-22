using Plate.DslMethod;
using Plate.DslMethod.Logic;
using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 装载期闸A（规则 R3）：任何坐标引用必须已在某已声明 <c>[Space]</c> 内可解析，否则**编译报错**
/// ——这是「未声明即静默失效」类缺陷（原 C1：_NextNodeConfig 声明被静默截断）的语言级判据。
/// 与 <c>ParameterMatrix.Row</c> 的装载期闸A 同语义，覆盖面从「参数轴」扩到「空间键 + 参数轴 + 词元」。
/// <para>
/// 实现：判据已抽为领域无关的 <see cref="UsharpDomainPack"/> 规则集（由内核 <c>GateEngine</c> 有序编排）；
/// 本类只负责「装配领域模型 → 运行引擎 → 投影回 Usharp 公共诊断」，公共签名与诊断语义逐字保持不变。
/// </para>
/// </summary>
public static class UsharpValidator
{
    public static IReadOnlyList<UsharpDiagnostic> Validate(
        IReadOnlyList<DslDocument> documents,
        UsharpDeclarationIndex declarations,
        DslIndex index,
        SpaceRegistry spaces,
        IReadOnlyList<UsharpMatrixDecl> matrices,
        IReadOnlyList<UsharpFlowDecl> flows,
        IReadOnlyList<UsharpRefDecl> refs,
        ObsRegistry? obs = null)
    {
        var model = new UsharpGateModel(documents, declarations, index, spaces, matrices, flows, refs, obs);
        var result = UsharpDomainPack.Instance.CreateEngine().Run(model);
        return result.Diagnostics.Select(UsharpDiagnosticProjection.ToUsharp).ToList();
    }
}
