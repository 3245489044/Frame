namespace CSG.Unified;

public class ExecutionPlan
{
    public List<PlanStep> Steps { get; set; } = new();

    public List<HashSet<int>> ParallelGroups { get; set; } = new();

    public ValidationResult PreValidation { get; set; } = new();

    public bool HasErrors => !PreValidation.IsValid;

    public static ExecutionPlan CreateBasePlan(ITransformBatch batch)


    {
        var plan = new ExecutionPlan();

        var steps = batch.GetStepData();

        foreach (var step in steps)
        {
            plan.Steps.Add(new PlanStep
            {
                StepName = step.StepName,
                OperationName = step.OperationName,
                Input = step.IsReference ? null : step.Input,
                ReferenceStepName = step.IsReference ? (string)step.Input! : null
            });
        }

        for (int i = 0; i < plan.Steps.Count; i++)
        {
            plan.ParallelGroups.Add(new HashSet<int> { i });
        }

        return plan;
    }


    public ExecutionPlan Clone()

    {
        var cloned = new ExecutionPlan
        {
            Steps = Steps.Select(s => new PlanStep
            {
                StepName = s.StepName,
                OperationName = s.OperationName,
                ReferenceStepName = s.ReferenceStepName,
                Input = s.Input,
                Dependencies = new List<string>(s.Dependencies),
                IsPureFunction = s.IsPureFunction,
                IsIdempotent = s.IsIdempotent,
                Description = s.Description
            }).ToList(),
            ParallelGroups = ParallelGroups.Select(g => new HashSet<int>(g)).ToList(),
            PreValidation = new ValidationResult()
        };
        cloned.PreValidation.Merge(PreValidation);
        return cloned;
    }

    public override string ToString()
        => $"ExecutionPlan: {Steps.Count} steps, {ParallelGroups.Count} groups, " +
           $"Errors: {PreValidation.Errors.Count}, Warnings: {PreValidation.Warnings.Count}";
}
