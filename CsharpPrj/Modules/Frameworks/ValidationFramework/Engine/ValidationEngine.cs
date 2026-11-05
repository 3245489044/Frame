
using System.Diagnostics;
using System.Text.Json;
using BasicMethod.Block;
using ValidationFramework.Abstractions;

namespace ValidationFramework.Engine;

public class ValidationEngine
{
    private readonly Dictionary<string, IValidationRule> _rulesByName = new(StringComparer.Ordinal);
    private readonly Dictionary<ValidationRuleCategory, List<IValidationRule>> _rulesByCategory = new();
    private readonly List<IValidationRule> _unrestrictedRules = new();
    private HashSet<ValidationRuleCategory>? _enabledCategories;
    private readonly object _lock = new();

    public string Name { get; }

    public int RuleCount
    {
        get { lock (_lock) return _rulesByName.Count; }
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
            if (_rulesByName.ContainsKey(rule.RuleName))
            {
                throw new InvalidOperationException($"规则 '{rule.RuleName}' 已注册");
            }

            _rulesByName[rule.RuleName] = rule;

            if (!_rulesByCategory.TryGetValue(rule.Category, out var list))
            {
                list = new List<IValidationRule>();
                _rulesByCategory[rule.Category] = list;
            }
            list.Add(rule);
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

    public ValidationEngine RegisterUnrestrictedRule(IValidationRule rule)
    {
        if (rule == null) throw new ArgumentNullException(nameof(rule));

        lock (_lock)
        {
            if (_rulesByName.ContainsKey(rule.RuleName))
            {
                throw new InvalidOperationException($"规则 '{rule.RuleName}' 已注册");
            }

            _rulesByName[rule.RuleName] = rule;
            _unrestrictedRules.Add(rule);
        }

        return this;
    }

    public ValidationEngine RegisterUnrestrictedRules(IEnumerable<IValidationRule> rules)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));

        foreach (var rule in rules)
        {
            RegisterUnrestrictedRule(rule);
        }

        return this;
    }

    public ValidationEngine UnregisterRule(string ruleName)
    {
        if (ruleName == null) throw new ArgumentNullException(nameof(ruleName));

        lock (_lock)
        {
            if (_rulesByName.Remove(ruleName, out var removed))
            {
                _unrestrictedRules.Remove(removed);
                if (_rulesByCategory.TryGetValue(removed.Category, out var list))
                {
                    list.Remove(removed);
                    if (list.Count == 0) _rulesByCategory.Remove(removed.Category);
                }
            }
        }

        return this;
    }

    public IReadOnlyList<IValidationRule> GetRules()
    {
        lock (_lock)
        {
            return _rulesByName.Values.ToList().AsReadOnly();
        }
    }

    public IReadOnlyList<IValidationRule> GetRulesByCategory(ValidationRuleCategory category)
    {
        lock (_lock)
        {
            var result = new List<IValidationRule>();
            if (_rulesByCategory.TryGetValue(category, out var list))
            {
                result.AddRange(list);
            }
            result.AddRange(_unrestrictedRules.Where(r => r.Category == category));
            return result.AsReadOnly();
        }
    }

    public ValidationEngine RestrictToCategories(IEnumerable<ValidationRuleCategory>? categories)
    {
        lock (_lock)
        {
            _enabledCategories = categories?.ToHashSet();
        }
        return this;
    }

    public async Task<ValidationReport> ValidateAsync(
        JsonElement config,
        IValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (context.IsSilent)
        {
            stopwatch.Stop();
            return ValidationReport.Empty(Name, stopwatch.Elapsed);
        }

        var allDiagnostics = new List<ValidationDiagnostic>();

        List<IValidationRule> unrestrictedSnapshot;
        lock (_lock) unrestrictedSnapshot = _unrestrictedRules.ToList();

        if (unrestrictedSnapshot.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var unrestrictedDiagnostics = await ExecuteCategoryAsync(
                "Unrestricted",
                unrestrictedSnapshot,
                config,
                context,
                cancellationToken);

            allDiagnostics.AddRange(unrestrictedDiagnostics);
        }

        foreach (var categoryGroup in GetCategoryGroupsInExecutionOrder())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var categoryDiagnostics = await ExecuteCategoryAsync(
                $"Category:{categoryGroup.Key}",
                categoryGroup.Value,
                config,
                context,
                cancellationToken);

            allDiagnostics.AddRange(categoryDiagnostics);

            if (context.IsStrict && allDiagnostics.Any(d => d.Severity == Severity.Error))
            {
                break;
            }
        }

        stopwatch.Stop();
        return ValidationReport.Create(Name, allDiagnostics, stopwatch.Elapsed);
    }

    private async Task<IReadOnlyList<ValidationDiagnostic>> ExecuteCategoryAsync(
        string stageName,
        List<IValidationRule> rules,
        JsonElement config,
        IValidationContext context,
        CancellationToken cancellationToken)
    {
        var pipeline = new ExecutionPipeline(stageName);
        var ruleContext = new RuleBlockContext
        {
            Config = config,
            ValidationContext = context
        };

        foreach (var rule in rules)
        {
            pipeline.AddBlock(new RuleBlockAdapter(rule));
        }

        var blockContext = new BlockContext { CancellationToken = cancellationToken };
        RuleBlockAdapter.SetRuleContext(blockContext, ruleContext);

        var result = await pipeline.RunAsync(blockContext, cancellationToken);

        var diagnostics = RuleBlockAdapter.CollectDiagnosticsFrom(blockContext).ToList();
        foreach (var failed in result.Diagnostics.Where(d => d.Status == BlockStatus.Failed))
        {
            var ruleName = failed.BlockName;
            diagnostics.Add(ValidationDiagnostic.Error(
                ruleName,
                $"执行校验规则时发生异常: {failed.Message}"));
        }

        return diagnostics;
    }

    private IReadOnlyList<KeyValuePair<ValidationRuleCategory, List<IValidationRule>>> GetCategoryGroupsInExecutionOrder()
    {
        lock (_lock)
        {
            var result = new List<KeyValuePair<ValidationRuleCategory, List<IValidationRule>>>();
            foreach (var category in ValidationCategoryOrder.Default)
            {
                if (_enabledCategories != null && !_enabledCategories.Contains(category)) continue;
                if (_rulesByCategory.TryGetValue(category, out var list) && list.Count > 0)
                {
                    result.Add(new KeyValuePair<ValidationRuleCategory, List<IValidationRule>>(category, list.ToList()));
                }
            }
            return result;
        }
    }
}
