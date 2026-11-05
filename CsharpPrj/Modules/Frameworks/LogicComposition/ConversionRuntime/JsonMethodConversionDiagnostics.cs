
using System.Text.Json;
using System.Text.Json.Nodes;
using Plate.JsonMethod.Diagnostics;
using Share.LogicComposition.Conversion.Models;

namespace Share.LogicComposition.Conversion.Runtime
{
    /// <summary>
    /// JSON 解析诊断：把 <see cref="JsonException"/> 转成内核诊断，消息复用 <see cref="JsonErrorDiagnostics"/> 富化
    /// （路径 / 行号 / 列位置 / 实际 JSON 值 / 期望类型），位置还原为 <see cref="SourceSpan"/>（供编译器/测试可点击定位）。
    /// </summary>
    public static class JsonMethodConversionDiagnostics
    {
        /// <summary>JSON 解析错误码。</summary>
        public const string JsonParseErrorCode = "JSON-PARSE";

        /// <summary>JsonException → 诊断（含源定位）。</summary>
        /// <param name="ex">解析异常（可为被富化包装后的外层异常）。</param>
        /// <param name="file">源文件路径（可空）。</param>
        /// <param name="json">原始 JSON 文本（用于提取出错路径处的实际值；可空）。</param>
        /// <param name="targetType">期望目标类型（默认 <see cref="JsonNode"/>）。</param>
        public static ConversionDiagnostic FromJsonException(
            JsonException ex,
            string? file = null,
            string? json = null,
            Type? targetType = null)
        {
            if (ex is null) throw new ArgumentNullException(nameof(ex));

            // JsonSerializationService 抛的是包装异常（内层才带行号/列位置）
            var source = ex.LineNumber.HasValue ? ex : ex.InnerException as JsonException ?? ex;

            var message = JsonErrorDiagnostics.Enrich(source, json ?? string.Empty, targetType ?? typeof(JsonNode));

            return ConversionDiagnostic.Error(JsonParseErrorCode, message, ToSpan(source, file));
        }

        /// <summary>JsonException 位置 → 源跨度（1 基行号；列位置为**字节**偏移，非字符偏移）。</summary>
        public static SourceSpan ToSpan(JsonException ex, string? file = null)
        {
            if (ex is null || !ex.LineNumber.HasValue) return new SourceSpan(file, 0, 0);

            var line = Clamp(ex.LineNumber.Value) + 1;
            var column = ex.BytePositionInLine.HasValue ? Clamp(ex.BytePositionInLine.Value) + 1 : 0;

            return new SourceSpan(file, line, column);
        }

        private static int Clamp(long value)
        {
            if (value <= 0) return 0;
            return value >= int.MaxValue - 1 ? int.MaxValue - 1 : (int)value;
        }
    }
}
