
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules.Semantic;

public class EventBindingFormatRule : ValidationRuleBase
{
    public EventBindingFormatRule()
        : base(
            "EventBindingFormat",
            "校验 events.bindings 的格式是否符合规范",
            ValidationRuleCategory.Semantic)
    {
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        var eventsList = JsonTreeWalker.FindEventsObjects(config, cancellationToken);
        foreach (var (eventsElement, eventsPath) in eventsList)
        {
            ValidateBindings(eventsElement, eventsPath, diagnostics, context);
        }

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }

    private void ValidateBindings(
        JsonElement eventsElement,
        string eventsPath,
        List<ValidationDiagnostic> diagnostics,
        ConfigValidationContext context)
    {
        if (!eventsElement.TryGetProperty("bindings", out var bindingsElement))
        {
            return;
        }

        if (bindingsElement.ValueKind != JsonValueKind.Object)
        {
            var location = new SourceLocation(
                context.FilePath ?? "unknown",
                $"{eventsPath}.bindings");

            diagnostics.Add(
                ValidationDiagnostic.CreateError(
                    RuleName,
                    "events.bindings 必须为 object 类型")
                .WithLocation(location)
                .WithSuggestion("将 bindings 改为对象格式，如 \"bindings\": { \"parameters\": [...] }"));
            return;
        }

        var hasParameters = bindingsElement.TryGetProperty("parameters", out var parametersElement);
        var hasOutput = bindingsElement.TryGetProperty("output", out _);

        // 契约（SKILL.md §2）：bindings 可仅含 output（output-only 绑定，parameters 缺省）。
        if (!hasParameters && !hasOutput)
        {
            var location = new SourceLocation(
                context.FilePath ?? "unknown",
                $"{eventsPath}.bindings");

            diagnostics.Add(
                ValidationDiagnostic.CreateWarning(
                    RuleName,
                    "events.bindings 既无 parameters 也无 output（空绑定）")
                .WithLocation(location)
                .WithSuggestion("添加 parameters 数组或 output 目标控件，如 \"output\": \"resultTextBox\""));
            return;
        }

        // output-only 绑定：parameters 可缺省，无需逐项校验。
        if (!hasParameters)
            return;

        if (parametersElement.ValueKind != JsonValueKind.Array)
        {
            var location = new SourceLocation(
                context.FilePath ?? "unknown",
                $"{eventsPath}.bindings.parameters");

            diagnostics.Add(
                ValidationDiagnostic.CreateError(
                    RuleName,
                    "events.bindings.parameters 必须为数组类型")
                .WithLocation(location)
                .WithSuggestion("将 parameters 改为数组格式"));
            return;
        }

        int index = 0;
        foreach (var paramItem in parametersElement.EnumerateArray())
        {
            if (paramItem.ValueKind != JsonValueKind.Object)
            {
                var location = new SourceLocation(
                    context.FilePath ?? "unknown",
                    $"{eventsPath}.bindings.parameters[{index}]");

                diagnostics.Add(
                    ValidationDiagnostic.CreateError(
                        RuleName,
                        $"parameters[{index}] 必须为 object 类型")
                    .WithLocation(location));
                index++;
                continue;
            }

            if (!paramItem.TryGetProperty("target", out var targetValue) ||
                targetValue.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(targetValue.GetString()))
            {
                var location = new SourceLocation(
                    context.FilePath ?? "unknown",
                    $"{eventsPath}.bindings.parameters[{index}]");

                diagnostics.Add(
                    ValidationDiagnostic.CreateError(
                        RuleName,
                        $"parameters[{index}] 缺少必填字段 'target' 或值无效")
                    .WithLocation(location)
                    .WithSuggestion("添加 target 字段，如 \"target\": \"paramName\""));
            }

            if (!paramItem.TryGetProperty("source", out var sourceValue) ||
                sourceValue.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(sourceValue.GetString()))
            {
                var location = new SourceLocation(
                    context.FilePath ?? "unknown",
                    $"{eventsPath}.bindings.parameters[{index}]");

                diagnostics.Add(
                    ValidationDiagnostic.CreateError(
                        RuleName,
                        $"parameters[{index}] 缺少必填字段 'source' 或值无效")
                    .WithLocation(location)
                    .WithSuggestion("添加 source 字段，如 \"source\": \"controlName\""));
            }

            // P5·值语义校验：semantic（可选）必须是合法值语义（Text/Selection/Content/Checked/Range/Path）
            if (paramItem.TryGetProperty("semantic", out var semanticValue)
                && semanticValue.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(semanticValue.GetString())
                && !Enum.TryParse<TConfig.Controls.ValueSemantic>(semanticValue.GetString(), ignoreCase: true, out _))
            {
                var location = new SourceLocation(
                    context.FilePath ?? "unknown",
                    $"{eventsPath}.bindings.parameters[{index}]");

                diagnostics.Add(
                    ValidationDiagnostic.CreateWarning(
                        RuleName,
                        $"parameters[{index}] 的 semantic 值 '{semanticValue.GetString()}' 不是合法的值语义")
                    .WithLocation(location)
                    .WithSuggestion("使用 Text/Selection/Content/Checked/Range/Path 之一，或移除该字段使用契约默认语义"));
            }

            index++;
        }
    }
}
