using System;
using System.Collections.Generic;
using System.Linq;
using Plate.DslMethod;
using Plate.DslMethod.Logic;
using Plate.DslMethod.Model;
using Plate.DslMethod.Parsing;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 装载期闸A（规则 R3）：任何坐标引用必须已在某已声明 <c>[Space]</c> 内可解析，否则**编译报错**
/// ——这是「未声明即静默失效」类缺陷（原 C1：_NextNodeConfig 声明被静默截断）的语言级判据。
/// 与 <c>ParameterMatrix.Row</c> 的装载期闸A 同语义，覆盖面从「参数轴」扩到「空间键 + 参数轴 + 词元」。
/// <para>
/// <b>去硬编码（H3 消除）</b>：本方法签名恒定 —— 只收「泛型声明事实 + facet 注册表 + pack 列表」，
/// 不再随 facet 膨胀。判据已抽为可注册的领域包规则集（<see cref="IUsharpDomainPack.GateRules"/>），
/// 由内核 <c>GateEngine</c> 按包声明序有序编排；本类只负责「装配领域模型 → 运行引擎 → 投影回公共诊断」。
/// </para>
/// <para>
/// <b>核心常驻闸</b>：<c>UnregisteredPrefixGateRule</c>（<c>US0013</c>）不属任何包，固定排在规则序首位
/// —— 未装载包导致的「前缀不识别」是后续一切下游怪象的根因，须先暴露。
/// </para>
/// </summary>
public static class UsharpValidator
{
    public static IReadOnlyList<UsharpDiagnostic> Validate(
        IReadOnlyList<DslDocument> documents,
        UsharpDeclarationIndex declarations,
        DslIndex index,
        UsharpFacetRegistry facets,
        IReadOnlyList<IUsharpDomainPack> packs,
        IReadOnlyList<UsharpSourceFile>? sources = null,
        DslPrefixSet? prefixScope = null)
    {
        ArgumentNullException.ThrowIfNull(facets);
        ArgumentNullException.ThrowIfNull(packs);

        var model = new UsharpGateModel(documents, declarations, index, facets, sources, prefixScope);

        var rules = new List<IGateRule<UsharpGateModel>> { new UnregisteredPrefixGateRule() };
        rules.AddRange(packs.SelectMany(p => p.GateRules));

        var result = new GateEngine<UsharpGateModel>(rules).Run(model);
        return result.Diagnostics.Select(UsharpDiagnosticProjection.ToUsharp).ToList();
    }
}
