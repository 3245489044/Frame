namespace Rge.Unified.Models;

public sealed class ParamRef
{
    public string Target { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public bool Required { get; init; }

    public object? Fallback { get; init; }
}