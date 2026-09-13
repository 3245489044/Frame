
using System;

namespace Share.LogicComposition.Conversion.Models
{
    /// <summary>
    /// 转换诊断：稳定错误码 + 人类可读消息 + 源位置。
    /// 采用「收集后统一报告」而非抛异常，对齐声明式测试框架「全量失败可见」原则。
    /// </summary>
    public sealed class ConversionDiagnostic
    {
        public ConversionSeverity Severity { get; }

        /// <summary>稳定错误码（如 <c>CVT-EMIT-NULL</c>），供测试断言与文档索引。</summary>
        public string Code { get; }

        public string Message { get; }

        public SourceSpan Span { get; }

        public ConversionDiagnostic(ConversionSeverity severity, string code, string message, SourceSpan span = default)
        {
            Severity = severity;
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? string.Empty;
            Span = span;
        }

        public static ConversionDiagnostic Error(string code, string message, SourceSpan span = default)
            => new ConversionDiagnostic(ConversionSeverity.Error, code, message, span);

        public static ConversionDiagnostic Warning(string code, string message, SourceSpan span = default)
            => new ConversionDiagnostic(ConversionSeverity.Warning, code, message, span);

        public static ConversionDiagnostic Info(string code, string message, SourceSpan span = default)
            => new ConversionDiagnostic(ConversionSeverity.Info, code, message, span);

        /// <summary>渲染为可读单行：有定位时前缀源位置。</summary>
        public string Format()
            => Span.HasPosition ? $"{Span}: {Code} {Message}" : $"{Code}: {Message}";

        public override string ToString() => Format();
    }
}
