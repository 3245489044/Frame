
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ValidationFramework.Abstractions;

namespace ValidationFramework.Feedback;

public interface IValidationFeedbackSink
{
    void Emit(ValidationReport report, string? jsonFeedback);
}

public class ValidationFeedbackFormatter
{
    private readonly bool _includeInfo;

    public ValidationFeedbackFormatter(bool includeInfo = false)
    {
        _includeInfo = includeInfo;
    }

    public string ToLlmFeedback(ValidationReport report)
    {
        var payload = new
        {
            isValid = report.IsValid,
            errorCount = report.ErrorCount,
            warningCount = report.WarningCount,
            fixes = report.ProjectDiagnostics(_includeInfo)
        };

        return JsonSerializer.Serialize(payload,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
    }

    public string ToTextSummary(ValidationReport report)
    {
        if (report.IsValid && report.WarningCount == 0)
        {
            return $"[OK] {report.ValidatorName}: 校验通过";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[FAIL] {report.ValidatorName}: {report.ErrorCount} 错误, {report.WarningCount} 警告");
        foreach (var d in report.Diagnostics.Where(d => d.Severity != Severity.Info))
        {
            sb.AppendLine($"  [{d.Severity}] {d.RuleName}: {d.Message} ({d.SourceLocation})");
            if (!string.IsNullOrEmpty(d.RepairSuggestion))
            {
                sb.AppendLine($"    -> 修复: {d.RepairSuggestion}");
            }
        }
        return sb.ToString().TrimEnd();
    }
}

public class DefaultFeedbackSink : IValidationFeedbackSink
{
    private readonly Action<string> _logger;
    private readonly ValidationFeedbackFormatter _formatter = new();

    public DefaultFeedbackSink(Action<string>? logger = null)
    {
        _logger = logger ?? (s => Console.Out.WriteLine(s));
    }

    public void Emit(ValidationReport report, string? jsonFeedback)
    {
        var summary = _formatter.ToTextSummary(report);
        _logger(summary);

        if (!string.IsNullOrEmpty(jsonFeedback))
        {
            _logger(jsonFeedback);
        }
    }
}
