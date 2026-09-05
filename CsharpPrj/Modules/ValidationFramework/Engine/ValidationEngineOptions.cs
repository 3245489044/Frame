
using ValidationFramework.Abstractions;

namespace ValidationFramework.Engine;

public delegate IEnumerable<IValidationRule> CategoryRuleFactory(ValidationRuleCategory category);

public class ValidationEngineOptions
{
    public string Name { get; set; } = "ConfiguredValidationEngine";

    public bool EnableStructure { get; set; } = true;

    public bool EnableReference { get; set; } = true;

    public bool EnableSemantic { get; set; } = true;

    public bool EnableRuntime { get; set; } = false;

    public bool EnableContract { get; set; } = false;

    public CategoryRuleFactory? RuleFactory { get; set; }

    public IReadOnlyList<IValidationRule> AdditionalRules { get; set; }
        = Array.Empty<IValidationRule>();

    public ValidationEngineOptions ShallowCopy()
    {
        var copy = (ValidationEngineOptions)MemberwiseClone();
        copy.AdditionalRules = AdditionalRules.ToList().AsReadOnly();
        return copy;
    }

    public ValidationEngine Build()
    {
        var engine = new ValidationEngine(Name);

        if (RuleFactory != null)
        {
            foreach (var category in ValidationCategoryOrder.GetEnabled(this))
            {
                var rules = RuleFactory(category)?.ToList() ?? new List<IValidationRule>();
                engine.RegisterRules(rules);
            }
        }

        engine.RegisterUnrestrictedRules(AdditionalRules);

        engine.RestrictToCategories(ValidationCategoryOrder.GetEnabled(this));

        return engine;
    }
}