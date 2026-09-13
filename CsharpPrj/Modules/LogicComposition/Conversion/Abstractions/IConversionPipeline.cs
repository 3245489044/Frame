
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Abstractions
{
    /// <summary>转换管线：按声明顺序执行阶段，统一收集诊断（不抛异常）。</summary>
    public interface IConversionPipeline
    {
        IReadOnlyList<IConversionStage> Stages { get; }

        ConversionResult<ConversionNode> Run(ConversionNode input, ConversionContext? context = null);
    }
}
