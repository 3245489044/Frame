
namespace Common.PlaceholderFramework.SchemaValidation;

public enum ValidationSeverity
{
    Warning,

    Error
}

public class ValidationError
{
    public string TargetName { get; }

    public string TargetType { get; }

    public string Message { get; }

    public ValidationSeverity Severity { get; }

    public ValidationError(string targetName, string targetType,
        string message,
        ValidationSeverity severity = ValidationSeverity.Error)
    {
        TargetName = targetName ?? "(unnamed)";
        TargetType = targetType ?? "(unknown)";
        Message = message ?? "Unknown validation error";
        Severity = severity;
    }

    public override string ToString()
        => $"[{TargetType}] '{TargetName}': {Message}";
}

public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public List<ValidationError> Errors { get; } = new();

    public List<ValidationError> Warnings { get; } = new();

    public int TotalChecked { get; set; }

    public ValidationResult() { }

    public ValidationResult(IEnumerable<ValidationError> errors)
    {
        Errors.AddRange(errors);
    }

    public string GetFormattedErrors() => GetFormatted("Schema 校验失败", "错误", Errors);

    public string GetFormattedWarnings() => GetFormatted("Schema 校验警告", "个", Warnings);

    private string GetFormatted(string title, string unitSuffix, List<ValidationError> items)
    {
        if (items.Count == 0) return string.Empty;

        var lines = items.Select(e => $"  {e}");
        return $"{title} ({items.Count} {unitSuffix}):\n{string.Join("\n", lines)}";
    }

    public void Merge(ValidationResult other)
    {
        if (other == null) return;
        Errors.AddRange(other.Errors);
        Warnings.AddRange(other.Warnings);
        TotalChecked += other.TotalChecked;
    }
}
