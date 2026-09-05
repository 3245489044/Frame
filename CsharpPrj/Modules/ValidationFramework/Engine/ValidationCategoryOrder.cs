
using ValidationFramework.Abstractions;

namespace ValidationFramework.Engine;

public static class ValidationCategoryOrder
{
    public static readonly ValidationRuleCategory[] Default =
    {
        ValidationRuleCategory.Structure,
        ValidationRuleCategory.Reference,
        ValidationRuleCategory.Semantic,
        ValidationRuleCategory.Runtime,
        ValidationRuleCategory.Contract
    };

    public static bool IsEnabled(ValidationEngineOptions options, ValidationRuleCategory category)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        return category switch
        {
            ValidationRuleCategory.Structure => options.EnableStructure,
            ValidationRuleCategory.Reference => options.EnableReference,
            ValidationRuleCategory.Semantic => options.EnableSemantic,
            ValidationRuleCategory.Runtime => options.EnableRuntime,
            ValidationRuleCategory.Contract => options.EnableContract,
            _ => false
        };
    }

    public static IReadOnlyList<ValidationRuleCategory> GetEnabled(ValidationEngineOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        var result = new List<ValidationRuleCategory>();
        foreach (var category in Default)
        {
            if (IsEnabled(options, category)) result.Add(category);
        }
        return result;
    }
}
