
using System.Text.Json;
using Plate.JsonMethod.Diagnostics;
using Plate.JsonMethod.Serialization;
using Share.LogicComposition.Conversion.Abstractions;
using Share.LogicComposition.Conversion.Emitters;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Runtime
{
    /// <summary>
    /// 运行期 JSON 发射器：IR → JSON 文本，**序列化统一交由 <c>Plate.JsonMethod</c> 上下文**（平台 JSON 单一出口）。
    /// 与内核侧 <see cref="JsonTextEmitter"/> 的分工：
    /// 内核发射器为 <c>netstandard2.0</c> 零依赖（Roslyn 分析器可用）；本发射器为运行期唯一出口，复用 JsonMethod 的
    /// 缩进/编码/诊断/计量能力，避免两套 JSON 写出实现。
    /// </summary>
    public sealed class JsonMethodJsonEmitter : IArtifactEmitter<JsonMethodJsonArtifact>
    {
        /// <summary>缩进预设（<see cref="JsonContext.PrettyPrint"/>）。</summary>
        public static readonly JsonMethodJsonEmitter Pretty = new JsonMethodJsonEmitter(JsonContext.PrettyPrint);

        /// <summary>紧凑预设（<see cref="JsonContext.Compact"/>）。</summary>
        public static readonly JsonMethodJsonEmitter Compact = new JsonMethodJsonEmitter(JsonContext.Compact);

        private readonly JsonContext _context;

        public JsonMethodJsonEmitter(JsonContext context = JsonContext.PrettyPrint) => _context = context;

        /// <summary>所用 JSON 上下文（缩进/编码策略来源）。</summary>
        public JsonContext Context => _context;

        public ConversionResult<JsonMethodJsonArtifact> Emit(ConversionNode node, ConversionOptions? options = null)
        {
            if (node is null)
                return ConversionResult<JsonMethodJsonArtifact>.Failure("CVT-EMIT-NULL", "待发射节点为 null");

            var headerComment = (options ?? ConversionOptions.Default).HeaderComment;

            try
            {
                var jsonNode = ConversionNodeJsonNodeBridge.ToJsonNode(node);
                var body = jsonNode?.ToJsonString(JsonSerializerFactory.GetOptions(_context)) ?? "null";
                var text = JsonHeaderComment.Render(headerComment) + body;
                var size = JsonSizeAnalyzer.AnalyzeJsonSize(body);

                return ConversionResult<JsonMethodJsonArtifact>.Success(
                    new JsonMethodJsonArtifact(text, body, jsonNode, headerComment, size));
            }
            catch (JsonException ex)
            {
                return ConversionResult<JsonMethodJsonArtifact>.Failure(
                    new[] { JsonMethodConversionDiagnostics.FromJsonException(ex) });
            }
        }
    }
}
