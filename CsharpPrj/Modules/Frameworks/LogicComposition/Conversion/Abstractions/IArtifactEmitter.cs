
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Abstractions
{
    /// <summary>
    /// 产物发射器：IR → 目标格式（JSON 文本 / C# 内嵌 / 清单）。
    /// 新增目标格式只需新增发射器，转换管线保持不变。
    /// </summary>
    public interface IArtifactEmitter<T>
    {
        ConversionResult<T> Emit(ConversionNode node, ConversionOptions? options = null);
    }
}
