namespace CSG.Unified;


public record ParameterDeclaration
{
    public required string Name { get; init; }

    public string Type { get; init; } = "any";

    public bool Required { get; init; } = true;

    public object? Default { get; init; }

    public string Description { get; init; } = string.Empty;
}


public record ParameterValue
{
    public required string Name { get; init; }

    public object? Value { get; init; }

    public string Type { get; init; } = "any";
}

