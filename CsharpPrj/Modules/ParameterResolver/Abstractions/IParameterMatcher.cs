
namespace ParameterResolver.Abstractions;

public interface IParameterMatcher
{
    void TryMatch(
        IReadOnlyList<IParameterDescriptor> parameters,
        object? source,
        object?[] args,
        bool[] matchedFlags,
        ParameterMatchInfo[] matchInfos);
}

public interface IParameterDescriptor
{
    string Name { get; }

    Type Type { get; }

    bool IsOptional { get; }

    object? DefaultValue { get; }
}
