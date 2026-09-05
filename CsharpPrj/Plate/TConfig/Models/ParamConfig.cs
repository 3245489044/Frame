
namespace TConfig;

public class ParamConfig : IParamConfig
{
    public required string Name { get; init; }

    public Type Type { get; init; } = typeof(object);

    public bool IsOptional { get; init; }

    public object? DefaultValue { get; init; }
}
