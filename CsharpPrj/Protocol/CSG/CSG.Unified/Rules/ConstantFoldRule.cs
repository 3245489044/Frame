using CSG.Unified;

namespace CSG.Unified.Rules;

public class ConstantFoldRule : IOptimizationRule
{
    public string Name => "ConstantFold";

    public int Order => 4;

    public ExecutionPlan Apply(ITransformBatch batch, ExecutionPlan currentPlan)

    {
        var plan = currentPlan.Clone();

        var foldableIndices = new List<int>();
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            if (step.ReferenceStepName == null && step.IsIdempotent)
            {
                foldableIndices.Add(i);
            }
        }

        foreach (var index in foldableIndices)
        {
            plan.Steps[index].Description = (plan.Steps[index].Description + " [常量折叠]").TrimStart();
        }

        return plan;
    }
}
