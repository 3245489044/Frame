
using Microsoft.CodeAnalysis;

namespace Share.RoslynJson.Abstractions;

/// <summary>
/// Roslyn 源提供器：文本 → 语法树（屏蔽解析选项细节，便于测试注入固定解析配置）。
/// </summary>
public interface IRoslynSourceProvider
{
    /// <summary>解析源文本为语法树（不抛异常；语法错误由诊断承载）。</summary>
    SyntaxTree Parse(string text, string filePath);

    /// <summary>取语法树根节点。</summary>
    SyntaxNode GetRoot(SyntaxTree tree);
}
