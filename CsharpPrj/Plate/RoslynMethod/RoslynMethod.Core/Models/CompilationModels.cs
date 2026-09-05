using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace RoslynSpace.RoslynSyntaxUtilities.Core.Models
{
    public enum ErrorSeverity
    {
        SyntaxError,
        SemanticError,
        CompilationError,
        EmitError,
        ValidationError,
        SecurityError,
        SystemError
    }

    public class CompilationError
    {
        public string Id { get; set; }
        public string Message { get; set; }
        public int? Line { get; set; }
        public int? Column { get; set; }
        public ErrorSeverity Severity { get; set; }

        public CompilationError() { }

        public CompilationError(string id, string message, int? line, int? column, ErrorSeverity severity)
        {
            Id = id;
            Message = message;
            Line = line;
            Column = column;
            Severity = severity;
        }

        public static CompilationError FromDiagnostic(Diagnostic diagnostic, ErrorSeverity severity)
        {
            var span = diagnostic.Location.GetLineSpan();
            return new CompilationError(
                diagnostic.Id,
                diagnostic.GetMessage(),
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1,
                severity);
        }

        public static CompilationError FromLineSpan(string id, string message, FileLinePositionSpan? lineSpan, ErrorSeverity severity)
        {
            return new CompilationError(
                id,
                message,
                lineSpan?.StartLinePosition.Line + 1,
                lineSpan?.StartLinePosition.Character + 1,
                severity);
        }

        public override string ToString() =>
            $"{Severity} {Id}: {Message} (Line: {Line}, Column: {Column})";
    }

    public class CompilationSuggestion
    {
        public string ErrorId { get; set; }
        public string Message { get; set; }
        public string[] Suggestions { get; set; }

        public CompilationSuggestion() { }

        public CompilationSuggestion(string errorId, string message, string[] suggestions)
        {
            ErrorId = errorId;
            Message = message;
            Suggestions = suggestions;
        }
    }

    public class LineError
    {
        public string Message { get; set; }
        public int Column { get; set; }
        public ErrorSeverity Severity { get; set; }

        public LineError() { }

        public LineError(string message, int column, ErrorSeverity severity)
        {
            Message = message;
            Column = column;
            Severity = severity;
        }
    }

    public class CompilationErrorReport
    {
        public bool IsSuccess { get; set; }
        public int TotalErrors { get; set; }
        public List<CompilationError> ErrorList { get; set; } = new();
        public string ErrorSummary { get; set; }
        public Dictionary<int, List<LineError>> ErrorsByLine { get; set; } = new();

        public CompilationErrorReport() { }

        public CompilationErrorReport(bool isSuccess, int totalErrors, List<CompilationError> errorList, string errorSummary)
        {
            IsSuccess = isSuccess;
            TotalErrors = totalErrors;
            ErrorList = errorList;
            ErrorSummary = errorSummary;
        }
    }

    public class ErrorStatistics
    {
        public int TotalErrors { get; set; }
        public int SyntaxErrors { get; set; }
        public int SemanticErrors { get; set; }
        public int CompilationErrors { get; set; }
        public int ErrorLines { get; set; }

        public ErrorStatistics() { }

        public ErrorStatistics(int totalErrors, int syntaxErrors, int semanticErrors, int compilationErrors, int errorLines)
        {
            TotalErrors = totalErrors;
            SyntaxErrors = syntaxErrors;
            SemanticErrors = semanticErrors;
            CompilationErrors = compilationErrors;
            ErrorLines = errorLines;
        }
    }

    public class CompilationValidationResult
    {
        public bool IsValid { get; set; }
        public List<CompilationError> Errors { get; set; } = new();
        public List<CompilationSuggestion> Suggestions { get; set; } = new();
        public int TotalErrors => Errors?.Count ?? 0;
    }

    public class SnippetValidationResult
    {
        public bool IsValid { get; set; }
        public List<CompilationError> Errors { get; set; } = new();
        public string WrappedCode { get; set; }
    }
}
