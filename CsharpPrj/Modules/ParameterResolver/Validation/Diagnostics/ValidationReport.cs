
using System.Text;
using System.Text.Json;

namespace ParameterResolver.Validation.Diagnostics;

public class ValidationReport
{
    public string TargetName { get; }

    public bool IsValid { get; private set; }

    public IReadOnlyList<ValidationDiagnostic> Diagnostics => _diagnostics.AsReadOnly();

    public int ErrorCount { get; private set; }

    public int WarningCount { get; private set; }

    public int InfoCount { get; private set; }

    public DateTime ExecutedAt { get; }

    public TimeSpan Duration { get; set; }

    private readonly List<ValidationDiagnostic> _diagnostics = new();

    public ValidationReport(string targetName, DateTime? executedAt = null)
    {
        TargetName = targetName ?? throw new ArgumentNullException(nameof(targetName));
        ExecutedAt = executedAt ?? DateTime.UtcNow;
        IsValid = true;
    }

    public ValidationReport AddDiagnostic(ValidationDiagnostic diagnostic)
    {
        if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));

        _diagnostics.Add(diagnostic);

        switch (diagnostic.Severity)
        {
            case Severity.Error:
                ErrorCount++;
                IsValid = false;
                break;
            case Severity.Warning:
                WarningCount++;
                break;
            case Severity.Info:
                InfoCount++;
                break;
        }

        return this;
    }

    public ValidationReport AddDiagnostics(IEnumerable<ValidationDiagnostic> diagnostics)
    {
        if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));

        foreach (var diagnostic in diagnostics)
        {
            AddDiagnostic(diagnostic);
        }

        return this;
    }

    public ValidationReport MergeFrom(IEnumerable<ValidationReport> reports)
    {
        if (reports == null) throw new ArgumentNullException(nameof(reports));

        foreach (var report in reports)
        {
            if (report == null) continue;
            AddDiagnostics(report.Diagnostics);
        }

        return this;
    }

    public ValidationReport MergeFrom(ValidationReport report)
    {
        if (report == null) throw new ArgumentNullException(nameof(report));
        return MergeFrom(new[] { report });
    }

    public string ToJson(bool prettyPrint = true)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = prettyPrint,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var reportObj = new
        {
            TargetName,
            IsValid,
            ExecutedAt,
            Duration = Duration.ToString(@"hh\:mm\:ss\.fff"),
            Summary = new
            {
                ErrorCount,
                WarningCount,
                InfoCount,
                TotalCount = _diagnostics.Count
            },
            Diagnostics = _diagnostics.Select(d => new
            {
                d.RuleName,
                Severity = d.Severity.ToString(),
                SeverityDisplay = d.Severity.ToDisplayString(),
                d.Message,
                SourceLocation = d.SourceLocation == null ? null : new
                {
                    d.SourceLocation.FilePath,
                    d.SourceLocation.JsonPath,
                    d.SourceLocation.LineNumber,
                    d.SourceLocation.ColumnNumber,
                    d.SourceLocation.ContextSnippet
                },
                d.RepairSuggestion,
                Timestamp = d.Timestamp
            })
        };

        return JsonSerializer.Serialize(reportObj, options);
    }

    public string ToDiagnosticString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine($"校验报告: {TargetName}");
        sb.AppendLine($"结果: {(IsValid ? "通过" : "失败")}");
        sb.AppendLine($"耗时: {Duration.TotalMilliseconds:F0}ms");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine($"错误: {ErrorCount} | 警告: {WarningCount} | 信息: {InfoCount}");
        sb.AppendLine("----------------------------------------");

        if (_diagnostics.Count == 0)
        {
            sb.AppendLine("无诊断信息。");
        }
        else
        {
            foreach (var diagnostic in _diagnostics)
            {
                sb.AppendLine(diagnostic.ToLogString());
            }
        }

        sb.AppendLine("========================================");

        return sb.ToString();
    }

    public IReadOnlyList<ValidationDiagnostic> GetBySeverity(Severity severity)
        => _diagnostics.Where(d => d.Severity == severity).ToList().AsReadOnly();

    public IReadOnlyList<ValidationDiagnostic> GetByRule(string ruleName)
        => _diagnostics.Where(d => d.RuleName == ruleName).ToList().AsReadOnly();
}
