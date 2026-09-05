
namespace TConfig.Abstractions
{
    public class JsonValidationResult
    {
        public bool IsValid { get; }
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }

        public JsonValidationResult(bool isValid, IEnumerable<string>? errors = null, IEnumerable<string>? warnings = null)
        {
            IsValid = isValid;
            Errors = errors?.ToList().AsReadOnly() ?? Array.Empty<string>().AsReadOnly();
            Warnings = warnings?.ToList().AsReadOnly() ?? Array.Empty<string>().AsReadOnly();
        }

        public static JsonValidationResult Success() => new(true);
        public static JsonValidationResult Failure(params string[] errors) => new(false, errors);
    }
}
