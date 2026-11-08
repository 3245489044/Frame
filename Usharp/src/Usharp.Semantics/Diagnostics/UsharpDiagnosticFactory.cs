using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;

namespace Usharp.Semantics;

/// <summary>
/// 诊断构造**单一入口**：严重度只从 <see cref="UsharpDiagnosticCatalog"/> 取，
/// 发射点不得自行选择 Error/Warn/Info（E-9：同一码两处严重度会让 <c>HasError</c> 漂移）。
/// </summary>
public static class UsharpDiagnosticFactory
{
    /// <summary>取码表严重度（未登记 ⇒ 快速失败，禁止静默猜 Error）。</summary>
    /// <param name="code">诊断码。</param>
    public static UsharpSeverity SeverityOf(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return UsharpDiagnosticCatalog.TryGet(code, out var spec)
            ? spec.Severity
            : throw new InvalidOperationException($"诊断码未登记：{code}");
    }

    /// <summary>构造 Usharp 诊断（严重度 = 码表）。</summary>
    /// <param name="code">诊断码。</param>
    /// <param name="message">报文。</param>
    /// <param name="at">归因来源。</param>
    public static UsharpDiagnostic Create(string code, string message, UsharpSourceRef at)
        => new(SeverityOf(code), code, message, at.File, at.Line);

    /// <summary>构造 Usharp 诊断（文件 + 行）。</summary>
    public static UsharpDiagnostic Create(string code, string message, string? file, int line)
        => Create(code, message, UsharpSourceRef.At(file, line));

    /// <summary>构造内核诊断（闸A sink 用；严重度 = 码表，未登记 ⇒ 抛，与 <see cref="SeverityOf"/> 同口径）。</summary>
    public static ConversionDiagnostic Conversion(string code, string message, UsharpSourceRef at)
    {
        var span = new SourceSpan(at.File, at.Line, 0);
        var severity = SeverityOf(code);
        return severity switch
        {
            UsharpSeverity.Error => ConversionDiagnostic.Error(code, message, span),
            UsharpSeverity.Warning => ConversionDiagnostic.Warning(code, message, span),
            _ => ConversionDiagnostic.Info(code, message, span),
        };
    }

    /// <summary>构造内核诊断（文件 + 行）。</summary>
    public static ConversionDiagnostic Conversion(string code, string message, string? file, int line)
        => Conversion(code, message, UsharpSourceRef.At(file, line));
}
