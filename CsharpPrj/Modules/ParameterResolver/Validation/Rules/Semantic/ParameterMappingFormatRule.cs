
using System.Text.RegularExpressions;
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules.Semantic;

public partial class ParameterMappingFormatRule : ValidationRuleBase
{
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*)*$")]
    private static partial Regex TargetFormatRegex();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_-]*$")]
    private static partial Regex SourceFormatRegex();

    public ParameterMappingFormatRule()
        : base(
            "ParameterMappingFormat",
            "校验 bindings.parameters[].target 和 source 的格式是否符合规范",
            ValidationRuleCategory.Semantic)
    {
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        var parametersList = JsonTreeWalker.FindBindingsParameters(config, cancellationToken);
        foreach (var (parametersArray, parametersPath) in parametersList)
        {
            ValidateParameters(parametersArray, parametersPath, diagnostics, context);
        }

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }

    private void ValidateParameters(
        JsonElement parametersArray,
        string parametersPath,
        List<ValidationDiagnostic> diagnostics,
        ConfigValidationContext context)
    {
        int index = 0;
        foreach (var paramItem in parametersArray.EnumerateArray())
        {
            if (paramItem.ValueKind != JsonValueKind.Object)
            {
                index++;
                continue;
            }

            if (paramItem.TryGetProperty("target", out var targetValue) &&
                targetValue.ValueKind == JsonValueKind.String)
            {
                var target = targetValue.GetString();
                if (!string.IsNullOrEmpty(target) && !TargetFormatRegex().IsMatch(target))
                {
                    var jsonPath = $"{parametersPath}[{index}].target";
                    var location = new SourceLocation(
                        context.FilePath ?? "unknown",
                        jsonPath);

                    diagnostics.Add(
                        ValidationDiagnostic.CreateWarning(
                            RuleName,
                            $"parameters[{index}].target 格式不规范: '{target}'")
                        .WithLocation(location)
                        .WithSuggestion("target 格式应为字母开头，允许字母、数字、下划线、点号，如 \"paramName\" 或 \"paramName.subField\""));
                }
            }

            if (paramItem.TryGetProperty("source", out var sourceValue) &&
                sourceValue.ValueKind == JsonValueKind.String)
            {
                var source = sourceValue.GetString();
                if (!string.IsNullOrEmpty(source) && !SourceFormatRegex().IsMatch(source))
                {
                    var jsonPath = $"{parametersPath}[{index}].source";
                    var location = new SourceLocation(
                        context.FilePath ?? "unknown",
                        jsonPath);

                    diagnostics.Add(
                        ValidationDiagnostic.CreateWarning(
                            RuleName,
                            $"parameters[{index}].source 格式不规范: '{source}'")
                        .WithLocation(location)
                        .WithSuggestion("source 格式应为字母开头，允许字母、数字、下划线、中划线，如 \"controlName\""));
                }
            }

            index++;
        }
    }
}
