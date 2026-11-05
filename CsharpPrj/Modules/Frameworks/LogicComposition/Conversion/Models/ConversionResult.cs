
using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.LogicComposition.Conversion.Models
{
    /// <summary>
    /// 转换结果：产物 + 诊断 + 成功判定（<see cref="IsSuccess"/> ⇔ 无 Error 级诊断）。
    /// 形态对齐既有 <c>Share.LogicComposition.Models.ValidationResult</c>（收集 + 摘要），
    /// 但承载泛型产物以便直接串联发射器。
    /// </summary>
    public sealed class ConversionResult<T>
    {
        /// <summary>产物（失败时可能仍携带部分产物，供诊断/对拍使用）。</summary>
        public T? Artifact { get; }

        public bool IsSuccess { get; }

        public IReadOnlyList<ConversionDiagnostic> Diagnostics { get; }

        /// <summary>首条 Error 的摘要（无 Error 时为 null）。</summary>
        public string? ErrorMessage { get; }

        private ConversionResult(T? artifact, bool isSuccess, IReadOnlyList<ConversionDiagnostic>? diagnostics)
        {
            Artifact = artifact;
            IsSuccess = isSuccess;
            Diagnostics = diagnostics ?? Array.Empty<ConversionDiagnostic>();
            ErrorMessage = Diagnostics.FirstOrDefault(d => d.Severity == ConversionSeverity.Error)?.Format();
        }

        public static ConversionResult<T> Success(T artifact, IReadOnlyList<ConversionDiagnostic>? diagnostics = null)
            => new ConversionResult<T>(artifact, true, diagnostics);

        public static ConversionResult<T> Failure(IReadOnlyList<ConversionDiagnostic> diagnostics, T? partialResult = default)
            => new ConversionResult<T>(partialResult, false, diagnostics);

        public static ConversionResult<T> Failure(string code, string message, SourceSpan span = default)
            => Failure(new[] { ConversionDiagnostic.Error(code, message, span) });

        /// <summary>全部诊断的可读多行文本。</summary>
        public string GetDiagnosticsString()
            => Diagnostics.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, Diagnostics.Select(d => d.Format()));

        /// <summary>诊断摘要（无诊断时为「无诊断」）。</summary>
        public string GetSummary()
            => Diagnostics.Count == 0
                ? "无诊断"
                : string.Join(Environment.NewLine, Diagnostics.Select(d => d.Format()));
    }
}
