
using System;
using Share.LogicComposition.Conversion.Abstractions;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Emitters
{
    /// <summary>IR 发射便捷扩展（失败即抛 <see cref="ConversionStageException"/>，供已知合法场景使用）。</summary>
    public static class ConversionNodeJsonExtensions
    {
        /// <summary>发射为 JSON 文本；失败抛异常（含诊断摘要）。</summary>
        public static string ToJson(this ConversionNode node, ConversionOptions? options = null)
        {
            if (node is null) throw new ArgumentNullException(nameof(node));

            var result = JsonTextEmitter.Instance.Emit(node, options);
            if (!result.IsSuccess || result.Artifact is null)
            {
                throw new ConversionStageException(
                    "CVT-TOJSON-FAIL",
                    $"发射 JSON 失败：{result.GetDiagnosticsString()}",
                    SourceSpan.None);
            }

            return result.Artifact;
        }
    }
}
