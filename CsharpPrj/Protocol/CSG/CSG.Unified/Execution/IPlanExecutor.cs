namespace CSG.Unified;

public interface IPlanExecutor
{
    Dictionary<string, object?> Execute(ExecutionPlan plan, ITransformEngine engine);
}
