
using ParameterResolver.Abstractions;
using ParameterResolver.Matchers;

namespace ParameterResolver.Builder;

public class ParameterBuilder
{
    private readonly IParameterMatcher[] _matchers;

    public ParameterBuilder()
    {
        _matchers = new IParameterMatcher[]
        {
            new DictionaryMatcher(),
            new PositionalMatcher(),
            new SingleValueMatcher(),
            new DefaultValueMatcher()
        };
    }

    public ParameterBuilder(IParameterMatcher[] matchers)
    {
        _matchers = matchers ?? throw new ArgumentNullException(nameof(matchers));
    }


    public ParameterBuildResult Build(
        IReadOnlyList<IParameterDescriptor> functionParameters,
        object? parameters)
    {
        if (functionParameters == null)
            return ParameterBuildResult.Failure("FunctionParameters is null");

        return MatchParameters(functionParameters, parameters);
    }

    private ParameterBuildResult MatchParameters(
        IReadOnlyList<IParameterDescriptor> functionParameters,
        object? parameters)
    {
        if (functionParameters.Count == 0)
        {
            return ParameterBuildResult.Create(
                Array.Empty<object?>(),
                Array.Empty<ParameterMatchInfo>());
        }

        var args = new object?[functionParameters.Count];
        var matchInfos = new ParameterMatchInfo[functionParameters.Count];
        var matchedFlags = new bool[functionParameters.Count];

        for (int i = 0; i < functionParameters.Count; i++)
        {
            var p = functionParameters[i];
            matchInfos[i] = new ParameterMatchInfo(
                p.Name, p.Type, p.IsOptional,
                isMatched: false, matchSource: "None");
        }

        foreach (var matcher in _matchers)
        {
            matcher.TryMatch(functionParameters, parameters, args, matchedFlags, matchInfos);
        }

        for (int i = 0; i < functionParameters.Count; i++)
        {
            if (!matchedFlags[i] && !functionParameters[i].IsOptional)
            {
                var p = functionParameters[i];
                return ParameterBuildResult.Failure(
                    $"Required parameter '{p.Name}' (type: {p.Type.Name}) could not be resolved");
            }
        }

        return ParameterBuildResult.Create(args, matchInfos);
    }
}
