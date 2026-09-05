namespace Rge.Unified.Models;

public sealed class DependencyEdge
{
    public string From { get; init; } = string.Empty;

    public string To { get; init; } = string.Empty;

    public string? TargetParam { get; init; }

    public string? SourceKey { get; init; }
}