
using Microsoft.CodeAnalysis;
using Share.LogicComposition.Conversion.Models;

namespace Share.RoslynJson.Roslyn;

/// <summary>Roslyn 位置 → 转换内核 <see cref="SourceSpan"/> 的唯一换算点（1 基行列）。</summary>
public static class SourceLocation
{
    public static SourceSpan ToSpan(SyntaxNode node)
    {
        if (node is null) return SourceSpan.None;

        var lineSpan = node.GetLocation().GetLineSpan();
        var start = lineSpan.StartLinePosition;
        var file = node.SyntaxTree?.FilePath;

        return new SourceSpan(file, start.Line + 1, start.Character + 1, node.Span.Length);
    }

    public static SourceSpan ToSpan(SyntaxToken token)
    {
        var lineSpan = token.GetLocation().GetLineSpan();
        var start = lineSpan.StartLinePosition;
        return new SourceSpan(token.SyntaxTree?.FilePath, start.Line + 1, start.Character + 1, token.Span.Length);
    }

    /// <summary>Roslyn 诊断位置 → 源跨度（非源内位置返回 <see cref="SourceSpan.None"/>）。</summary>
    public static SourceSpan ToSpan(Diagnostic diagnostic)
    {
        if (diagnostic is null) return SourceSpan.None;

        var location = diagnostic.Location;
        if (!location.IsInSource) return SourceSpan.None;

        var lineSpan = location.GetLineSpan();
        var start = lineSpan.StartLinePosition;

        return new SourceSpan(lineSpan.Path, start.Line + 1, start.Character + 1, location.SourceSpan.Length);
    }
}
