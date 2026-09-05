
using System.Text;

namespace ParameterResolver.Validation.Diagnostics;

public record ValidationDiagnostic(
    string RuleName,
    Severity Severity,
    string Message,
    SourceLocation? SourceLocation = null,
    string? RepairSuggestion = null,
    DateTime Timestamp = default)
{
    public static ValidationDiagnostic CreateError(string ruleName, string message)
        => new(ruleName, Severity.Error, message, Timestamp: DateTime.UtcNow);

    public static ValidationDiagnostic CreateWarning(string ruleName, string message)
        => new(ruleName, Severity.Warning, message, Timestamp: DateTime.UtcNow);

    public static ValidationDiagnostic CreateInfo(string ruleName, string message)
        => new(ruleName, Severity.Info, message, Timestamp: DateTime.UtcNow);

    public ValidationDiagnostic WithLocation(SourceLocation location)
        => this with { SourceLocation = location };

    public ValidationDiagnostic WithSuggestion(string suggestion)
        => this with { RepairSuggestion = suggestion };

    public string ToLogString()
    {
        var sb = new StringBuilder();
        sb.Append('[');
        sb.Append(Severity.ToDisplayString());
        sb.Append("] ");
        sb.Append(RuleName);
        sb.Append(": ");
        sb.Append(Message);

        if (SourceLocation != null)
        {
            sb.Append(" (");
            sb.Append(SourceLocation.ToShortString());
            sb.Append(')');
        }

        if (!string.IsNullOrEmpty(RepairSuggestion))
        {
            sb.Append(" — ");
            sb.Append(RepairSuggestion);
        }

        return sb.ToString();
    }

    public string ToDetailedString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"级别: {Severity.ToDisplayString()}");
        sb.AppendLine($"规则: {RuleName}");
        sb.AppendLine($"消息: {Message}");

        if (SourceLocation != null)
        {
            sb.AppendLine($"位置: {SourceLocation.ToDetailedString()}");
        }

        if (!string.IsNullOrEmpty(RepairSuggestion))
        {
            sb.AppendLine($"建议: {RepairSuggestion}");
        }

        sb.AppendLine($"时间: {Timestamp:yyyy-MM-dd HH:mm:ss}");

        return sb.ToString();
    }
}
