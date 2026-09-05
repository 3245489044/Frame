
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules.Reference;

public class OutputTargetRule : ValidationRuleBase
{
    public OutputTargetRule()
        : base(
            "OutputTarget",
            "校验 bindings.output 引用的控件名是否在配置树中存在",
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
                    "控件树提供者未配置，跳过输出目标校验"));
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
        }

        var outputs = JsonTreeWalker.FindBindingsOutput(config, cancellationToken);
        foreach (var (outputTarget, jsonPath) in outputs)
        {
            if (!context.ControlTree!.HasControl(outputTarget))
            {
                var location = new SourceLocation(
                    context.FilePath ?? "unknown",
                    jsonPath);

                diagnostics.Add(
                    ValidationDiagnostic.CreateError(
                        RuleName,
                        $"output 目标控件 '{outputTarget}' 不存在")
                    .WithLocation(location)
                    .WithSuggestion($"确保控件名 '{outputTarget}' 在配置树中已定义，或检查是否有拼写错误"));
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }
}
