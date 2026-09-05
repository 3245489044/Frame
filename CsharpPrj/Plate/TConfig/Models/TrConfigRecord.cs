
namespace TConfig;

public class TrConfigRecord : ITrConfig
{
    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public Type InputType { get; init; } = typeof(object);

    public Type OutputType { get; init; } = typeof(object);

    public Type ReturnType => OutputType;

    public bool IsAsync { get; init; }

    public IReadOnlyList<IParamConfig> Parameters { get; init; } = Array.Empty<IParamConfig>();

    public object? Invoke(params object?[] args) => null;
}
