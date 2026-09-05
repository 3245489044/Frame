
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValidationFramework.Abstractions;

public enum Severity
{
    Info = 0,

    Warning = 1,

    Error = 2
}

public record SourceLocation(
    string? Source,
    string? JsonPath = null,
    int? Line = null,
    int? Column = null)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(Source)) parts.Add(Source);
        if (!string.IsNullOrEmpty(JsonPath)) parts.Add(JsonPath);
        if (Line.HasValue)
        {
            parts.Add(Column.HasValue
                ? $"({Line},{Column})"
                : $"({Line})");
        }
        return parts.Count > 0 ? string.Join(" - ", parts) : "(unknown)";
    }
}

public class ValidationDiagnostic
{
    public string RuleName { get; }

    public string Message { get; }

    public Severity Severity { get; }

    public SourceLocation? SourceLocation { get; init; }

    public string? RepairSuggestion { get; init; }

    public IReadOnlyDictionary<string, object?>? Properties { get; init; }

    private ValidationDiagnostic(
        string ruleName,
        string message,
        Severity severity,
        SourceLocation? sourceLocation = null,
        string? repairSuggestion = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        RuleName = ruleName ?? throw new ArgumentNullException(nameof(ruleName));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Severity = severity;
        SourceLocation = sourceLocation;
        RepairSuggestion = repairSuggestion;
        Properties = properties;
    }

    public static ValidationDiagnostic Error(
        string ruleName, string message,
        SourceLocation? location = null,
        string? repairSuggestion = null,
        IReadOnlyDictionary<string, object?>? properties = null)
        => new(ruleName, message, Severity.Error, location, repairSuggestion, properties);

    public static ValidationDiagnostic Warning(
        string ruleName, string message,
        SourceLocation? location = null,
        string? repairSuggestion = null,
        IReadOnlyDictionary<string, object?>? properties = null)
        => new(ruleName, message, Severity.Warning, location, repairSuggestion, properties);

    public static ValidationDiagnostic Info(
        string ruleName, string message,
        SourceLocation? location = null,
        string? repairSuggestion = null,
        IReadOnlyDictionary<string, object?>? properties = null)
        => new(ruleName, message, Severity.Info, location, repairSuggestion, properties);

    public ValidationDiagnostic WithLocation(SourceLocation? location)
        => new(RuleName, Message, Severity, location, RepairSuggestion, Properties);

    public ValidationDiagnostic WithSuggestion(string? suggestion)
        => new(RuleName, Message, Severity, SourceLocation, suggestion, Properties);

    public override string ToString()
        => $"[{Severity}] {RuleName}: {Message} ({SourceLocation})";
}

public class ValidationReport
{
    public string ValidatorName { get; }

    public IReadOnlyList<ValidationDiagnostic> Diagnostics { get; }

    public int ErrorCount { get; }

    public int WarningCount { get; }

    public int InfoCount { get; }

    public bool IsValid => ErrorCount == 0;

    public TimeSpan Duration { get; set; }

    private ValidationReport(
        string validatorName,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        TimeSpan duration,
        int errorCount,
        int warningCount,
        int infoCount)
    {
        ValidatorName = validatorName;
        Diagnostics = diagnostics;
        ErrorCount = errorCount;
        WarningCount = warningCount;
        InfoCount = infoCount;
        Duration = duration;
    }

    public static ValidationReport Empty(string validatorName, TimeSpan? duration = null)
        => new(validatorName, Array.Empty<ValidationDiagnostic>(), duration ?? TimeSpan.Zero, 0, 0, 0);

    public static ValidationReport Create(
        string validatorName,
        IEnumerable<ValidationDiagnostic> diagnostics,
        TimeSpan? duration = null)
    {
        var list = diagnostics.ToList().AsReadOnly();
        return new ValidationReport(
            validatorName,
            list,
            duration ?? TimeSpan.Zero,
            list.Count(d => d.Severity == Severity.Error),
            list.Count(d => d.Severity == Severity.Warning),
            list.Count(d => d.Severity == Severity.Info));
    }

    public static ValidationReport Create(
        string validatorName,
        IEnumerable<ValidationDiagnostic> diagnostics,
        TimeSpan? duration,
        int errorCount,
        int warningCount,
        int infoCount)
        => new(
            validatorName,
            diagnostics.ToList().AsReadOnly(),
            duration ?? TimeSpan.Zero,
            errorCount,
            warningCount,
            infoCount);

    public IReadOnlyList<object> ProjectDiagnostics(bool includeInfo = false)
        => Diagnostics
            .Where(d => d.Severity != Severity.Info || includeInfo)
            .Select(d => new
            {
                severity = d.Severity.ToString(),
                rule = d.RuleName,
                message = d.Message,
                path = d.SourceLocation?.JsonPath,
                source = d.SourceLocation?.Source,
                line = d.SourceLocation?.Line,
                column = d.SourceLocation?.Column,
                fix = d.RepairSuggestion
            })
            .ToList();

    public string ToJson(bool indent = true)
    {
        var payload = new
        {
            validatorName = ValidatorName,
            isValid = IsValid,
            errorCount = ErrorCount,
            warningCount = WarningCount,
            infoCount = InfoCount,
            durationMs = Duration.TotalMilliseconds,
            diagnostics = ProjectDiagnostics(includeInfo: true)
        };

        return JsonSerializer.Serialize(payload,
            new JsonSerializerOptions
            {
                WriteIndented = indent,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
    }
}