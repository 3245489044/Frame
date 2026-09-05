namespace CSG.Unified;

public record DomainOptimizationHints
{
    public bool IsIdempotent { get; init; }

    public bool IsPureFunction { get; init; }

    public int SuggestedParallelism { get; init; } = 1;

    public bool SupportsIncrementalExecution { get; init; }

    public int EstimatedCost { get; init; } = 50;

    public static DomainOptimizationHints Default => new();

    public static DomainOptimizationHints PureFunction => new()
    {
        IsPureFunction = true,
        IsIdempotent = true,
        EstimatedCost = 10
    };

    public static DomainOptimizationHints Idempotent => new()
    {
        IsIdempotent = true,
        EstimatedCost = 30
    };

    public override string ToString()
        => $"PureFunc={IsPureFunction}, Idempotent={IsIdempotent}, " +
           $"Parallelism={SuggestedParallelism}, Cost={EstimatedCost}";
}
