
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Share.LogicComposition.Conversion.Abstractions;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Emitters
{
    /// <summary>
    /// 确定性 JSON 文本发射器（零外部依赖，可被 Roslyn 分析器复用）。
    /// 三处确定性保证：① 键序稳定（默认保留 IR 插入序，可选按 Ordinal 排序）；
    /// ② 数字使用 InvariantCulture；③ 头部注释固定前缀 <c>// </c>（JSONC，消费方已跳过注释）。
    /// </summary>
    public sealed class JsonTextEmitter : IArtifactEmitter<string>
    {
        /// <summary>无状态单例。</summary>
        public static readonly JsonTextEmitter Instance = new JsonTextEmitter();

        public ConversionResult<string> Emit(ConversionNode node, ConversionOptions? options = null)
        {
            if (node is null)
                return ConversionResult<string>.Failure("CVT-EMIT-NULL", "待发射节点为 null");

            var opts = options ?? ConversionOptions.Default;
            if (opts.MaxDepth <= 0)
                return ConversionResult<string>.Failure("CVT-EMIT-MAXDEPTH", $"非法 MaxDepth：{opts.MaxDepth}");

            var diagnostics = new List<ConversionDiagnostic>();
            var builder = new StringBuilder(512);

            WriteHeader(builder, opts);
            WriteNode(builder, node, opts, 1, diagnostics);

            if (diagnostics.Any(d => d.Severity == ConversionSeverity.Error))
                return ConversionResult<string>.Failure(diagnostics, builder.ToString());

            return ConversionResult<string>.Success(builder.ToString(), diagnostics);
        }

        private static void WriteHeader(StringBuilder builder, ConversionOptions options)
            => builder.Append(JsonHeaderComment.Render(options.HeaderComment));

        private static void WriteNode(
            StringBuilder builder,
            ConversionNode node,
            ConversionOptions options,
            int depth,
            List<ConversionDiagnostic> diagnostics)
        {
            if (depth > options.MaxDepth)
            {
                diagnostics.Add(ConversionDiagnostic.Error(
                    "CVT-EMIT-MAXDEPTH",
                    $"IR 深度超过上限 {options.MaxDepth}（疑似畸形/自引用结构）",
                    SourceSpan.None));
                builder.Append("null");
                return;
            }

            switch (node.Kind)
            {
                case ConversionNodeKind.Object:
                    WriteObject(builder, (ConversionObject)node, options, depth, diagnostics);
                    break;

                case ConversionNodeKind.Array:
                    WriteArray(builder, (ConversionArray)node, options, depth, diagnostics);
                    break;

                case ConversionNodeKind.Scalar:
                    WriteScalar(builder, (ConversionScalar)node);
                    break;

                case ConversionNodeKind.Raw:
                    builder.Append(((ConversionRaw)node).Text);
                    break;

                default:
                    diagnostics.Add(ConversionDiagnostic.Error(
                        "CVT-EMIT-KIND",
                        $"未知 IR 节点种类：{node.Kind}",
                        SourceSpan.None));
                    builder.Append("null");
                    break;
            }
        }

        private static void WriteObject(
            StringBuilder builder,
            ConversionObject node,
            ConversionOptions options,
            int depth,
            List<ConversionDiagnostic> diagnostics)
        {
            var members = options.SortObjectKeys
                ? node.Members.OrderBy(m => m.Key, StringComparer.Ordinal).ToList()
                : node.Members.ToList();

            if (members.Count == 0)
            {
                builder.Append("{}");
                return;
            }

            builder.Append('{');

            for (var i = 0; i < members.Count; i++)
            {
                if (i > 0) builder.Append(',');
                NewLine(builder, options, depth + 1);
                WriteString(builder, members[i].Key);
                builder.Append(':');
                if (options.Indent.Length > 0) builder.Append(' ');
                WriteNode(builder, members[i].Value ?? ConversionScalar.Null, options, depth + 1, diagnostics);
            }

            NewLine(builder, options, depth);
            builder.Append('}');
        }

        private static void WriteArray(
            StringBuilder builder,
            ConversionArray node,
            ConversionOptions options,
            int depth,
            List<ConversionDiagnostic> diagnostics)
        {
            if (node.Count == 0)
            {
                builder.Append("[]");
                return;
            }

            builder.Append('[');

            for (var i = 0; i < node.Count; i++)
            {
                if (i > 0) builder.Append(',');
                NewLine(builder, options, depth + 1);
                WriteNode(builder, node.Items[i] ?? ConversionScalar.Null, options, depth + 1, diagnostics);
            }

            NewLine(builder, options, depth);
            builder.Append(']');
        }

        private static void WriteScalar(StringBuilder builder, ConversionScalar node)
        {
            switch (node.ScalarKind)
            {
                case ConversionScalarKind.Null:
                    builder.Append("null");
                    break;

                case ConversionScalarKind.Boolean:
                    builder.Append(node.Value is bool flag && flag ? "true" : "false");
                    break;

                case ConversionScalarKind.Integer:
                    builder.Append(node.Value is long number
                        ? number.ToString(CultureInfo.InvariantCulture)
                        : "0");
                    break;

                case ConversionScalarKind.Number:
                    builder.Append(FormatNumber(node.Value));
                    break;

                case ConversionScalarKind.String:
                    WriteString(builder, node.Value as string ?? string.Empty);
                    break;

                default:
                    builder.Append("null");
                    break;
            }
        }

        /// <summary>浮点格式化（InvariantCulture；NaN/Infinity 非法 JSON → 降级 null）。</summary>
        private static string FormatNumber(object? value)
        {
            if (value is not double number) return "null";
            if (double.IsNaN(number) || double.IsInfinity(number)) return "null";

            return number.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void NewLine(StringBuilder builder, ConversionOptions options, int depth)
        {
            if (options.Indent.Length == 0) return;

            builder.Append('\n');
            for (var i = 1; i < depth; i++) builder.Append(options.Indent);
        }

        /// <summary>JSON 字符串转义（控制字符 → \uXXXX；中文等非 ASCII 原样保留）。</summary>
        public static void WriteString(StringBuilder builder, string value)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));

            builder.Append('"');
            if (value is not null)
            {
                for (var i = 0; i < value.Length; i++)
                {
                    var c = value[i];
                    switch (c)
                    {
                        case '"': builder.Append("\\\""); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '\b': builder.Append("\\b"); break;
                        case '\f': builder.Append("\\f"); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        default:
                            if (c < 0x20)
                                builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else
                                builder.Append(c);
                            break;
                    }
                }
            }
            builder.Append('"');
        }

    }
}
