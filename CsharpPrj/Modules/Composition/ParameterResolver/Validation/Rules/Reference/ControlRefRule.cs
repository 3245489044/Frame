
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules.Reference;

public class ControlRefRule : ValidationRuleBase
{
    public ControlRefRule()
        : base(
            "ControlRef",
            "校验 bindings.parameters[].source 引用的控件名是否在配置树中存在",
            ValidationRuleCategory.Reference)
    {
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        if (context.ControlTree == null)
        {
            // 未配置提供者 = 该校验功能未启用，属提示性信息而非警告
            diagnostics.Add(
                ValidationDiagnostic.CreateInfo(
                    RuleName,
                    "控件树提供者未配置，跳过控件引用校验"));
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
        }

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
            if (paramItem.ValueKind == JsonValueKind.Object &&
                paramItem.TryGetProperty("source", out var sourceValue) &&
                sourceValue.ValueKind == JsonValueKind.String)
            {
                // 字面量绑定（sourceType="literal"）的 source 是常量而非控件名，跳过控件引用校验。
                var isLiteral = paramItem.TryGetProperty("sourceType", out var sourceType)
                                && sourceType.ValueKind == JsonValueKind.String
                                && string.Equals(sourceType.GetString(), "literal", StringComparison.OrdinalIgnoreCase);

                if (!isLiteral)
                {
                    var source = sourceValue.GetString();
                    if (!string.IsNullOrEmpty(source) && !context.ControlTree!.HasControl(source))
                    {
                        var jsonPath = $"{parametersPath}[{index}].source";
                        var location = new SourceLocation(
                            context.FilePath ?? "unknown",
                            jsonPath);

                        diagnostics.Add(
                            ValidationDiagnostic.CreateError(
                                RuleName,
                                $"参数 '{GetParameterTarget(paramItem)}' 的 source 引用控件 '{source}' 不存在")
                            .WithLocation(location)
                            .WithSuggestion($"确保控件名 '{source}' 在配置树中已定义，或检查是否有拼写错误"));
                    }
                }
            }
            index++;
        }
    }

    private static string GetParameterTarget(JsonElement paramItem)
    {
        if (paramItem.TryGetProperty("target", out var target) &&
            target.ValueKind == JsonValueKind.String)
        {
            return target.GetString() ?? "unknown";
        }
        return "unknown";
    }
}
