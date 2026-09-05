using CSG.Unified;

namespace CSG.Unified.Rules;

public class DomainValidationRule : IOptimizationRule
{
    public string Name => "DomainValidation";

    public int Order => 0;

    public IOptimizationContext? Context { get; set; }

    public ExecutionPlan Apply(ITransformBatch batch, ExecutionPlan currentPlan)

    {
        var plan = currentPlan.Clone();

        if (Context == null)
            return plan;

        for (int i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];

            var hints = GetHintsForOperation(Context, step.OperationName);
            if (hints != null)
            {
                step.IsPureFunction = hints.IsPureFunction;
                step.IsIdempotent = hints.IsIdempotent;
            }
        }

        return plan;
    }

    private static DomainOptimizationHints? GetHintsForOperation(IOptimizationContext context, string operationName)
    {
        var registry = context.AdapterRegistry;
        if (registry == null)
            return null;

        try
        {
            var capabilities = registry.GetCapabilities(operationName);
            return capabilities?.GetOptimizationHints(operationName);
        }
        catch
        {
            return null;
        }
    }
}
