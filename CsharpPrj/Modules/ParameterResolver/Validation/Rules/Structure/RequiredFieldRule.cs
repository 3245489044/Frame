
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules.Structure;

public class RequiredFieldRule : ValidationRuleBase
{
    private readonly string _parentPath;
    private readonly string _parentLabel;
    private readonly IReadOnlyList<string> _requiredFields;

    public RequiredFieldRule(
        string parentPath,
        string parentLabel,
        IReadOnlyList<string> requiredFields)
        : base(
            $"RequiredField_{parentLabel}",
            $"校验 {parentLabel} 的必填字段: {string.Join(", ", requiredFields)}",
            ValidationRuleCategory.Structure)
    {
        _parentPath = parentPath ?? throw new ArgumentNullException(nameof(parentPath));
        _parentLabel = parentLabel ?? throw new ArgumentNullException(nameof(parentLabel));
        _requiredFields = requiredFields ?? throw new ArgumentNullException(nameof(requiredFields));
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        if (!TryGetParentElements(config, out var parentElements))
        {
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
        }

        for (int i = 0; i < parentElements.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var element = parentElements[i];
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var field in _requiredFields)
            {
                if (!element.TryGetProperty(field, out var fieldValue) ||
                    fieldValue.ValueKind == JsonValueKind.Null ||
                    (fieldValue.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(fieldValue.GetString())))
                {
                    var jsonPath = $"{_parentPath}[{i}].{field}";
                    var location = new SourceLocation(
                        context.FilePath ?? "unknown",
                        jsonPath);

                    diagnostics.Add(
                        ValidationDiagnostic.CreateError(
                            RuleName,
                            $"{_parentLabel}[{i}] 缺少必填字段 '{field}'")
                        .WithLocation(location)
                        .WithSuggestion($"添加 {field} 字段，如 \"{field}\": \"{GetExampleValue(field)}\""));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }

    private bool TryGetParentElements(JsonElement config, out List<JsonElement> elements)
    {
        elements = new List<JsonElement>();

        var parts = _parentPath.TrimStart('$', '.').Split('.');
        var currentElements = new List<JsonElement> { config };

        foreach (var part in parts)
        {
            var arrayMarker = "[]";
            var propertyName = part.EndsWith(arrayMarker)
                ? part[..^arrayMarker.Length]
                : part;

            var nextElements = new List<JsonElement>();

            foreach (var current in currentElements)
            {
                if (current.ValueKind != JsonValueKind.Object)
                    continue;

                if (!current.TryGetProperty(propertyName, out var next))
                    continue;

                if (part.EndsWith(arrayMarker))
                {
                    if (next.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in next.EnumerateArray())
                        {
                            nextElements.Add(item);
                        }
                    }
                }
                else
                {
                    nextElements.Add(next);
                }
            }

            if (nextElements.Count == 0)
                return false;

            currentElements = nextElements;
        }

        elements = currentElements;
        return true;
    }

    private static string GetExampleValue(string fieldName)
    {
        return fieldName.ToLowerInvariant() switch
        {
            "name" => "myControl",
            "type" => "button",
            "id" => "ctrl_001",
            "key" => "myKey",
            _ => "value"
        };
    }
}
