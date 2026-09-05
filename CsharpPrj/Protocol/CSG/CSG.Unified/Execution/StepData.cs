namespace CSG.Unified;

public record StepData
{
    public required string StepName { get; init; }

    public required string OperationName { get; init; }

    public object? Input { get; init; }

    public bool IsReference { get; init; }
}
