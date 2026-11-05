
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules.Structure;

public class LayoutFormatRule : ValidationRuleBase
{
    public LayoutFormatRule()
        : base(
            "LayoutFormat",
            "校验 layout 字段的百分比值在 0~1 范围内",
            ValidationRuleCategory.Structure)
    {
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        var layouts = JsonTreeWalker.FindLayoutObjects(config, cancellationToken);
        foreach (var (layoutElement, layoutPath) in layouts)
        {
            ValidateLayoutObject(layoutElement, layoutPath, diagnostics, context);
        }

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }

    private void ValidateLayoutObject(
        JsonElement layoutElement,
        string layoutPath,
        List<ValidationDiagnostic> diagnostics,
        ConfigValidationContext context)
    {
        foreach (var property in layoutElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number)
            {
                var value = property.Value.GetDouble();
                if (value < 0.0 || value > 1.0)
                {
                    var jsonPath = $"{layoutPath}.{property.Name}";
                    var location = new SourceLocation(
                        context.FilePath ?? "unknown",
                        jsonPath);

                    diagnostics.Add(
                        ValidationDiagnostic.CreateWarning(
                            RuleName,
                            $"layout.{property.Name} 的值 {value} 超出 0~1 范围")
                        .WithLocation(location)
                        .WithSuggestion($"将值调整为 0~1 之间的百分比值，当前值为 {value}"));
                }
            }
        }
    }
}
