using CSG.Unified;

namespace CSG.Unified.Engine;

public class PlanExecutor : IPlanExecutor
{
    public Dictionary<string, object?> Execute(ExecutionPlan plan, ITransformEngine engine)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(engine);

        if (plan.HasErrors)
            throw new InvalidOperationException(
                $"计划验证失败，无法执行: {plan.PreValidation.Errors.Count} 个错误");

        var results = new Dictionary<string, object?>();

        if (plan.ParallelGroups.Count == 0)
        {
            foreach (var step in plan.Steps)
            {
                results[step.StepName] = ExecuteStep(engine, step, results);
            }
            return results;
        }

        foreach (var group in plan.ParallelGroups)
        {
            if (group.Count == 1)
            {
                var stepIndex = group.First();
                var step = plan.Steps[stepIndex];
                results[step.StepName] = ExecuteStep(engine, step, results);
            }
            else
            {
                Parallel.ForEach(group, stepIndex =>
                {
                    var step = plan.Steps[stepIndex];
                    var result = ExecuteStep(engine, step, results);
                    lock (results)
                    {
                        results[step.StepName] = result;
                    }
                });
            }
        }

        return results;
    }

    private static object? ExecuteStep(ITransformEngine engine, PlanStep step, Dictionary<string, object?> previousResults)
    {
        object input;

        if (step.ReferenceStepName != null)
        {
            if (!previousResults.TryGetValue(step.ReferenceStepName, out var previousResult))
            {
                throw new InvalidOperationException(
                    $"步骤 '{step.StepName}' 引用的前一步 '{step.ReferenceStepName}' 不存在或尚未执行");
            }
            input = previousResult ?? throw new InvalidOperationException(
                $"步骤 '{step.StepName}' 引用的前一步 '{step.ReferenceStepName}' 结果为 null");
        }
        else
        {
            input = step.Input!;
        }

        return engine.CallOperation(step.OperationName, input);
    }
}
