using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using RoslynSpace.RoslynSyntaxUtilities.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Analysis
{
    public static class CompilationErrorAnalyzer
    {
        public static List<CompilationError> Analyze(CSharpCompilation compilation)
        {
            if (compilation == null)
                return new List<CompilationError>();

            return compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d =>
                {
                    int? line = null;
                    int? column = null;
                    if (RoslynSourceLocator.TryGetPosition(d, out _, out var positionLine, out var positionColumn))
                    {
                        line = positionLine;
                        column = positionColumn;
                    }

                    var severity = d.Id switch
                    {
                        string id when id.StartsWith("CS") => ErrorSeverity.CompilationError,
                        _ => ErrorSeverity.SyntaxError
                    };

                    return new CompilationError(d.Id, d.GetMessage(), line, column, severity);
                })
                .ToList();
        }

        public static List<CompilationError> AnalyzeSyntaxErrors(SyntaxTree syntaxTree)
        {
            if (syntaxTree == null)
                return new List<CompilationError>();

            return syntaxTree.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d =>
                {
                    int? line = null;
                    int? column = null;
                    if (RoslynSourceLocator.TryGetPosition(d, out _, out var positionLine, out var positionColumn))
                    {
                        line = positionLine;
                        column = positionColumn;
                    }

                    return new CompilationError(d.Id, d.GetMessage(), line, column, ErrorSeverity.SyntaxError);
                })
                .ToList();
        }
    }
}
