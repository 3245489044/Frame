
using Microsoft.CodeAnalysis;

namespace RoslynSpace.RoslynSyntaxUtilities.Base;

/// <summary>
/// Roslyn 源提供器：文本 → 语法树（屏蔽解析选项细节，便于测试注入固定解析配置）。
/// 单一实现：分析器侧（RoslynJson 源生成器）与运行期（RoslynMethod）共用。
/// </summary>
public interface IRoslynSourceProvider
{
    /// <summary>解析源文本为语法树（不抛异常；语法错误由诊断承载）。</summary>
    SyntaxTree Parse(string text, string filePath);

    /// <summary>解析内存代码片段为语法树（合成路径见 <see cref="CSharpSourceProvider.InlineFilePath"/>）。</summary>
    SyntaxTree Parse(string text);

    /// <summary>取语法树根节点。</summary>
    SyntaxNode GetRoot(SyntaxTree tree);
}
