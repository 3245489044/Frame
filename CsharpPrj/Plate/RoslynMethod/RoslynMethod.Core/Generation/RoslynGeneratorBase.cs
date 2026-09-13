
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynSpace.RoslynStringCodeGeneration;

/// <summary>
/// C# 源码生成基底（自 RoslynMethod.Generation 上移至 RoslynMethod.Core）：
/// 以 <see cref="SyntaxFactory"/> 构建语法树 → 规范化渲染，避免手写字符串拼接产生不可校验的 C# 文本。
/// 消费方：RoslynMethod（<c>CodeGenerators</c>）与 RoslynJson（内嵌 JSON 类发射器，经 netstandard2.0 目标复用）。
/// </summary>
public abstract class RoslynGeneratorBase<TModel>
{
    /// <summary>原始字符串字面量解析选项（C# 11 起的原始字符串语法）。</summary>
    private static readonly CSharpParseOptions RawStringLiteralParseOptions =
        new CSharpParseOptions(LanguageVersion.Latest);

    public async Task<CodeGenerationResult> GenerateAsync(TModel model, CodeGenerationContext context)
    {
        try
        {
            var compilationUnit = await BuildCompilationUnitAsync(model, context);
            var code = Render(compilationUnit);
            return CodeGenerationResult.SuccessResult(code);
        }
        catch (Exception ex)
        {
            return CodeGenerationResult.ErrorResult(ex);
        }
    }

    protected abstract Task<CompilationUnitSyntax> BuildCompilationUnitAsync(TModel model, CodeGenerationContext context);

    /// <summary>渲染钩子（默认规范化空白；派生类可覆盖以注入头部注释等前后缀）。</summary>
    protected virtual string Render(CompilationUnitSyntax compilationUnit)
        => compilationUnit.NormalizeWhitespace().ToFullString();

    protected FieldDeclarationSyntax CreateField(string typeName, string fieldName, string initializerValue,
        bool isStatic = true, bool isReadOnly = true)
        => CreateFieldWithInitializer(
            typeName,
            fieldName,
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(initializerValue)),
            isStatic,
            isReadOnly);

    /// <summary>按给定初始化表达式创建字段（支持 const / 访问修饰符 / 原始字符串字面量等 SyntaxFactory 无法直接构造的初值）。</summary>
    protected FieldDeclarationSyntax CreateFieldWithInitializer(string typeName, string fieldName,
        ExpressionSyntax initializer, bool isStatic = false, bool isReadOnly = false, bool isConst = false,
        bool isPublic = false)
    {
        if (initializer is null) throw new ArgumentNullException(nameof(initializer));

        var variableDeclarator = SyntaxFactory.VariableDeclarator(fieldName)
            .WithInitializer(SyntaxFactory.EqualsValueClause(initializer));

        var variableDeclaration = SyntaxFactory.VariableDeclaration(
            SyntaxFactory.ParseTypeName(typeName))
            .WithVariables(SyntaxFactory.SingletonSeparatedList(variableDeclarator));

        // 修饰符按 C# 语法顺序显式排列（public → const → static → readonly），避免追加顺序产生非法修饰符序
        var modifiers = new List<SyntaxToken>(4);
        if (isPublic) modifiers.Add(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
        if (isConst) modifiers.Add(SyntaxFactory.Token(SyntaxKind.ConstKeyword));
        if (isStatic) modifiers.Add(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
        if (isReadOnly) modifiers.Add(SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword));

        return SyntaxFactory.FieldDeclaration(variableDeclaration)
            .WithModifiers(SyntaxFactory.TokenList(modifiers));
    }

    /// <summary>普通转义字符串字面量（由 Roslyn <see cref="SymbolDisplay"/> 生成，绝对安全）。</summary>
    protected static ExpressionSyntax CreateEscapedStringLiteral(string value)
        => SyntaxFactory.ParseExpression(
            SymbolDisplay.FormatLiteral(value ?? string.Empty, quote: true),
            options: RawStringLiteralParseOptions);

    /// <summary>
    /// 原始字符串字面量（C# 11+；围栏长度按内容自适应，文本保留换行便于评审）。
    /// 围栏固定在列 0：内容行缩进恒 ≥ 闭合围栏缩进，规避规范化重排对原始字符串的裁剪规则影响。
    /// </summary>
    protected static ExpressionSyntax CreateRawStringLiteral(string value)
    {
        var text = value ?? string.Empty;
        var fence = new string('"', ComputeRawStringFenceLength(text));

        var builder = new System.Text.StringBuilder(text.Length + fence.Length * 2 + 4);
        builder.Append(fence).Append('\n');
        builder.Append(text);
        if (!text.EndsWith("\n", StringComparison.Ordinal)) builder.Append('\n');
        builder.Append(fence);

        return SyntaxFactory.ParseExpression(builder.ToString(), options: RawStringLiteralParseOptions);
    }

    /// <summary>围栏长度 = 内容中最长连续引号串 + 1（下限 3），保证原始字符串不被内容截断。</summary>
    protected static int ComputeRawStringFenceLength(string? text)
    {
        var longest = 0;
        var current = 0;

        if (text is not null)
        {
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
        }

        return Math.Max(3, longest + 1);
    }

    protected ClassDeclarationSyntax CreateClass(string className, SyntaxList<MemberDeclarationSyntax> members,
        bool isStatic = false, bool isSealed = false)
    {
        var classDeclaration = SyntaxFactory.ClassDeclaration(className)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithMembers(members);

        if (isStatic)
            classDeclaration = classDeclaration.AddModifiers(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
        else if (isSealed)
            classDeclaration = classDeclaration.AddModifiers(SyntaxFactory.Token(SyntaxKind.SealedKeyword));

        return classDeclaration;
    }

    protected NamespaceDeclarationSyntax CreateNamespace(string name, SyntaxList<MemberDeclarationSyntax> members)
    {
        return SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(name))
            .WithMembers(members);
    }

    protected UsingDirectiveSyntax CreateUsing(string namespaceName)
    {
        return SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName));
    }
}
