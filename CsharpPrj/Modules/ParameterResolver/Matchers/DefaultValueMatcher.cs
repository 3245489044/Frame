
using ParameterResolver.Abstractions;

namespace ParameterResolver.Matchers;

internal class DefaultValueMatcher : IParameterMatcher
{
    public void TryMatch(
        IReadOnlyList<IParameterDescriptor> parameters,
        object? source,
        object?[] args,
        bool[] matchedFlags,
        ParameterMatchInfo[] matchInfos)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            if (matchedFlags[i])
                continue;

            var param = parameters[i];
            if (param.IsOptional)
            {
                args[i] = param.DefaultValue;
                matchedFlags[i] = true;
                matchInfos[i] = new ParameterMatchInfo(
                    param.Name, param.Type, param.IsOptional,
                    isMatched: true, matchSource: "Default",
                    convertedValue: param.DefaultValue);
            }
        }
    }
}
