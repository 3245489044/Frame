
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules.Reference;

public class TemplateRefRule : ValidationRuleBase
{
    public TemplateRefRule()
        : base(
            "TemplateRef",
            "校验 template 字段指向的模板是否在模板注册表中存在",
            ValidationRuleCategory.Reference)
    {
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        if (context.TemplateRegistry == null)
        {
            // 未配置提供者 = 该校验功能未启用，属提示性信息而非警告
            diagnostics.Add(
                ValidationDiagnostic.CreateInfo(
                    RuleName,
                    "模板注册表提供者未配置，跳过模板引用校验"));
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
        }

        var templates = JsonTreeWalker.FindStringFieldValues(config, "template", cancellationToken);
        foreach (var (templateName, jsonPath) in templates)
        {
            if (!context.TemplateRegistry!.HasTemplate(templateName))
            {
                var location = new SourceLocation(
                    context.FilePath ?? "unknown",
                    jsonPath);

                var availableTemplates = string.Join("、", context.TemplateRegistry.GetTemplateNames());

                diagnostics.Add(
                    ValidationDiagnostic.CreateError(
                        RuleName,
                        $"模板 '{templateName}' 未在模板注册表中定义")
                    .WithLocation(location)
                    .WithSuggestion($"可用模板: {availableTemplates}"));
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }
}
