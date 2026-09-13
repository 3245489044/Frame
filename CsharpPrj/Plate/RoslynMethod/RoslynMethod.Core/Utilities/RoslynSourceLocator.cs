
using System;
using Microsoft.CodeAnalysis;

namespace RoslynSpace.RoslynSyntaxUtilities.Base;

/// <summary>
/// Roslyn 源位置换算的唯一实现（1 基行列）：语法节点 / 词法记号 / 诊断 → 文件:行:列。
/// 收敛 RoslynMethod 侧原有内联 <c>Line + 1 / Character + 1</c> 换算，避免点差漂移。
/// </summary>
public static class RoslynSourceLocator
{
    /// <summary>节点位置（无有效位置返回 false）。</summary>
    public static bool TryGetPosition(SyntaxNode? node, out int line, out int column)
    {
        line = 0;
        column = 0;
        if (node is null) return false;

        var lineSpan = node.GetLocation().GetLineSpan();
        if (!lineSpan.IsValid) return false;

        var start = lineSpan.StartLinePosition;
        line = start.Line + 1;
        column = start.Character + 1;
        return true;
    }

    /// <summary>词法记号位置（无有效位置返回 false）。</summary>
    public static bool TryGetPosition(SyntaxToken token, out int line, out int column)
    {
        line = 0;
        column = 0;

        var lineSpan = token.GetLocation().GetLineSpan();
        if (!lineSpan.IsValid) return false;

        var start = lineSpan.StartLinePosition;
        line = start.Line + 1;
        column = start.Character + 1;
        return true;
    }

    /// <summary>诊断位置（非源内位置返回 false）。</summary>
    public static bool TryGetPosition(
        Diagnostic? diagnostic,
        out string? filePath,
        out int line,
        out int column)
    {
        filePath = null;
        line = 0;
        column = 0;
        if (diagnostic is null) return false;

        var location = diagnostic.Location;
        if (!location.IsInSource) return false;

        var lineSpan = location.GetLineSpan();
        var start = lineSpan.StartLinePosition;
        filePath = lineSpan.Path;
        line = start.Line + 1;
        column = start.Character + 1;
        return true;
    }

    /// <summary>节点起始行（1 基；无有效位置返回 0）。</summary>
    public static int GetLineNumber(SyntaxNode node)
        => TryGetPosition(node, out var line, out _) ? line : 0;

    /// <summary>节点所属文件路径（语法树未携带路径时返回 null）。</summary>
    public static string? GetFilePath(SyntaxNode? node)
    {
        if (node is null) return null;

        var path = node.SyntaxTree?.FilePath;
        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>节点跨度长度。</summary>
    public static int GetLength(SyntaxNode node)
        => node?.Span.Length ?? 0;
}
