
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Share.LogicComposition.Conversion.Models;

namespace Share.RoslynJson.Diagnostics;

/// <summary>
/// 转换内核诊断 → Roslyn 诊断的唯一桥（把 <see cref="SourceSpan"/> 还原为编译器可点击的 <see cref="Location"/>）。
/// </summary>
public static class RoslynDiagnosticBridge
{
    /// <summary>源位置 → Roslyn 位置（无文件信息时返回 <see cref="Location.None"/>）。</summary>
    public static Location ToLocation(SourceSpan span)
    {
        if (string.IsNullOrEmpty(span.File)) return Location.None;

        var line = span.Line > 0 ? span.Line - 1 : 0;
        var column = span.Column > 0 ? span.Column - 1 : 0;
        var length = span.Length > 0 ? span.Length : 0;

        var start = new LinePosition(line, column);
        var end = new LinePosition(line, column + length);

        return Location.Create(span.File!, new TextSpan(0, 0), new LinePositionSpan(start, end));
    }

    /// <summary>严重度换算。</summary>
    public static DiagnosticSeverity ToSeverity(ConversionSeverity severity) => severity switch
    {
        ConversionSeverity.Error => DiagnosticSeverity.Error,
        ConversionSeverity.Warning => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Info,
    };

    /// <summary>单条换算（描述符由调用方按错误码选定；缺省用 <paramref name="fallback"/>）。</summary>
    public static Diagnostic ToDiagnostic(ConversionDiagnostic diagnostic, DiagnosticDescriptor fallback)
    {
        if (diagnostic is null) throw new ArgumentNullException(nameof(diagnostic));
        if (fallback is null) throw new ArgumentNullException(nameof(fallback));

        return Diagnostic.Create(
            descriptor: fallback,
            location: ToLocation(diagnostic.Span),
            messageArgs: new object[] { diagnostic.Code, diagnostic.Message });
    }

    /// <summary>批量换算。</summary>
    public static IEnumerable<Diagnostic> ToDiagnostics(IEnumerable<ConversionDiagnostic> diagnostics, DiagnosticDescriptor fallback)
    {
        if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));

        foreach (var diagnostic in diagnostics)
        {
            yield return ToDiagnostic(diagnostic, fallback);
        }
    }
}
