
using System;
using Share.LogicComposition.Conversion.Models;

namespace Share.LogicComposition.Conversion.Abstractions
{
    /// <summary>
    /// 转换阶段异常：携带稳定错误码与源位置，由 <c>DefaultConversionPipeline</c> 统一转诊断。
    /// </summary>
    public sealed class ConversionStageException : Exception
    {
        /// <summary>稳定错误码。</summary>
        public string Code { get; }

        /// <summary>源位置（可为 <see cref="SourceSpan.None"/>）。</summary>
        public SourceSpan Span { get; }

        public ConversionStageException(string code, string message, SourceSpan span = default)
            : base(message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Span = span;
        }

        public ConversionStageException(string code, string message, SourceSpan span, Exception innerException)
            : base(message, innerException)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Span = span;
        }
    }
}
