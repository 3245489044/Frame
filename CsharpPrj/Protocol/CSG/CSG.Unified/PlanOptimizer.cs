using CSG.Unified;

namespace CSG.Unified;

public class PlanOptimizer : IOptimizationContext
{
    private readonly List<IOptimizationRule> _rules = new();
    private readonly ITransformEngine _engine;
    private IAdapterRegistry? _adapterRegistry;

    public PlanOptimizer(ITransformEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public ITransformEngine Engine => _engine;

    ITransformEngine? IOptimizationContext.Engine => _engine;

    IAdapterRegistry? IOptimizationContext.AdapterRegistry => _adapterRegistry;

    public PlanOptimizer WithAdapterRegistry(IAdapterRegistry registry)
    {
        _adapterRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
        return this;
    }

    public PlanOptimizer WithRule(IOptimizationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var index = _rules.BinarySearch(rule, Comparer<IOptimizationRule>.Create((a, b) => a.Order.CompareTo(b.Order)));
        if (index < 0) index = ~index;
        _rules.Insert(index, rule);
        return this;
    }

    public PlanOptimizer WithRules(IEnumerable<IOptimizationRule> rules)
    {
        foreach (var rule in rules)
        {
            WithRule(rule);
        }
        return this;
    }

    public IReadOnlyList<IOptimizationRule> Rules => _rules.AsReadOnly();

    public ExecutionPlan Optimize(ITransformBatch batch)

    {
        ArgumentNullException.ThrowIfNull(batch);

        var plan = ExecutionPlan.CreateBasePlan(batch);

        if (_rules.Count == 0)
            return plan;

        foreach (var rule in _rules)
        {
            try
            {
                plan = rule.Apply(batch, plan);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PlanOptimizer] 规则 '{rule.Name}' 执行失败: {ex.Message}");
            }
        }

        return plan;
    }

    public string GetInfo()
    {
        var ruleNames = _rules.Count > 0
            ? string.Join(", ", _rules.Select(r => $"{r.Name}(Order={r.Order})"))
            : "(无规则)";
        return $"PlanOptimizer: {_rules.Count} 条规则 [{ruleNames}]";
    }
}
