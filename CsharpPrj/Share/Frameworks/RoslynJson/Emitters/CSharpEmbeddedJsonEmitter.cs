
using System;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Share.LogicComposition.Conversion.Abstractions;
using Share.LogicComposition.Conversion.Emitters;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.RoslynJson.Emitters;

/// <summary>
/// C# 内嵌 JSON 发射器：IR → 「类 + <c>public const string</c> 承载 JSON」的 C# 源码。
/// 这是 Roslyn 源生成器**唯一合法**的产物形态（分析器只能经 <c>AddSource</c> 产出 C# 源码）。
/// JSON 文本仍由转换内核的 <see cref="JsonTextEmitter"/> 生成（单一发射来源）。
/// </summary>
public sealed class CSharpEmbeddedJsonEmitter : IArtifactEmitter<string>
{
    /// <summary>默认单例。</summary>
    public static readonly CSharpEmbeddedJsonEmitter Instance = new CSharpEmbeddedJsonEmitter();

    private readonly JsonTextEmitter _jsonEmitter;

    public CSharpEmbeddedJsonEmitter(JsonTextEmitter? jsonEmitter = null)
        => _jsonEmitter = jsonEmitter ?? JsonTextEmitter.Instance;

    public ConversionResult<string> Emit(ConversionNode node, ConversionOptions? options = null)
        => Emit(node, EmbeddedJsonClassSpec.Default, options);

    /// <summary>按规格发射 C# 源码。</summary>
    public ConversionResult<string> Emit(ConversionNode node, EmbeddedJsonClassSpec spec, ConversionOptions? options = null)
    {
        if (node is null)
            return ConversionResult<string>.Failure("RJ-EMIT-NULL", "待发射节点为 null");
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.ClassName))
            return ConversionResult<string>.Failure("RJ-EMIT-CLASS", "类名不能为空");
        if (string.IsNullOrWhiteSpace(spec.MemberName))
            return ConversionResult<string>.Failure("RJ-EMIT-MEMBER", "成员名不能为空");

        // 头部注释走 C# 注释，不污染 JSON 值
        var jsonOptions = (options ?? ConversionOptions.Default).Clone();
        jsonOptions.HeaderComment = null;

        var jsonResult = _jsonEmitter.Emit(node, jsonOptions);
        if (!jsonResult.IsSuccess || jsonResult.Artifact is null)
            return ConversionResult<string>.Failure(jsonResult.Diagnostics);

        var code = Render(spec, jsonResult.Artifact);
        return ConversionResult<string>.Success(code, jsonResult.Diagnostics);
    }

    private static string Render(EmbeddedJsonClassSpec spec, string json)
    {
        var builder = new StringBuilder(json.Length + 256);

        if (!string.IsNullOrEmpty(spec.HeaderComment))
        {
            var lines = spec.HeaderComment!.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                builder.Append("// ").Append(lines[i].TrimEnd('\r')).Append('\n');
            }
        }

        var hasNamespace = !string.IsNullOrWhiteSpace(spec.Namespace);
        var indent = hasNamespace ? "    " : string.Empty;

        if (hasNamespace) builder.Append("namespace ").Append(spec.Namespace).Append('\n').Append("{\n");

        var modifiers = spec.IsStatic ? "public static class " : (spec.IsSealed ? "public sealed class " : "public class ");
        builder.Append(indent).Append(modifiers).Append(spec.ClassName).Append('\n');
        builder.Append(indent).Append("{\n");
        builder.Append(indent).Append("    public const string ").Append(spec.MemberName).Append(" =\n");

        // 字面量固定在列 0：规避原始字符串「内容行缩进不得少于闭合定界符缩进」的裁剪规则
        builder.Append(RenderLiteral(json, spec.Style));
        builder.Append(";\n");

        builder.Append(indent).Append("}\n");
        if (hasNamespace) builder.Append("}\n");

        return builder.ToString();
    }

    private static string RenderLiteral(string json, EmbeddedJsonStyle style)
    {
        if (style == EmbeddedJsonStyle.EscapedLiteral)
            return SymbolDisplay.FormatLiteral(json, quote: true);

        var fence = new string('"', ComputeFenceLength(json));

        var builder = new StringBuilder(json.Length + 16);
        builder.Append(fence).Append('\n');
        builder.Append(json);
        if (!json.EndsWith("\n", StringComparison.Ordinal)) builder.Append('\n');
        builder.Append(fence);
        return builder.ToString();
    }

    /// <summary>围栏长度 = 内容中最长连续引号串 + 1（下限 3），保证原始字符串不被内容截断。</summary>
    private static int ComputeFenceLength(string text)
    {
        var longest = 0;
        var current = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                current++;
                if (current > longest) longest = current;
            }
            else
            {
                current = 0;
            }
        }

        return Math.Max(3, longest + 1);
    }
}
