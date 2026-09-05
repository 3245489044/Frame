namespace CSG.Unified;

public record TransformOperation
{
    public required string Name { get; init; }

    public string Label { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Category { get; init; } = "transform";

    public required Type InputType { get; init; }

    public required Type OutputType { get; init; }

    public bool IsReversible { get; init; }

    public string? ReverseOperationName { get; init; }

    public string Version { get; init; } = "1.0";

    public string[] Tags { get; init; } = Array.Empty<string>();

    public override string ToString()
        => $"[{Category}] {Name}: {InputType.Name} → {OutputType.Name}";
}
