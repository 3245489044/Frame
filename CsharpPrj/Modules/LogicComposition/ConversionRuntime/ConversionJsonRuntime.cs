
using System.Text.Json;
using System.Text.Json.Nodes;
using Plate.JsonMethod.Diagnostics;
using Plate.JsonMethod.Serialization;
using Plate.JsonMethod.Utilities;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Runtime
{
    /// <summary>
    /// 运行期 JSON 门面：转换 IR ↔ JSON 的平台统一出口（解析/发射/计量一律走 <c>Plate.JsonMethod</c>）。
    /// 路径导航、合并、语法校验等进一步需求直接复用 <see cref="JsonPathHelper"/> / <see cref="JsonMergeService"/> /
    /// <c>JsonValidationService</c>，不在转换内核重复实现。
    /// </summary>
    public static class ConversionJsonRuntime
    {
        /// <summary>IR → JSON（缩进由 <see cref="JsonContext"/> 决定；<paramref name="headerComment"/> 为 JSONC 头注释）。</summary>
        public static ConversionResult<JsonMethodJsonArtifact> Emit(
            ConversionNode node,
            JsonContext context = JsonContext.PrettyPrint,
            string? headerComment = null)
            => new JsonMethodJsonEmitter(context).Emit(node, WithHeader(headerComment));

        /// <summary>IR → 缩进 JSON（<see cref="JsonContext.PrettyPrint"/>）。</summary>
        public static ConversionResult<JsonMethodJsonArtifact> EmitPretty(ConversionNode node, string? headerComment = null)
            => JsonMethodJsonEmitter.Pretty.Emit(node, WithHeader(headerComment));

        /// <summary>IR → 紧凑 JSON（<see cref="JsonContext.Compact"/>）。</summary>
        public static ConversionResult<JsonMethodJsonArtifact> EmitCompact(ConversionNode node, string? headerComment = null)
            => JsonMethodJsonEmitter.Compact.Emit(node, WithHeader(headerComment));

        /// <summary>JSON 文本 → IR（解析失败返回带源定位的诊断，不抛异常）。</summary>
        public static ConversionResult<ConversionNode> Parse(string json, string? file = null)
        {
            if (json is null)
                return ConversionResult<ConversionNode>.Failure("JSON-NULL", "JSON 文本为 null");

            try
            {
                var node = JsonSerializationService.Instance.ParseJsonNode(json);
                return ConversionResult<ConversionNode>.Success(ConversionNodeJsonNodeBridge.FromJsonNode(node));
            }
            catch (JsonException ex)
            {
                return ConversionResult<ConversionNode>.Failure(
                    new[] { JsonMethodConversionDiagnostics.FromJsonException(ex, file, json) });
            }
        }

        /// <summary>JsonNode → IR（无解析开销；已有节点时优先用它）。</summary>
        public static ConversionResult<ConversionNode> FromJsonNode(JsonNode? node)
            => ConversionResult<ConversionNode>.Success(ConversionNodeJsonNodeBridge.FromJsonNode(node));

        /// <summary>JSON 文本计量（复用 <see cref="JsonSizeAnalyzer"/>）。</summary>
        public static JsonSizeInfo Analyze(string json) => JsonSizeAnalyzer.AnalyzeJsonSize(json);

        private static ConversionOptions WithHeader(string? headerComment)
        {
            if (string.IsNullOrEmpty(headerComment)) return ConversionOptions.Default;

            var options = ConversionOptions.Default;
            options.HeaderComment = headerComment;
            return options;
        }
    }
}
