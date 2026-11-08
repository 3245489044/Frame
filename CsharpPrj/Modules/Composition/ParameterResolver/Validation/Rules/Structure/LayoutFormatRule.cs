
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;

namespace ParameterResolver.Validation.Rules.Structure;

public class LayoutFormatRule : ValidationRuleBase
{
    public LayoutFormatRule()
        : base(
            "LayoutFormat",
            "按字段语义校验 layout 数值（比例 0~1 / DIP·像素·优先级 >=0 / 跨度 >=1）",
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
            if (property.Value.ValueKind != JsonValueKind.Number) continue;

            var value = property.Value.GetDouble();
            var jsonPath = $"{layoutPath}.{property.Name}";
            var location = new SourceLocation(context.FilePath ?? "unknown", jsonPath);

            if (!LayoutFieldSemantics.TryGet(property.Name, out var kind))
            {
                // 未登记字段：不猜语义（禁把「所有数字」当比例），仅提示补齐语义表。
                diagnostics.Add(
                    ValidationDiagnostic.CreateInfo(
                        RuleName,
                        $"layout.{property.Name} 未登记字段语义（跳过范围校验；请在 LayoutFieldSemantics 登记）")
                    .WithLocation(location));
                continue;
            }

            var (valid, expectation) = kind switch
            {
                LayoutFieldKind.Ratio => (value is >= 0.0 and <= 1.0, "0~1 的比例"),
                LayoutFieldKind.Dip => (value >= 0, ">=0 的 DIP"),
                LayoutFieldKind.Pixel => (value >= 0, ">=0 的像素"),
                LayoutFieldKind.Priority => (value >= 0, ">=0 的优先级（0=刚性）"),
                LayoutFieldKind.Span => (value >= 1, ">=1 的跨度"),
                _ => (value >= 0, ">=0 的序号"),
            };

            if (valid) continue;

            diagnostics.Add(
                ValidationDiagnostic.CreateWarning(
                    RuleName,
                    $"layout.{property.Name} 的值 {value} 不符合字段语义（期望 {expectation}）")
                .WithLocation(location)
                .WithSuggestion($"按 {kind} 语义调整 {property.Name}（当前值 {value}）"));
        }
    }
}
