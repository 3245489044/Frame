namespace CSG.Unified;

public class ValidationResult
{
    public bool IsValid { get; private set; } = true;

    public List<ValidationError> Errors { get; init; } = new();

    public List<ValidationWarning> Warnings { get; init; } = new();

    public void AddError(string rule, string message, string? sourceId = null, string? targetId = null)
    {
        IsValid = false;
        Errors.Add(new ValidationError(CsgErrorCode.Unspecified, rule, message, sourceId, targetId));
    }

    public void AddError(CsgErrorCode code, string rule, string message, string? sourceId = null, string? targetId = null)
    {
        IsValid = false;
        Errors.Add(new ValidationError(code, rule, message, sourceId, targetId));
    }

    public void AddWarning(string rule, string message, string? sourceId = null, string? targetId = null)
    {
        Warnings.Add(new ValidationWarning(rule, message, sourceId, targetId));
    }

    public void Merge(ValidationResult other)
    {
        if (!other.IsValid) IsValid = false;
        Errors.AddRange(other.Errors);
        Warnings.AddRange(other.Warnings);
    }

    public override string ToString()
    {
        return $"Valid: {IsValid}, Errors: {Errors.Count}, Warnings: {Warnings.Count}";
    }
}
