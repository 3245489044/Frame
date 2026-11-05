using System.Collections.Generic;
using Share.LogicComposition.Conversion.Models;

namespace Share.LogicComposition.Conversion.Generation;

/// <summary>
/// 源前端（通用管线注入点）：文本 → 模型，并暴露格式级诊断与语义级校验。
/// 领域生成器只需实现本接口，展开编排（判定同源）由 <see cref="SourceExpansionPipeline{TModel}"/> 统一承担。
/// </summary>
public interface ISourceFrontEnd<TModel>
{
    /// <summary>解析文本（格式级诊断经 <see cref="Diagnostics"/> 读取）。</summary>
    /// <param name="diagnosticPath">诊断定位所用路径（建议绝对路径以保证可点击）。</param>
    /// <param name="text">源文本。</param>
    TModel Parse(string diagnosticPath, string text);

    /// <summary>解析期收集的格式级诊断（<see cref="Parse"/> 之后读取）。</summary>
    IReadOnlyList<ConversionDiagnostic> Diagnostics { get; }

    /// <summary>模型上的语义级校验。</summary>
    IReadOnlyList<ConversionDiagnostic> Validate(TModel model);
}
