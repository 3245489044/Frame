
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Abstractions
{
    /// <summary>
    /// 转换规则：单点匹配的 IR 重写（表/链驱动，对齐既有 DSL 的「策略 table/chain」语义）。
    /// 未命中返回 false 且不产生诊断；命中但语义非法时通过 <c>diagnostic</c> 上报（result 返回原节点）。
    /// </summary>
    public interface IConversionRule
    {
        /// <summary>规则名（诊断与去重键）。</summary>
        string Name { get; }

        bool TryApply(
            ConversionNode node,
            ConversionContext context,
            out ConversionNode result,
            out ConversionDiagnostic? diagnostic);
    }
}
