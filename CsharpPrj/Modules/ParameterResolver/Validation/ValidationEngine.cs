
using System.Diagnostics;
using System.Text.Json;
using ParameterResolver.Validation.Diagnostics;
using ParameterResolver.Validation.Rules;

namespace ParameterResolver.Validation;

public class ValidationEngine
{
    private readonly List<IValidationRule> _rules = new();
    private readonly object _lock = new();

    public string Name { get; }

    public int RuleCount
    {
        get { lock (_lock) return _rules.Count; }
    }

    public ValidationEngine(string name = "DefaultValidationEngine")
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public ValidationEngine RegisterRule(IValidationRule rule)
    {
        if (rule == null) throw new ArgumentNullException(nameof(rule));

        lock (_lock)
        {
            if (_rules.Any(r => r.RuleName == rule.RuleName))
            {
                throw new InvalidOperationException(
                    $"规则 '{rule.RuleName}' 已注册");
            }
            _rules.Add(rule);
        }

        return this;
    }

    public ValidationEngine RegisterRules(IEnumerable<IValidationRule> rules)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));

        foreach (var rule in rules)
        {
            RegisterRule(rule);
        }

        return this;
    }

    public ValidationEngine UnregisterRule(string ruleName)
    {
        if (ruleName == null) throw new ArgumentNullException(nameof(ruleName));

        lock (_lock)
        {
            _rules.RemoveAll(r => r.RuleName == ruleName);
        }

        return this;
    }

    public IReadOnlyList<IValidationRule> GetRules()
    {
        lock (_lock)
        {
            return _rules.ToList().AsReadOnly();
        }
    }

    public IReadOnlyList<IValidationRule> GetRulesByCategory(ValidationRuleCategory category)
    {
        lock (_lock)
        {
            return _rules.Where(r => r.Category == category).ToList().AsReadOnly();
        }
    }

    public async Task<ValidationReport> ValidateAsync(
        JsonElement config,
        ConfigValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var report = new ValidationReport(Name);

        if (context.IsSilent)
        {
            stopwatch.Stop();
            report.Duration = stopwatch.Elapsed;
            return report;
        }

        var orderedRules = GetRulesInExecutionOrder();

        foreach (var categoryGroup in orderedRules.GroupBy(r => r.Category))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var categoryDiagnostics = await ExecuteCategoryAsync(
                categoryGroup.Key,
                categoryGroup.ToList(),
                config,
                context,
                cancellationToken);

            report.AddDiagnostics(categoryDiagnostics);

            if (context.IsStrict && report.ErrorCount > 0)
            {
                break;
            }
        }

        stopwatch.Stop();
        report.Duration = stopwatch.Elapsed;

        return report;
    }

    private async Task<IReadOnlyList<ValidationDiagnostic>> ExecuteCategoryAsync(
        ValidationRuleCategory category,
        List<IValidationRule> rules,
        JsonElement config,
        ConfigValidationContext context,
        CancellationToken cancellationToken)
    {
        var allDiagnostics = new List<ValidationDiagnostic>();

        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var diagnostics = await rule.ValidateAsync(config, context, cancellationToken);
                allDiagnostics.AddRange(diagnostics);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                allDiagnostics.Add(
                    ValidationDiagnostic.CreateError(
                        rule.RuleName,
                        $"执行校验规则时发生异常: {ex.Message}"));
            }
        }

        return allDiagnostics;
    }

    private List<IValidationRule> GetRulesInExecutionOrder()
    {
        var categoryOrder = new[]
        {
            ValidationRuleCategory.Structure,
            ValidationRuleCategory.Reference,
            ValidationRuleCategory.Semantic,
            ValidationRuleCategory.Runtime
        };

        var orderedRules = new List<IValidationRule>();

        lock (_lock)
        {
            foreach (var category in categoryOrder)
            {
                orderedRules.AddRange(
                    _rules.Where(r => r.Category == category));
            }
        }

        return orderedRules;
    }
}
