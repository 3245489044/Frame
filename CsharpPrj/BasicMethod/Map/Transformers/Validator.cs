
namespace Modules.Transformers
{
    public class ValidationResult
    {
        public bool IsValid { get; }
        public string? Error { get; }

        public static ValidationResult Success => new(true, null);
        public static ValidationResult Fail(string? error = null) => new(false, error);

        private ValidationResult(bool isValid, string? error)
            => (IsValid, Error) = (isValid, error);

        public override string ToString()
            => IsValid ? "Valid" : $"Invalid: {Error}";
    }

    public class Validator<T> : ITransformer<T, ValidationResult>
    {
        private readonly Func<T, bool> _rule;

        public string Name { get; }
        public string? ErrorMessage { get; }

        public Validator(string name, Func<T, bool> rule, string? errorMessage = null)
        {
            Name = name;
            _rule = rule ?? throw new ArgumentNullException(nameof(rule));
            ErrorMessage = errorMessage;
        }

        public ValidationResult Transform(T input)
            => _rule(input)
                ? ValidationResult.Success
                : ValidationResult.Fail(ErrorMessage ?? $"Validation failed: {Name}");
    }
}
