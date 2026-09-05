
using System.Text.Json;
using Plate.JsonMethod.Serialization;

namespace Plate.JsonMethod.Validation
{
    public class JsonValidationResult
    {
        public bool IsValid { get; private set; }
        public string[] Errors { get; private set; }
        public string[] Warnings { get; private set; }

        private JsonValidationResult(bool isValid, string[]? errors = null, string[]? warnings = null)
        {
            IsValid = isValid;
            Errors = errors ?? Array.Empty<string>();
            Warnings = warnings ?? Array.Empty<string>();
        }

        public static JsonValidationResult Success() => new(true);
        public static JsonValidationResult Failure(params string[] errors) => new(false, errors);
        public static JsonValidationResult SuccessWithWarnings(params string[] warnings) => new(true, warnings: warnings);
    }

    public class JsonValidationService
    {
        public JsonValidationResult ValidateSyntax(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return JsonValidationResult.Failure("JSON input is null or empty");
            try
            {
                JsonDocument.Parse(json);
                return JsonValidationResult.Success();
            }
            catch (JsonException ex)
            {
                return JsonValidationResult.Failure($"JSON syntax error: {ex.Message}");
            }
        }

        public JsonValidationResult ValidateStructure<T>(string json)
        {
            var (result, errors) = JsonSerializationService.Instance
                .DeserializeWithValidation<T>(json, JsonContext.Default);
            if (errors.Count == 0 && result != null)
                return JsonValidationResult.Success();
            return JsonValidationResult.Failure(errors.ToArray());
        }

        public async Task<JsonValidationResult> ValidateFileAsync(string path)
        {
            if (!File.Exists(path))
                return JsonValidationResult.Failure($"File not found: {path}");
            var json = await File.ReadAllTextAsync(path);
            return ValidateSyntax(json);
        }

        public async Task<JsonValidationResult> ValidateFileStructureAsync<T>(string path)
        {
            if (!File.Exists(path))
                return JsonValidationResult.Failure($"File not found: {path}");
            var json = await File.ReadAllTextAsync(path);
            return ValidateStructure<T>(json);
        }
    }
}
