
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules.Structure;

public class EventValueTypeRule : ValidationRuleBase
{
    public EventValueTypeRule()
        : base(
            "EventValueType",
            "校验 events 对象中非 bindings 的键值对，值类型必须为 string[] 或 string（单函数速记）",
            ValidationRuleCategory.Structure)
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
            ValidateEventsObject(eventsElement, eventsPath, diagnostics, context);
        }

        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }

    private void ValidateEventsObject(
        JsonElement eventsElement,
        string eventsPath,
        List<ValidationDiagnostic> diagnostics,
        ConfigValidationContext context)
    {
        foreach (var property in eventsElement.EnumerateObject())
        {
            if (property.Name == "bindings")
                continue;

            var value = property.Value;
            var jsonPath = $"{eventsPath}.{property.Name}";

            // 契约（SKILL.md §2.4）：string 为单函数速记（等价于 ["X"]），string[] 为多函数/顺序执行；两者均合法。
            if (value.ValueKind != JsonValueKind.Array && value.ValueKind != JsonValueKind.String)
            {
                var actualType = value.ValueKind switch
                {
                    JsonValueKind.Number => "number",
                    JsonValueKind.Object => "object",
                    JsonValueKind.True or JsonValueKind.False => "boolean",
                    JsonValueKind.Null => "null",
                    _ => value.ValueKind.ToString()
                };

                var location = new SourceLocation(
                    context.FilePath ?? "unknown",
                    jsonPath);

                diagnostics.Add(
                    ValidationDiagnostic.CreateError(
                        RuleName,
                        $"events.{property.Name} 的值类型应为 string[] 或 string（单函数速记），当前为 {actualType}")
                    .WithLocation(location)
                    .WithSuggestion($"将值改为数组或字符串，如 \"{property.Name}\": [\"FunctionName\"] 或 \"{property.Name}\": \"FunctionName\""));
            }
        }
    }
}
