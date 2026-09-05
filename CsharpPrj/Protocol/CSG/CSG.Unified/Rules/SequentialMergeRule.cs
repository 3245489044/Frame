using CSG.Unified;

namespace CSG.Unified.Rules;

public class SequentialMergeRule : IOptimizationRule
{
    public string Name => "SequentialMerge";

    public int Order => 2;

    public ExecutionPlan Apply(ITransformBatch batch, ExecutionPlan currentPlan)

    {
        var plan = currentPlan.Clone();

        var merged = new bool[plan.Steps.Count];
        var mergeGroups = new List<List<int>>();

        for (int i = 0; i < plan.Steps.Count; i++)
        {
            if (merged[i]) continue;
            if (!plan.Steps[i].IsPureFunction) continue;

            var group = new List<int> { i };
            merged[i] = true;

            for (int j = i + 1; j < plan.Steps.Count; j++)
            {
                if (!plan.Steps[j].IsPureFunction) break;

                var deps = plan.Steps[j].Dependencies;
                if (deps.Count == 1 && deps[0] == plan.Steps[j - 1].StepName)
                {
                    group.Add(j);
                    merged[j] = true;
                }
                else
                {
                    break;
                }
            }

            if (group.Count > 1)
            {
                mergeGroups.Add(group);
            }
        }

        if (mergeGroups.Count > 0)
        {
            var newSteps = new List<PlanStep>();
            var mergeGroupSet = new HashSet<int>(mergeGroups.SelectMany(g => g));

            for (int i = 0; i < plan.Steps.Count; i++)
            {
                if (mergeGroupSet.Contains(i))
                {
                    var group = mergeGroups.First(g => g.Contains(i));
                    if (i == group[0])
                    {
                        var firstStep = plan.Steps[group[0]];
                        var lastStep = plan.Steps[group.Last()];
                        newSteps.Add(new PlanStep
                        {
                            StepName = $"{firstStep.StepName}_merged",
                            OperationName = firstStep.OperationName,
                            ReferenceStepName = firstStep.ReferenceStepName,
                            Input = firstStep.Input,
                            Dependencies = new List<string>(firstStep.Dependencies),
                            IsPureFunction = true,
                            Description = $"合并步骤: {string.Join(" → ", group.Select(idx => plan.Steps[idx].StepName))}"
                        });
                    }
                }
                else
                {
                    newSteps.Add(plan.Steps[i]);
                }
            }

            plan.Steps = newSteps;

            plan.ParallelGroups = Enumerable.Range(0, plan.Steps.Count)
                .Select(i => new HashSet<int> { i })
                .ToList();
        }

        return plan;
    }
}
