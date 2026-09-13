
using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynStringCodeGeneration;
using Share.LogicComposition.Conversion.Abstractions;
using Share.LogicComposition.Conversion.Emitters;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;
using Share.LogicComposition.Conversion.Diagnostics;

namespace Share.RoslynJson.Emitters;

/// <summary>
/// C# 内嵌 JSON 发射器：IR → 「类 + <c>public const string</c> 承载 JSON」的 C# 源码。
/// 这是 Roslyn 源生成器**唯一合法**的产物形态（分析器只能经 <c>AddSource</c> 产出 C# 源码）。
/// 结构生成基于共享内核 <see cref="RoslynGeneratorBase{TModel}"/>（<c>SyntaxFactory</c> 构建，与 RoslynMethod 共用同一实现），
/// 不再手写字符串拼接；JSON 文本仍由转换内核 <see cref="JsonTextEmitter"/> 生成（单一发射来源）。
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
            return ConversionResult<string>.Failure(RoslynJsonDiagnosticCodes.EmitNull, "待发射节点为 null");
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.ClassName))
            return ConversionResult<string>.Failure(RoslynJsonDiagnosticCodes.EmitClassName, "类名不能为空");
        if (string.IsNullOrWhiteSpace(spec.MemberName))
            return ConversionResult<string>.Failure(RoslynJsonDiagnosticCodes.EmitMemberName, "成员名不能为空");

        // 头部注释走 C# 注释，不污染 JSON 值
        var jsonOptions = (options ?? ConversionOptions.Default).Clone();
        jsonOptions.HeaderComment = null;

        var jsonResult = _jsonEmitter.Emit(node, jsonOptions);
        if (!jsonResult.IsSuccess || jsonResult.Artifact is null)
            return ConversionResult<string>.Failure(jsonResult.Diagnostics);

        var model = new EmbeddedJsonClassModel
        {
            Json = jsonResult.Artifact,
            HeaderComment = spec.HeaderComment,
        };

        var generationContext = new CodeGenerationContext
        {
            ClassName = spec.ClassName,
            Namespace = spec.Namespace ?? string.Empty,
            GenerateAsStatic = spec.IsStatic,
        };

        var generated = new EmbeddedJsonClassGenerator(spec)
            .GenerateAsync(model, generationContext)
            .GetAwaiter()
            .GetResult();

        if (!generated.Success)
            return ConversionResult<string>.Failure(RoslynJsonDiagnosticCodes.EmitGenerationFailed, generated.Error?.Message ?? "C# 源码生成失败");

        return ConversionResult<string>.Success(generated.GeneratedCode, jsonResult.Diagnostics);
    }

    /// <summary>生成模型（已去头注释的 JSON 文本 + 类级头部注释）。</summary>
    private sealed class EmbeddedJsonClassModel
    {
        public string Json { get; set; } = string.Empty;
        public string? HeaderComment { get; set; }
    }

    /// <summary>
    /// 内嵌 JSON 类生成器：类/成员/命名空间结构由内核 <see cref="RoslynGeneratorBase{TModel}"/> 经语法树构建；
    /// 字面量形态由 <see cref="EmbeddedJsonStyle"/> 选择（原始字符串 / 转义字面量）。
    /// </summary>
    private sealed class EmbeddedJsonClassGenerator : RoslynGeneratorBase<EmbeddedJsonClassModel>
    {
        private readonly EmbeddedJsonClassSpec _spec;

        public EmbeddedJsonClassGenerator(EmbeddedJsonClassSpec spec) => _spec = spec;

        protected override Task<CompilationUnitSyntax> BuildCompilationUnitAsync(
            EmbeddedJsonClassModel model,
            CodeGenerationContext context)
        {
            // 字面量固定在列 0：规避原始字符串「内容行缩进不得少于闭合定界符缩进」的裁剪规则
            var initializer = _spec.Style == EmbeddedJsonStyle.EscapedLiteral
                ? CreateEscapedStringLiteral(model.Json)
                : CreateRawStringLiteral(model.Json);

            var field = CreateFieldWithInitializer("string", _spec.MemberName, initializer,
                isConst: true, isPublic: true);

            var classDeclaration = CreateClass(
                context.ClassName,
                SyntaxFactory.SingletonList<MemberDeclarationSyntax>(field),
                isStatic: _spec.IsStatic,
                isSealed: _spec.IsSealed);

            MemberDeclarationSyntax member = classDeclaration;
            if (!string.IsNullOrWhiteSpace(context.Namespace))
                member = CreateNamespace(context.Namespace, SyntaxFactory.SingletonList(member));

            return Task.FromResult(
                SyntaxFactory.CompilationUnit().WithMembers(SyntaxFactory.SingletonList(member)));
        }

        protected override string Render(CompilationUnitSyntax compilationUnit)
        {
            var code = compilationUnit.NormalizeWhitespace().ToFullString();
            return string.IsNullOrEmpty(_spec.HeaderComment)
                ? code
                : RenderHeaderComment(_spec.HeaderComment!) + code;
        }

        private static string RenderHeaderComment(string headerComment)
        {
            var lines = headerComment.Split('\n');
            var builder = new StringBuilder(headerComment.Length + lines.Length * 3);

            for (var i = 0; i < lines.Length; i++)
            {
                builder.Append("// ").Append(lines[i].TrimEnd('\r')).Append('\n');
            }

            return builder.ToString();
        }
    }
}
