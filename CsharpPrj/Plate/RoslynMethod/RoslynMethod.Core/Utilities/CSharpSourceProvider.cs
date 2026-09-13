
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynSpace.RoslynSyntaxUtilities.Base;

/// <summary>
/// C# 源提供器：以固定 <see cref="CSharpParseOptions"/> 解析（语言版本可注入，保证解析行为可复现）。
/// </summary>
public sealed class CSharpSourceProvider : IRoslynSourceProvider
{
    /// <summary>默认单例（<see cref="LanguageVersion.Latest"/>）。</summary>
    public static readonly CSharpSourceProvider Instance = new CSharpSourceProvider();

    /// <summary>无真实文件来源（内存代码片段）时使用的合成路径。</summary>
    public const string InlineFilePath = "<inline>";

    private readonly CSharpParseOptions _parseOptions;

    public CSharpSourceProvider(LanguageVersion languageVersion = LanguageVersion.Latest, IEnumerable<string>? preprocessorSymbols = null)
        => _parseOptions = new CSharpParseOptions(languageVersion, preprocessorSymbols: preprocessorSymbols);

    /// <summary>解析所用选项（诊断与测试断言用）。</summary>
    public CSharpParseOptions ParseOptions => _parseOptions;

    public SyntaxTree Parse(string text, string filePath)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("源文件路径不能为空", nameof(filePath));

        return CSharpSyntaxTree.ParseText(text, _parseOptions, path: filePath);
    }

    public SyntaxTree Parse(string text) => Parse(text, InlineFilePath);

    public SyntaxNode GetRoot(SyntaxTree tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));
        return tree.GetRoot();
    }
}
