
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules.Reference;

public class FunctionRefRule : ValidationRuleBase
{
    public FunctionRefRule()
        : base(
            "FunctionRef",
            "校验 events.xxx 引用的函数名是否在函数注册表中存在",
            ValidationRuleCategory.Reference)
    {
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        if (context.FunctionRegistry == null)
        {
            // 未配置提供者 = 该校验功能未启用，属提示性信息而非警告
            diagnostics.Add(
                ValidationDiagnostic.CreateInfo(
                    RuleName,
                    "函数注册表提供者未配置，跳过函数引用校验"));
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
        }

        var functionArrays = JsonTreeWalker.FindEventFunctionArrays(config, cancellationToken);
        foreach (var (eventName, funcArray, eventsPath) in functionArrays)
        {
            int funcIndex = 0;
            foreach (var funcItem in funcArray.EnumerateArray())
            {
                if (funcItem.ValueKind == JsonValueKind.String)
                {
                    var functionName = funcItem.GetString();
                    if (!string.IsNullOrEmpty(functionName) &&
                        !context.FunctionRegistry!.HasFunction(functionName))
                    {
                        var jsonPath = $"{eventsPath}[{funcIndex}]";
                        var location = new SourceLocation(
                            context.FilePath ?? "unknown",
                            jsonPath);

                        var availableFunctions = string.Join("、", context.FunctionRegistry.GetFunctionNames());

                        diagnostics.Add(
                            ValidationDiagnostic.CreateError(
                                RuleName,
                                $"事件 '{eventName}' 引用的函数 '{functionName}' 未在函数注册表中定义")
                            .WithLocation(location)
                            .WithSuggestion($"可用函数: {availableFunctions}"));
                    }
                }
                funcIndex++;
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }
}
