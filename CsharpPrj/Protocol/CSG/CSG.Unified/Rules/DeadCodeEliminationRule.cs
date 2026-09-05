using CSG.Unified;

namespace CSG.Unified.Rules;

public class DeadCodeEliminationRule : IOptimizationRule
{
    public string Name => "DeadCodeElimination";

    public int Order => 3;

    public ExecutionPlan Apply(ITransformBatch batch, ExecutionPlan currentPlan)

    {
        var plan = currentPlan.Clone();
        if (plan.Steps.Count <= 1) return plan;

        var dependsOnMe = new Dictionary<string, List<string>>();
        foreach (var step in plan.Steps)
        {
            dependsOnMe[step.StepName] = new List<string>();
        }

        foreach (var step in plan.Steps)
        {
            foreach (var dep in step.Dependencies)
            {
                if (dependsOnMe.ContainsKey(dep))
                {
                    dependsOnMe[dep].Add(step.StepName);
                }
            }
        }

        var reachable = new HashSet<string>();
        var lastStepName = plan.Steps.Last().StepName;
        var queue = new Queue<string>();
        queue.Enqueue(lastStepName);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!reachable.Add(current)) continue;

            if (dependsOnMe.TryGetValue(current, out var dependents))
            {
                foreach (var dependent in dependents)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        var removedCount = plan.Steps.RemoveAll(s => !reachable.Contains(s.StepName));

        if (removedCount > 0)
        {
            plan.ParallelGroups = Enumerable.Range(0, plan.Steps.Count)
                .Select(i => new HashSet<int> { i })
                .ToList();
        }

        return plan;
    }
}
