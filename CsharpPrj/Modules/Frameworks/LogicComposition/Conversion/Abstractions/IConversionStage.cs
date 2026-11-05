
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Abstractions
{
    /// <summary>
    /// 转换阶段：IR → IR 的一次确定性变换（解析/展开/规范化/校验）。
    /// 阶段内部失败应抛 <see cref="ConversionStageException"/>，由管线统一转诊断，避免各处 try/catch 重复。
    /// </summary>
    public interface IConversionStage
    {
        /// <summary>阶段名（诊断与追踪用）。</summary>
        string Name { get; }

        ConversionNode Execute(ConversionNode input, ConversionContext context);
    }
}
