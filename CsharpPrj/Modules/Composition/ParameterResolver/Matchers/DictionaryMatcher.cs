
using System.Collections;
using ParameterResolver.Abstractions;
using ParameterResolver.Converter;

namespace ParameterResolver.Matchers;

internal class DictionaryMatcher : ParameterMatcherBase
{
    public override void TryMatch(
        IReadOnlyList<IParameterDescriptor> parameters,
        object? source,
        object?[] args,
        bool[] matchedFlags,
        ParameterMatchInfo[] matchInfos)
    {
        if (source is not IDictionary dict)
            return;

        for (int i = 0; i < parameters.Count; i++)
        {
            if (matchedFlags[i])
                continue;

            var param = parameters[i];
            if (dict.Contains(param.Name))
            {
                var rawValue = dict[param.Name];
                ConvertAndSetMatch(i, param, rawValue, "Dictionary(Exact)", args, matchedFlags, matchInfos);
            }
        }
    }
}
