namespace CSG.Unified;

public class PlanStep
{
    public required string StepName { get; init; }

    public required string OperationName { get; init; }

    public string? ReferenceStepName { get; init; }

    public object? Input { get; init; }

    public List<string> Dependencies { get; init; } = new();

    public bool IsPureFunction { get; set; }

    public bool IsIdempotent { get; set; }

    public string Description { get; set; } = string.Empty;


    public override string ToString()
        => $"{StepName}: {OperationName}" +
           (ReferenceStepName != null ? $" ← {ReferenceStepName}" : "");
}
