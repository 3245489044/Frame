
using Microsoft.CodeAnalysis;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using Share.LogicComposition.Conversion.Models;

namespace Share.RoslynJson.Roslyn;

/// <summary>
/// Roslyn 位置 → 转换内核 <see cref="SourceSpan"/> 的适配点。
/// 行列换算的唯一实现在共享内核 <see cref="RoslynSourceLocator"/>（本类型仅做「转换内核类型」适配）。
/// </summary>
public static class SourceLocation
{
    public static SourceSpan ToSpan(SyntaxNode node)
    {
        if (node is null) return SourceSpan.None;
        if (!RoslynSourceLocator.TryGetPosition(node, out var line, out var column)) return SourceSpan.None;

        return new SourceSpan(
            RoslynSourceLocator.GetFilePath(node),
            line,
            column,
            RoslynSourceLocator.GetLength(node));
    }

    public static SourceSpan ToSpan(SyntaxToken token)
    {
        if (!RoslynSourceLocator.TryGetPosition(token, out var line, out var column)) return SourceSpan.None;

        return new SourceSpan(
            RoslynSourceLocator.GetFilePath(token.Parent),
            line,
            column,
            token.Span.Length);
    }

    /// <summary>Roslyn 诊断位置 → 源跨度（非源内位置返回 <see cref="SourceSpan.None"/>）。</summary>
    public static SourceSpan ToSpan(Diagnostic diagnostic)
    {
        if (diagnostic is null) return SourceSpan.None;
        if (!RoslynSourceLocator.TryGetPosition(diagnostic, out var file, out var line, out var column))
            return SourceSpan.None;

        return new SourceSpan(file, line, column, diagnostic.Location.SourceSpan.Length);
    }
}
