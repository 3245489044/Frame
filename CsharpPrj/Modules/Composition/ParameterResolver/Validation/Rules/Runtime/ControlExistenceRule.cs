
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules.Runtime;

public class ControlExistenceRule : ValidationRuleBase
{
    public ControlExistenceRule()
        : base(
            "ControlExistence",
            "校验配置中引用的控件在运行时 UI 树中是否存在",
            ValidationRuleCategory.Runtime)
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
            diagnostics.Add(
                ValidationDiagnostic.CreateWarning(
                    RuleName,
                    "控件树提供者未配置，跳过运行时控件存在性校验"));
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
        }

        var referencedControls = new List<(string name, string path)>();

        var parametersList = JsonTreeWalker.FindBindingsParameters(config, cancellationToken);
        foreach (var (parameters, path) in parametersList)
        {
            int index = 0;
            foreach (var paramItem in parameters.EnumerateArray())
            {
                if (paramItem.ValueKind == JsonValueKind.Object &&
                    paramItem.TryGetProperty("source", out var source) &&
                    source.ValueKind == JsonValueKind.String)
                {
                    var sourceName = source.GetString();
                    if (!string.IsNullOrEmpty(sourceName))
                        referencedControls.Add((sourceName, $"{path}[{index}].source"));
                }
                index++;
            }
        }

        var outputList = JsonTreeWalker.FindBindingsOutput(config, cancellationToken);
        foreach (var (outputValue, path) in outputList)
        {
            referencedControls.Add((outputValue, path));
        }

        foreach (var (controlName, jsonPath) in referencedControls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!context.ControlTree.HasControl(controlName))
            {
                var location = new SourceLocation(
                    context.FilePath ?? "unknown",
                    jsonPath);

                diagnostics.Add(
                    ValidationDiagnostic.CreateError(
                        RuleName,
                        $"运行时控件 '{controlName}' 不存在")
                    .WithLocation(location)
                    .WithSuggestion($"确保控件 '{controlName}' 已在运行时 UI 树中注册"));
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }
}
