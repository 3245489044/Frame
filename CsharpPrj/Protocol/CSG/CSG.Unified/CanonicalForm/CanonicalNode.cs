namespace CSG.Unified;

public record CanonicalNode
{
    public required string Id { get; init; }

    public string Label { get; init; } = string.Empty;

    public CanonicalNodeType Type { get; init; } = CanonicalNodeType.Default;

    public Dictionary<string, object?> Properties { get; init; } = new();
}
