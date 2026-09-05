
using System.Collections;
using ParameterResolver.Abstractions;

namespace ParameterResolver.Matchers;

internal class PositionalMatcher : ParameterMatcherBase
{
    public override void TryMatch(
        IReadOnlyList<IParameterDescriptor> parameters,
        object? source,
        object?[] args,
        bool[] matchedFlags,
        ParameterMatchInfo[] matchInfos)
    {
        if (source is not IList list)
            return;

        int listIndex = 0;
        int paramIndex = 0;

        while (paramIndex < parameters.Count && listIndex < list.Count)
        {
            if (matchedFlags[paramIndex])
            {
                paramIndex++;
                continue;
            }

            var param = parameters[paramIndex];
            var rawValue = list[listIndex];
            var (converted, error) = Converter.TypeConverter.Convert(rawValue, param.Type);

            if (error != null)
            {
                if (!param.IsOptional)
                {
                    listIndex++;
                    continue;
                }
                paramIndex++;
                continue;
            }

            SetMatch(paramIndex, param, rawValue, converted,
                $"Positional(Index:{listIndex})", args, matchedFlags, matchInfos);
            listIndex++;
            paramIndex++;
        }
    }
}
