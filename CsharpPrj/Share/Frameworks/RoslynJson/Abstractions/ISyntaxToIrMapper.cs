
using Microsoft.CodeAnalysis;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.RoslynJson.Abstractions;

/// <summary>
/// 语法节点 → 转换 IR 映射器：Roslyn 与转换内核之间的唯一桥（新增语法类型只扩规则表）。
/// </summary>
public interface ISyntaxToIrMapper
{
    ConversionNode Map(SyntaxNode node, ConversionContext context);
}
