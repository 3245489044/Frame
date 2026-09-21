using Share.LogicComposition.Conversion.Models;

namespace Usharp.Semantics;

/// <summary>
/// 内核诊断 → Usharp 公共诊断的无损投影：保持 Usharp 公共面（<see cref="UsharpDiagnostic"/>）稳定，
/// 内核诊断类型不泄漏到 Usharp 消费方（零回归：既有 174 处引用不变）。
/// 列号 Usharp 不产出 → 投影时丢弃。
/// </summary>
internal static class UsharpDiagnosticProjection
{
    public static UsharpDiagnostic ToUsharp(ConversionDiagnostic d)
    {
        var file = d.Span.File ?? string.Empty;
        var line = d.Span.Line;
        return d.Severity switch
        {
            ConversionSeverity.Error => UsharpDiagnostic.Error(d.Code, d.Message, file, line),
            ConversionSeverity.Warning => UsharpDiagnostic.Warn(d.Code, d.Message, file, line),
            _ => UsharpDiagnostic.Info(d.Code, d.Message, file, line),
        };
    }
}
