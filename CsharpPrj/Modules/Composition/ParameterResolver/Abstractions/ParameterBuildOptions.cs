
namespace ParameterResolver.Abstractions;

public class ParameterBuildOptions
{
    public bool AllowPartialMatch { get; set; } = true;

    public bool UseDefaultValues { get; set; } = true;

    public StringComparison KeyComparison { get; set; } = StringComparison.OrdinalIgnoreCase;

    public bool ThrowOnError { get; set; } = false;

    public bool EnableDetailedMatchInfo { get; set; } = true;

    public static readonly ParameterBuildOptions Default = new();

    public static readonly ParameterBuildOptions Strict = new()
    {
        AllowPartialMatch = false,
        ThrowOnError = true
    };
}