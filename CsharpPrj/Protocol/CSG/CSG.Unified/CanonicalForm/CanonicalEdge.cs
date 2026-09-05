namespace CSG.Unified;

public record CanonicalEdge
{
    public required string Id { get; init; }

    public required string SourceId { get; init; }

    public required string TargetId { get; init; }

    public string Label { get; init; } = string.Empty;

    public CanonicalEdgeType Type { get; init; } = CanonicalEdgeType.Default;

    public Dictionary<string, object?> Properties { get; init; } = new();

    public string? Condition { get; init; }

    public double Weight { get; init; } = 1.0;
}
