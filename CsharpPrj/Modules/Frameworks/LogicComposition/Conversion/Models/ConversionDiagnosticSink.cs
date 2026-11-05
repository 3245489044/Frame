
using System;
using System.Collections.Generic;

namespace Share.LogicComposition.Conversion.Models
{
    /// <summary>
    /// 诊断汇集器：转换各阶段写入，管线末端一次性读取。
    /// </summary>
    public interface IConversionDiagnosticSink
    {
        void Report(ConversionDiagnostic diagnostic);

        IReadOnlyList<ConversionDiagnostic> Diagnostics { get; }

        bool HasErrors { get; }
    }

    /// <summary>默认诊断汇集器（有序、稳定，供 <see cref="ConversionContext"/> 缺省注入）。</summary>
    public sealed class DiagnosticSink : IConversionDiagnosticSink
    {
        private readonly List<ConversionDiagnostic> _diagnostics = new List<ConversionDiagnostic>();

        public IReadOnlyList<ConversionDiagnostic> Diagnostics => _diagnostics;

        public bool HasErrors
        {
            get
            {
                for (var i = 0; i < _diagnostics.Count; i++)
                {
                    if (_diagnostics[i].Severity == ConversionSeverity.Error) return true;
                }
                return false;
            }
        }

        public void Report(ConversionDiagnostic diagnostic)
        {
            if (diagnostic is null) throw new ArgumentNullException(nameof(diagnostic));
            _diagnostics.Add(diagnostic);
        }
    }
}
