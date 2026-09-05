using System.Collections.Generic;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public class SecurityCheckResult
    {
        public bool IsClean { get; set; }

        public IReadOnlyList<SecurityIssue> Issues { get; set; } = new List<SecurityIssue>();

        public IReadOnlyList<CompilationError> CompilationErrors { get; set; } = new List<CompilationError>();

        public int TotalIssues { get; set; }

        public int TotalErrors { get; set; }

        public IDictionary<ErrorSeverity, int> IssueCountBySeverity { get; set; } = new Dictionary<ErrorSeverity, int>();
    }

    public class SecurityIssue
    {
        public string RuleId { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public ErrorSeverity Severity { get; set; } = ErrorSeverity.SecurityError;

        public int? Line { get; set; }

        public int? Column { get; set; }

        public string CodeSnippet { get; set; }
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
    }

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

}
