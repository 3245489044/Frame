
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules.Structure;

public class EnumValueRule : ValidationRuleBase
{
    private readonly string _fieldName;
    private readonly string _fieldLabel;
    private readonly IReadOnlySet<string> _allowedValues;

    public EnumValueRule(
        string fieldName,
        string fieldLabel,
        IReadOnlySet<string> allowedValues)
        : base(
            $"EnumValue_{fieldName}",
            $"校验 {fieldLabel}（{fieldName}）的值是否在允许范围内",
            ValidationRuleCategory.Structure)
    {
        _fieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        _fieldLabel = fieldLabel ?? throw new ArgumentNullException(nameof(fieldLabel));
        _allowedValues = allowedValues ?? throw new ArgumentNullException(nameof(allowedValues));
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        FindFieldValues(config, "$", diagnostics, context, cancellationToken);

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }

    private void FindFieldValues(
        JsonElement element,
        string currentPath,
        List<ValidationDiagnostic> diagnostics,
        ConfigValidationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty(_fieldName, out var fieldValue) &&
                    fieldValue.ValueKind == JsonValueKind.String)
                {
                    var value = fieldValue.GetString();
                    if (value != null && !_allowedValues.Contains(value))
                    {
                        var jsonPath = $"{currentPath}.{_fieldName}";
                        var location = new SourceLocation(
                            context.FilePath ?? "unknown",
                            jsonPath);

                        var allowedList = string.Join("、", _allowedValues);

                        diagnostics.Add(
                            ValidationDiagnostic.CreateError(
                                RuleName,
                                $"{_fieldLabel}（{_fieldName}）的值 '{value}' 不在允许范围内")
                            .WithLocation(location)
                            .WithSuggestion($"允许的值: {allowedList}"));
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    FindFieldValues(property.Value, $"{currentPath}.{property.Name}", diagnostics, context, cancellationToken);
                }
                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FindFieldValues(item, $"{currentPath}[{index}]", diagnostics, context, cancellationToken);
                    index++;
                }
                break;
        }
    }
}
