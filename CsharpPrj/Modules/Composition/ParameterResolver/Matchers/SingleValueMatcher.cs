
using System.Collections;
using ParameterResolver.Abstractions;
using ParameterResolver.Converter;

namespace ParameterResolver.Matchers;

internal class SingleValueMatcher : ParameterMatcherBase
{
    public override void TryMatch(
        IReadOnlyList<IParameterDescriptor> parameters,
        object? source,
        object?[] args,
        bool[] matchedFlags,
        ParameterMatchInfo[] matchInfos)
    {
        if (parameters.Count != 1)
            return;

        if (matchedFlags[0])
            return;

        var param = parameters[0];

        if (source == null)
            return;

        if (source is IDictionary && !param.Type.IsInstanceOfType(source) &&
            param.Type != typeof(object))
            return;

        if (source is IList && !param.Type.IsInstanceOfType(source) &&
            param.Type != typeof(object))
            return;

        ConvertAndSetMatch(0, param, source, "SingleValue", args, matchedFlags, matchInfos);
    }
}
