using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>
/// 规则取值端口：规则表谓词访问领域模型的**唯一**通道（判据不求值领域类型，只经本端口取值）。
/// <para>
/// 实现方 = 领域包（NMD 走内核通用 <see cref="ConversionObjectRuleModel{TModel}"/>，零适配；
/// 非 IR 领域自持适配器）。未装配端口 ⇒ <c>RuleSpecEngine</c> **构筑期拒绝**（无取值通道不得构筑），
/// 而非运行期空转（对齐 RuleSpec §8）。
/// </para>
/// <para>契约：取值缺失**不抛异常**（返回 false / 空序列），缺失语义由谓词决定（如 <c>absent</c>）。</para>
/// </summary>
/// <typeparam name="TModel">领域模型（如 NMD 文档 / Usharp 闸A 装配体）。</typeparam>
public interface IRuleObjectModel<in TModel>
{
    /// <summary>取作用域遍历起点（可为 null = 空模型，判据按缺失语义判定）。</summary>
    /// <param name="model">领域模型。</param>
    ConversionNode? Root(TModel model);

    /// <summary>节点相对路径取值（路径文法见 <see cref="RulePath"/>）。</summary>
    /// <param name="node">当前节点（作用域定位点）。</param>
    /// <param name="relPath">相对路径。</param>
    /// <param name="value">out：取值（缺失为 null）。</param>
    bool TryGet(ConversionNode? node, string relPath, out ConversionNode? value);

    /// <summary>集合枚举（路径解析后展开一层数组；见实现注释）。</summary>
    /// <param name="node">当前节点。</param>
    /// <param name="collectionPath">集合路径。</param>
    IReadOnlyList<ConversionNode> Enumerate(ConversionNode? node, string collectionPath);

    /// <summary>路径定位（诊断归因；无定位信息返回 <see cref="SourceSpan.None"/>）。</summary>
    /// <param name="model">领域模型。</param>
    /// <param name="path">路径（绝对路径形态，由引擎按作用域拼接）。</param>
    SourceSpan SpanOf(TModel model, string path);
}
