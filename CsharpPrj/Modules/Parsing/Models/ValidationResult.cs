namespace Modules.Parsing.Models;

public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationError> Errors { get; }

    public ValidationResult(List<ValidationError> errors)
    {
        Errors = errors ?? new List<ValidationError>();
    }

    public static ValidationResult Success() => new(new List<ValidationError>());
}

public class ValidationError
{
    public string PropertyName { get; }
    public string ErrorMessage { get; }

    public ValidationError(string propertyName, string errorMessage)
    {
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
    }
}
