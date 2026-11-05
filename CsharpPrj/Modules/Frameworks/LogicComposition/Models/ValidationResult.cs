
namespace Share.LogicComposition.Models
{
    public class ValidationResult
    {
        private readonly List<string> _errors = new();
        private readonly List<string> _warnings = new();

        public bool IsValid => _errors.Count == 0;

        public IReadOnlyList<string> Errors => _errors;

        public IReadOnlyList<string> Warnings => _warnings;

        public void AddError(string error) => _errors.Add(error);

        public void AddWarning(string warning) => _warnings.Add(warning);

        public void Merge(ValidationResult other)
        {
            if (other == null) return;
            _errors.AddRange(other._errors);
            _warnings.AddRange(other._warnings);
        }

        public string GetErrorString() => string.Join(Environment.NewLine, _errors);

        public string GetSummary()
        {
            var parts = new List<string>();
            if (_errors.Count > 0)
                parts.Add($"Errors ({_errors.Count}):\n{GetErrorString()}");
            if (_warnings.Count > 0)
                parts.Add($"Warnings ({_warnings.Count}):\n{string.Join(Environment.NewLine, _warnings)}");
            return parts.Count > 0 ? string.Join("\n\n", parts) : "Validation passed";
        }

        public static ValidationResult Success() => new();

        public static ValidationResult Failure(params string[] errors)
        {
            var result = new ValidationResult();
            foreach (var error in errors)
                result.AddError(error);
            return result;
        }
    }
}
