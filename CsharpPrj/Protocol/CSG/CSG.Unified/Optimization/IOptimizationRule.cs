namespace CSG.Unified;

public interface IOptimizationRule
{
    string Name { get; }

    int Order { get; }

    ExecutionPlan Apply(ITransformBatch batch, ExecutionPlan currentPlan);

}
